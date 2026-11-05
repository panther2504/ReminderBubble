/* Reminder Bubble - main panel */
(function () {
  'use strict';

  const {
    $, $$, h, esc, icon, startOfDay, addDays, sameDay, dayDiff, pad2, fmtTime, fmtTimeSec, fmtDateShort, fmtDateLong, fmtMonth,
    whenLabel, lcFirst, dueInfo, snoozeOptions, REPEAT, hydrate, dehydrate,
  } = Core;
  const { popover, closePopover, toast, createList } = UI;
  const { parseQuick } = QuickParse;

  if (!Bridge.isNative) document.documentElement.classList.add('preview');

  const COLORS = ['violet', 'blue', 'teal', 'green', 'amber', 'rose'];
  const TABS = ['today', 'upcoming', 'all', 'done'];
  const EXAMPLES = ['Remind me to…', 'Call mom tomorrow 5pm', 'Stand-up every weekday 9am', 'Stretch in 30 min', 'Dentist next friday 3pm', 'Pay rent every month'];
  const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

  const state = {
    reminders: [],
    settings: { theme: 'dark', accent: 'violet', bubbleSize: 64, bubbleOpacity: 1, snapToEdge: false, autoHide: false, sound: true, startWithWindows: false, clock24: false, defaultSnooze: 10 },
    info: { version: '', dbPath: '' },
    tab: 'today',
    query: '',
    completing: new Set(),
    pinned: false,
    flashId: null,
    update: { state: 'idle', current: '', latest: null, error: null, lastChecked: null },
  };

  const el = {
    list: $('#list'), empty: $('#empty'), tabs: $('#tabs'), q: $('#q'), searchbar: $('#searchbar'),
    qin: $('#qin'), go: $('#btnGo'), preview: $('#preview'), chips: $('#previewChips'),
    scrim: $('#scrim'), sheet: $('#sheet'), settings: $('#settings'),
  };

  // ---- static icons ----------------------------------------------------------------------
  $('#btnSearch').innerHTML = icon('search');
  $('#btnPin').innerHTML = icon('pin');
  $('#btnSettings').innerHTML = icon('sliders');
  $('#btnClose').innerHTML = icon('x');
  $('#searchIcon').innerHTML = icon('search');
  $('#sparkIcon').innerHTML = icon('sparkle');
  $('#btnMore').innerHTML = icon('calendar');
  $('#btnGo').innerHTML = icon('up');
  $('#shClose').innerHTML = icon('x');
  $('#fDelete').innerHTML = icon('trash');
  $('#icoCal').innerHTML = icon('calendar');
  $('#icoClock').innerHTML = icon('clock');
  $('#chev1').innerHTML = icon('down', 'chev');
  $('#chev2').innerHTML = icon('down', 'chev');

  // ======================================================================================
  // Data
  // ======================================================================================
  const find = (id) => state.reminders.find((r) => r.id === id);
  let refreshing = false, again = false;

  async function refresh(opts) {
    const list = await Bridge.call('reminders.list');
    state.reminders = list.map(hydrate);
    render(opts);
  }
  function queueRefresh() {                                    // coalesce bursts of change events
    if (refreshing) { again = true; return; }
    refreshing = true;
    refresh().catch(console.error).finally(() => { refreshing = false; if (again) { again = false; queueRefresh(); } });
  }
  const defaultDue = (now = new Date()) => { const d = new Date(now.getTime() + 60 * 60e3); d.setMinutes(Math.ceil(d.getMinutes() / 5) * 5, 0, 0); return d; };

  // ======================================================================================
  // Rendering
  // ======================================================================================
  function render(opts) {
    const now = new Date();
    renderHeader(now);
    renderTabs(now);
    renderList(now, opts);
  }

  function greeting(now) {
    const hr = now.getHours();
    return hr < 5 ? 'Good night' : hr < 12 ? 'Good morning' : hr < 17 ? 'Good afternoon' : hr < 22 ? 'Good evening' : 'Good night';
  }

  function renderHeader(now) {
    const open = state.reminders.filter((r) => !r.isDone);
    const eod = addDays(startOfDay(now), 1);
    const dueNow = open.filter((r) => r.due <= now).length;
    const todayLeft = open.filter((r) => r.due < eod).length;
    const doneToday = state.reminders.filter((r) => r.completed && sameDay(r.completed, now)).length;
    const total = todayLeft + doneToday;

    $('#greet').textContent = greeting(now);
    let sub;
    if (dueNow) sub = `<span class="bad">${dueNow} due</span>${todayLeft > dueNow ? `<span class="dot">•</span>${todayLeft - dueNow} later today` : ''}`;
    else if (todayLeft) sub = `${todayLeft} reminder${todayLeft === 1 ? '' : 's'} left today`;
    else if (open.length) sub = `All clear today<span class="dot">•</span>next ${esc(lcFirst(whenLabel(open[0].due, now)))}`;
    else sub = esc(fmtDateLong(now));
    const subEl = $('#sub');
    if (subEl._html !== sub) { subEl._html = sub; subEl.innerHTML = sub; }

    const ratio = total ? doneToday / total : 0;
    $('#ringFg').style.strokeDashoffset = String(125.66 * (1 - (total ? ratio : 1)));
    const rt = $('#ringText');
    const label = total ? `${doneToday}/${total}` : '✓';
    if (rt._label !== label) { rt._label = label; rt.innerHTML = total ? label : icon('check'); }
    $('#ring').title = total ? `${doneToday} of ${total} done today` : 'Nothing due today';
  }

  const counts = { today: -1, upcoming: -1, all: -1, done: -1 };
  function renderTabs(now) {
    const open = state.reminders.filter((r) => !r.isDone);
    const eod = addDays(startOfDay(now), 1);
    const c = { today: open.filter((r) => r.due < eod).length, upcoming: open.filter((r) => r.due >= eod).length, all: open.length, done: state.reminders.length - open.length };
    $$('.tab', el.tabs).forEach((b) => {
      const k = b.dataset.tab, badge = $('b', b);
      if (counts[k] !== c[k]) {
        if (counts[k] !== -1) { badge.classList.remove('bump'); void badge.offsetWidth; badge.classList.add('bump'); }
        counts[k] = c[k]; badge.textContent = c[k];
      }
    });
  }

  function visibleReminders(now) {
    const q = state.query.trim().toLowerCase();
    const eod = addDays(startOfDay(now), 1);
    let list = state.reminders;
    if (q) return list.filter((r) => (r.title + ' ' + r.notes).toLowerCase().includes(q)).sort((a, b) => (a.isDone - b.isDone) || (a.due - b.due));
    if (state.tab === 'done') return list.filter((r) => r.isDone).sort((a, b) => (b.completed || b.due) - (a.completed || a.due));
    list = list.filter((r) => !r.isDone);
    if (state.tab === 'today') list = list.filter((r) => r.due < eod);
    if (state.tab === 'upcoming') list = list.filter((r) => r.due >= eod);
    return list.sort((a, b) => a.due - b.due);
  }

  function buildGroups(items, now) {
    if (state.query.trim()) return !items.length ? [] : [{ id: 'results', title: items.length === 1 ? '1 result' : `${items.length} results`, items }];
    if (state.tab === 'done') return [{ id: 'done', title: 'Completed', items, action: 'Clear all' }];
    const order = ['due', 'today', 'tomorrow', 'week', 'later'];
    const titles = { due: 'Needs attention', today: 'Today', tomorrow: 'Tomorrow', week: 'This week', later: 'Later' };
    const map = new Map(order.map((k) => [k, []]));
    items.forEach((r) => {
      const dd = dayDiff(r.due, now);
      map.get(r.due <= now ? 'due' : dd === 0 ? 'today' : dd === 1 ? 'tomorrow' : dd < 7 ? 'week' : 'later').push(r);
    });
    return order.filter((k) => map.get(k).length).map((k) => ({ id: k, title: titles[k], tone: k === 'due' ? 'danger' : '', items: map.get(k) }));
  }

  const listView = createList(el.list, makeNode, updateNode);
  let lastTab = state.tab, lastQuery = state.query;

  function renderList(now, opts = {}) {
    const items = visibleReminders(now);
    const desired = [];
    buildGroups(items, now).forEach((g) => {
      desired.push({ key: 'h:' + g.id, type: 'head', title: g.title, count: g.items.length, tone: g.tone || '', action: g.action || '' });
      g.items.forEach((r) => desired.push({ key: 'r:' + r.id, type: 'card', r, now }));
    });
    const swap = opts.swap || lastTab !== state.tab || (lastQuery.trim() === '') !== (state.query.trim() === '');
    lastTab = state.tab; lastQuery = state.query;
    listView.render(desired, { swap });
    renderEmpty(items.length === 0);

    if (state.flashId != null) {
      const n = listView.get('r:' + state.flashId);
      if (n) {
        n.scrollIntoView({ block: 'nearest', behavior: UI.reduceMotion() ? 'auto' : 'smooth' });
        n.classList.remove('flash'); void n.offsetWidth; n.classList.add('flash');
        state.flashId = null;
      }
    }
  }

  function renderEmpty(isEmpty) {
    const q = state.query.trim();
    const t = q ? ['search', 'No matches', 'Try different words, or clear the search.']
      : state.tab === 'today' ? ['check', "You're all clear", 'Nothing is due today. Add a reminder below — try “Call mom tomorrow 5pm”.']
        : state.tab === 'upcoming' ? ['calendar', 'Nothing scheduled', 'Reminders for the days ahead will show up here.']
          : state.tab === 'done' ? ['check', 'Nothing completed yet', 'Finished reminders are kept here for you.']
            : ['bell', 'No reminders yet', 'Type something below and press Enter.'];
    if (el.empty._k !== t[0] + t[1]) {
      el.empty._k = t[0] + t[1];
      $('#emptyIcon').innerHTML = icon(t[0]); $('#emptyTitle').textContent = t[1]; $('#emptyText').textContent = t[2];
    }
    el.empty.classList.toggle('show', isEmpty);
  }

  // ---- list nodes ------------------------------------------------------------------------
  function makeNode(d) {
    const n = d.type === 'head' ? h('div', 'gh') : h('article', 'card');
    if (d.type === 'card') n.dataset.id = d.r.id;
    updateNode(n, d);
    return n;
  }
  function updateNode(n, d) { d.type === 'head' ? fillHead(n, d) : fillCard(n, d); }

  function fillHead(n, d) {
    const sig = [d.title, d.count, d.tone, d.action].join('|');
    if (n._sig === sig) return;
    n._sig = sig;
    n.classList.toggle('danger', d.tone === 'danger');
    n.innerHTML = `<span>${esc(d.title)}</span><span class="n">${d.count}</span><span class="sp"></span>${d.action ? `<button data-act="clear">${esc(d.action)}</button>` : ''}`;
  }

  function fillCard(n, d) {
    const { r, now } = d;
    const completing = state.completing.has(r.id);
    const info = r.isDone ? { text: 'Done ' + (r.completed ? lcFirst(whenLabel(r.completed, now)) : ''), tone: '' } : dueInfo(r, now);

    COLORS.forEach((c) => n.classList.remove('c-' + c));
    n.classList.add('c-' + r.color);
    n.classList.toggle('due', !r.isDone && !completing && r.due <= now);
    n.classList.toggle('done', r.isDone);
    n.classList.toggle('completing', completing);

    const sig = [r.title, r.notes, +r.due, r.repeat, r.priority, r.isDone, info.text, info.tone].join('\u0001');
    if (n._sig === sig) return;
    n._sig = sig;
    n.innerHTML =
      `<i class="stripe"></i>` +
      `<button class="check" data-act="${r.isDone ? 'undone' : 'done'}" aria-label="${r.isDone ? 'Mark as not done' : 'Mark as done'}"><svg viewBox="0 0 24 24"><path d="M5 12.5l4.5 4.5L19 7.5"/></svg></button>` +
      `<div class="body"><div class="title"><span>${esc(r.title)}</span></div>` +
      (r.notes ? `<div class="notes">${esc(r.notes)}</div>` : '') +
      `<div class="meta"><span class="when ${info.tone}">${icon('clock')}${esc(info.text)}</span>` +
      (r.repeat !== 'none' ? `<span class="chip">${icon('repeat')}${REPEAT[r.repeat]}</span>` : '') +
      (r.priority === 2 ? `<span class="chip danger">${icon('flag')}High</span>` : '') +
      `</div></div>` +
      `<div class="acts">` +
      (r.isDone
        ? `<button class="ibtn" data-act="undone" title="Restore">${icon('undo')}</button>`
        : `<button class="ibtn" data-act="snooze" title="Snooze">${icon('clock')}</button><button class="ibtn" data-act="edit" title="Edit">${icon('pencil')}</button>`) +
      `<button class="ibtn danger" data-act="del" title="Delete">${icon('trash')}</button></div>`;
  }

  // ======================================================================================
  // List interactions
  // ======================================================================================
  el.list.addEventListener('click', (e) => {
    const actEl = e.target.closest('[data-act]');
    if (actEl && actEl.dataset.act === 'clear') return void clearDone();
    const card = e.target.closest('.card');
    if (!card) return;
    const r = find(Number(card.dataset.id));
    if (!r) return;
    switch (actEl && actEl.dataset.act) {
      case 'done': complete(r, card); break;
      case 'undone': undone(r); break;
      case 'snooze': openSnooze(r, actEl); break;
      case 'edit': openSheet(r); break;
      case 'del': remove(r, card); break;
      default: if (!r.isDone) openSheet(r);
    }
  });
  el.list.addEventListener('pointermove', (e) => {              // cursor-following spotlight
    const c = e.target.closest('.card');
    if (!c) return;
    const b = c.getBoundingClientRect();
    c.style.setProperty('--mx', (e.clientX - b.left) + 'px');
    c.style.setProperty('--my', (e.clientY - b.top) + 'px');
  }, { passive: true });

  const restore = (snap) => Bridge.call('reminders.restore', snap).catch((e) => toast(e.message));

  async function complete(r, card) {
    if (state.completing.has(r.id)) return;
    const snap = dehydrate(r);
    state.completing.add(r.id);
    fillCard(card, { r, now: new Date() });            // plays the tick + strike-through
    await sleep(UI.reduceMotion() ? 0 : 640);
    card.dataset.leave = 'done';
    try {
      const next = await Bridge.call('reminders.complete', { id: r.id });
      state.completing.delete(r.id);
      await refresh();
      toast(r.repeat !== 'none' && next ? `Done · next ${whenLabel(new Date(next.dueAt))}` : 'Marked as done', { label: 'Undo', run: () => restore(snap) });
    } catch (e) { state.completing.delete(r.id); toast(e.message); refresh(); }
  }

  function undone(r) {
    const snap = Object.assign(dehydrate(r), { isDone: false, completedAt: null, notified: r.due <= new Date() });
    restore(snap).then(() => toast('Moved back to your list'));
  }

  function remove(r, card) {
    const snap = dehydrate(r);
    if (card) card.dataset.leave = 'del';
    Bridge.call('reminders.delete', { id: r.id }).then(() => toast('Reminder deleted', { label: 'Undo', run: () => restore(snap) })).catch((e) => toast(e.message));
  }

  function openSnooze(r, anchor) {
    const opts = snoozeOptions(new Date());
    popover(anchor, opts.map((o) => ({ label: o.label, hint: o.hint, onClick: () => snooze(r, o) })));
  }
  function snooze(r, o) {
    const snap = dehydrate(r);
    Bridge.call('reminders.snooze', { id: r.id, minutes: o.minutes })
      .then(() => toast(`Snoozed until ${lcFirst(whenLabel(o.date))}`, { label: 'Undo', run: () => restore(snap) }))
      .catch((e) => toast(e.message));
  }

  function clearDone() {
    const snaps = state.reminders.filter((r) => r.isDone).map(dehydrate);
    if (!snaps.length) return;
    Bridge.call('reminders.clearDone').then(() => toast(`Cleared ${snaps.length} completed`, {
      label: 'Undo', run: async () => { for (const s of snaps) await restore(s); },
    }));
  }

  // ======================================================================================
  // Tabs + search
  // ======================================================================================
  function setTab(t) {
    if (t === state.tab) return;
    state.tab = t;
    el.tabs.style.setProperty('--i', TABS.indexOf(t));
    $$('.tab', el.tabs).forEach((b) => { const on = b.dataset.tab === t; b.classList.toggle('on', on); b.setAttribute('aria-selected', on); });
    el.list.scrollTop = 0;
    render({ swap: true });
  }
  el.tabs.addEventListener('click', (e) => { const b = e.target.closest('.tab'); if (b) setTab(b.dataset.tab); });

  function toggleSearch(open) {
    open = open ?? !el.searchbar.classList.contains('open');
    el.searchbar.classList.toggle('open', open);
    $('#btnSearch').classList.toggle('on', open);
    if (open) setTimeout(() => el.q.focus(), 120);
    else if (state.query) { state.query = ''; el.q.value = ''; render(); }
  }
  $('#btnSearch').addEventListener('click', () => toggleSearch());
  el.q.addEventListener('input', () => { state.query = el.q.value; el.list.scrollTop = 0; render(); });

  // ======================================================================================
  // Quick add
  // ======================================================================================
  let parsed = null;
  function updatePreview() {
    const raw = el.qin.value;
    el.go.disabled = !raw.trim();
    parsed = raw.trim() ? parseQuick(raw) : null;
    if (!parsed) { el.preview.classList.remove('open'); return; }
    const due = parsed.due || defaultDue();
    const chips = [`<span class="chip accent">${icon('clock')}${esc(whenLabel(due))}</span>`];
    if (parsed.repeat !== 'none') chips.push(`<span class="chip accent">${icon('repeat')}${REPEAT[parsed.repeat]}</span>`);
    if (!parsed.due) chips.push(`<span class="chip">default time · edit with ${icon('calendar')}</span>`);
    const html = chips.join('');
    if (el.chips._html !== html) { el.chips._html = html; el.chips.innerHTML = html; }
    el.preview.classList.add('open');
  }
  el.qin.addEventListener('input', updatePreview);

  $('#quick').addEventListener('submit', async (e) => {
    e.preventDefault();
    const raw = el.qin.value.trim();
    if (!raw) return;
    const p = parseQuick(raw);
    const due = p.due || defaultDue();
    el.go.disabled = true;
    try {
      const r = await Bridge.call('reminders.add', { title: p.title, notes: '', dueAt: due.toISOString(), repeat: p.repeat, color: state.settings.accent, priority: 1 });
      el.go.classList.remove('sent'); void el.go.offsetWidth; el.go.classList.add('sent');
      el.qin.value = ''; updatePreview();
      state.flashId = r.id;
      showWhereItLives(due);
      toast(`Reminder set · ${whenLabel(due)}`, { label: 'Undo', run: () => Bridge.call('reminders.delete', { id: r.id }) });
      await refresh();
    } catch (err) { toast(err.message); el.go.disabled = false; }
  });

  /** After adding, make sure the tab being looked at will actually show the new reminder. */
  function showWhereItLives(due) {
    const isToday = due < addDays(startOfDay(new Date()), 1);
    if (state.query) { state.query = ''; el.q.value = ''; toggleSearch(false); }
    if (state.tab === 'done' || (state.tab === 'today' && !isToday) || (state.tab === 'upcoming' && isToday)) setTab(isToday ? 'today' : 'upcoming');
  }

  $('#btnMore').addEventListener('click', () => {
    const raw = el.qin.value.trim();
    const p = raw ? parseQuick(raw) : null;
    openSheet(null, p ? { title: p.title, due: p.due, repeat: p.repeat } : null);
    if (raw) { el.qin.value = ''; updatePreview(); }
  });

  // rotating placeholder
  let exIdx = 0;
  setInterval(() => {
    if (el.qin.value || document.hidden) return;
    el.qin.classList.add('swap');
    setTimeout(() => { exIdx = (exIdx + 1) % EXAMPLES.length; el.qin.placeholder = EXAMPLES[exIdx]; el.qin.classList.remove('swap'); }, 350);
  }, 4500);

  // ======================================================================================
  // Sheets
  // ======================================================================================
  let openSheetEl = null;
  function showSheet(s) {
    if (openSheetEl && openSheetEl !== s) openSheetEl.classList.remove('open');
    openSheetEl = s;
    el.scrim.classList.add('open');
    s.classList.add('open');
  }
  function hideSheet() {
    if (!openSheetEl) return;
    openSheetEl.classList.remove('open');
    openSheetEl = null;
    el.scrim.classList.remove('open');
    closePopover(true);
    if (!document.activeElement || document.activeElement === document.body) return;
    document.activeElement.blur();
  }
  el.scrim.addEventListener('click', hideSheet);
  $('#shClose').addEventListener('click', hideSheet);
  $('#fCancel').addEventListener('click', hideSheet);

  function placePill(seg) {
    const on = $('button.on', seg), pill = $('.pill', seg);
    if (!on || !pill) return;
    pill.style.width = on.offsetWidth + 'px';
    pill.style.transform = `translateX(${on.offsetLeft - 3}px)`;
  }

  // ---- edit / create sheet ---------------------------------------------------------------
  let draft = null, drawer = null, calMonth = null, quickSet = [];

  function openSheet(r, prefill) {
    closePopover(true);
    const now = new Date();
    draft = r
      ? { id: r.id, title: r.title, notes: r.notes, due: new Date(r.due), repeat: r.repeat, color: r.color, priority: r.priority, quick: null }
      : { id: null, title: (prefill && prefill.title) || '', notes: '', due: (prefill && prefill.due) || defaultDue(now), repeat: (prefill && prefill.repeat) || 'none', color: state.settings.accent, priority: 1, quick: null };
    quickSet = quickTimes(now);
    $('#shTitle').textContent = r ? 'Edit reminder' : 'New reminder';
    $('#fSave').textContent = r ? 'Save changes' : 'Add reminder';
    $('#fDelete').style.display = r ? '' : 'none';
    $('#fTitle').value = draft.title;
    $('#fNotes').value = draft.notes;
    growNotes();
    drawer = null;
    calMonth = new Date(draft.due.getFullYear(), draft.due.getMonth(), 1);
    renderSheet();
    syncDrawer();
    showSheet(el.sheet);
    requestAnimationFrame(() => placePill($('#fPrio')));
    setTimeout(() => { if (openSheetEl === el.sheet) $('#fTitle').focus({ preventScroll: true }); }, 380);
  }

  function quickTimes(now) {
    const out = [['In 15 min', new Date(now.getTime() + 15 * 60e3)], ['In 1 hour', new Date(now.getTime() + 60 * 60e3)]];
    const eve = new Date(now); eve.setHours(18, 0, 0, 0);
    if (eve - now > 45 * 60e3) out.push(['This evening', eve]);
    const tom = addDays(startOfDay(now), 1); tom.setHours(9, 0, 0, 0);
    out.push([`Tomorrow ${fmtTime(tom)}`, tom]);
    const mon = addDays(startOfDay(now), ((8 - now.getDay()) % 7) || 7); mon.setHours(9, 0, 0, 0);
    out.push([`Next ${new Intl.DateTimeFormat(undefined, { weekday: 'short' }).format(mon)} ${fmtTime(mon)}`, mon]);
    return out.map(([label, date]) => ({ label, date: (date.setSeconds(0, 0), date) }));
  }

  const dateLabel = (d) => { const dd = dayDiff(d, new Date()); return dd === 0 ? 'Today' : dd === 1 ? 'Tomorrow' : dd === -1 ? 'Yesterday' : fmtDateShort(d); };

  function renderSheet() {
    $('#dateText').textContent = dateLabel(draft.due);
    $('#timeText').textContent = fmtTimeSec(draft.due);

    $('#qchips').innerHTML = quickSet.map((q, i) => `<button type="button" class="chipbtn${draft.quick === i ? ' on' : ''}" data-i="${i}">${esc(q.label)}</button>`).join('');
    $('#fRepeat').innerHTML = Object.keys(REPEAT).map((k) => `<button type="button" class="chipbtn${draft.repeat === k ? ' on' : ''}" data-v="${k}">${REPEAT[k]}</button>`).join('');
    $('#fColor').innerHTML = COLORS.map((c) => `<button type="button" class="sw c-${c}${draft.color === c ? ' on' : ''}" data-v="${c}" aria-label="${c}">${icon('check')}</button>`).join('');
    $$('#fPrio button').forEach((b) => b.classList.toggle('on', Number(b.dataset.v) === draft.priority));
    placePill($('#fPrio'));
  }

  $('#qchips').addEventListener('click', (e) => {
    const b = e.target.closest('.chipbtn'); if (!b) return;
    const q = quickSet[Number(b.dataset.i)];
    draft.due = new Date(q.date); draft.quick = Number(b.dataset.i);
    calMonth = new Date(draft.due.getFullYear(), draft.due.getMonth(), 1);
    renderSheet(); if (drawer) renderDrawer();
  });
  $('#fRepeat').addEventListener('click', (e) => { const b = e.target.closest('.chipbtn'); if (b) { draft.repeat = b.dataset.v; renderSheet(); } });
  $('#fColor').addEventListener('click', (e) => { const b = e.target.closest('.sw'); if (b) { draft.color = b.dataset.v; renderSheet(); } });
  $('#fPrio').addEventListener('click', (e) => { const b = e.target.closest('button'); if (b) { draft.priority = Number(b.dataset.v); renderSheet(); } });

  const growNotes = () => { const t = $('#fNotes'); t.style.height = 'auto'; t.style.height = Math.min(120, t.scrollHeight + 2) + 'px'; };
  $('#fNotes').addEventListener('input', growNotes);

  // ---- date / time drawer ----------------------------------------------------------------
  $('#pickDate').addEventListener('click', () => toggleDrawer('date'));
  $('#pickTime').addEventListener('click', () => toggleDrawer('time'));

  function toggleDrawer(kind) { drawer = drawer === kind ? null : kind; syncDrawer(); if (drawer) renderDrawer(true); }
  function syncDrawer() {
    $('#drawer').classList.toggle('open', !!drawer);
    $('#pickDate').classList.toggle('open', drawer === 'date');
    $('#pickTime').classList.toggle('open', drawer === 'time');
  }
  function renderDrawer(fresh, dir) { drawer === 'date' ? renderCalendar(fresh, dir) : renderTime(fresh); }

  function firstDayOfWeek() {
    try { const l = new Intl.Locale(navigator.language); const wi = l.getWeekInfo ? l.getWeekInfo() : l.weekInfo; if (wi && wi.firstDay) return wi.firstDay % 7; } catch { /* older engine */ }
    return 0;
  }

  function renderCalendar(fresh, dir) {
    const fd = firstDayOfWeek();
    const first = new Date(calMonth.getFullYear(), calMonth.getMonth(), 1);
    const lead = (first.getDay() - fd + 7) % 7;
    const today = startOfDay(new Date());
    const dows = Array.from({ length: 7 }, (_, i) => new Intl.DateTimeFormat(undefined, { weekday: 'narrow' }).format(new Date(2023, 0, 1 + ((i + fd) % 7))));
    let cells = '';
    for (let i = 0; i < 42; i++) {
      const d = new Date(first.getFullYear(), first.getMonth(), 1 - lead + i);
      const cls = ['day', d.getMonth() !== first.getMonth() ? 'dim' : '', d < today ? 'past' : '', sameDay(d, today) ? 'today' : '', sameDay(d, draft.due) ? 'sel' : ''].filter(Boolean).join(' ');
      cells += `<button type="button" class="${cls}" data-t="${d.getTime()}">${d.getDate()}</button>`;
    }
    $('#dbody').innerHTML =
      `<div class="dbody"><div class="cal-head"><button type="button" class="ibtn" data-nav="-1" aria-label="Previous month">${icon('left')}</button><b>${esc(fmtMonth(first))}</b><button type="button" class="ibtn" data-nav="1" aria-label="Next month">${icon('right')}</button></div>` +
      `<div class="cal-grid ${dir === 1 ? 'r' : dir === -1 ? 'l' : ''}">${dows.map((x) => `<span class="dow">${x}</span>`).join('')}${cells}</div></div>`;
    if (!fresh) $('#dbody .dbody').style.animation = 'none';
  }
  $('#dbody').addEventListener('click', (e) => {
    if (drawer === 'date') {
      const nav = e.target.closest('[data-nav]');
      if (nav) { const dir = Number(nav.dataset.nav); calMonth = new Date(calMonth.getFullYear(), calMonth.getMonth() + dir, 1); renderCalendar(false, dir); return; }
      const day = e.target.closest('.day');
      if (day) {
        const d = new Date(Number(day.dataset.t));
        draft.due.setFullYear(d.getFullYear(), d.getMonth(), d.getDate());
        draft.quick = null; renderSheet(); renderCalendar(false);
        setTimeout(() => { if (drawer === 'date') { drawer = null; syncDrawer(); } }, 260);
      }
    } else if (drawer === 'time') {
      const b = e.target.closest('button'); if (!b) return;
      const d = draft.due, c24 = Core.S.clock24, pm = d.getHours() >= 12;
      if (b.dataset.h != null) { const v = Number(b.dataset.h); d.setHours(c24 ? v : (v % 12) + (pm ? 12 : 0)); }
      if (b.dataset.m != null) d.setMinutes(Number(b.dataset.m));
      if (b.dataset.s != null) d.setSeconds(Number(b.dataset.s));
      if (b.dataset.ap != null) { const h12 = d.getHours() % 12; d.setHours(h12 + (b.dataset.ap === 'pm' ? 12 : 0)); }
      d.setMilliseconds(0); draft.quick = null; renderSheet(); syncTimeSel(true); syncTimeInput();
    }
  });

  // typed entry: "7:05:30 pm", "19:05:30", "7:05" ...
  $('#dbody').addEventListener('change', (e) => {
    if (e.target.id !== 'tType') return;
    const m = /^\s*(\d{1,2})\s*[:.]\s*(\d{1,2})(?:\s*[:.]\s*(\d{1,2}))?\s*([ap])?\.?m?\.?\s*$/i.exec(e.target.value);
    let ok = !!m;
    if (m) {
      let hh = Number(m[1]); const mm = Number(m[2]), ss = Number(m[3] || 0), ap = m[4] && m[4].toLowerCase();
      if (ap) { if (hh < 1 || hh > 12) ok = false; hh = (hh % 12) + (ap === 'p' ? 12 : 0); }
      if (hh > 23 || mm > 59 || ss > 59) ok = false;
      if (ok) { draft.due.setHours(hh, mm, ss, 0); draft.quick = null; renderSheet(); syncTimeSel(true); }
    }
    e.target.classList.toggle('bad', !ok);
    if (ok) syncTimeInput();
  });
  function syncTimeInput() { const t = $('#tType'); if (t && document.activeElement !== t) { t.value = fmtTimeSec(draft.due); t.classList.remove('bad'); } }

  function renderTime(fresh) {
    const c24 = Core.S.clock24;
    const hours = c24 ? Array.from({ length: 24 }, (_, i) => i) : [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12];
    const sixty = Array.from({ length: 60 }, (_, i) => i);          // every minute and every second, 00-59
    $('#dbody').innerHTML =
      `<div class="dbody"><label class="ttype">${icon('clock')}<input id="tType" type="text" inputmode="numeric" autocomplete="off" spellcheck="false" aria-label="Type a time (hh:mm:ss)" placeholder="hh:mm:ss"></label>` +
      `<div class="tlabels${c24 ? ' c24' : ''}"><span>Hour</span><span>Min</span><span>Sec</span>${c24 ? '' : '<span></span>'}</div>` +
      `<div class="tcols${c24 ? ' c24' : ''}"><div class="tcol" data-col="h">${hours.map((v) => `<button type="button" data-h="${v}">${c24 ? pad2(v) : v}</button>`).join('')}</div>` +
      `<div class="tcol" data-col="m">${sixty.map((v) => `<button type="button" data-m="${v}">${pad2(v)}</button>`).join('')}</div>` +
      `<div class="tcol" data-col="s">${sixty.map((v) => `<button type="button" data-s="${v}">${pad2(v)}</button>`).join('')}</div>` +
      (c24 ? '' : `<div class="tcol ap"><button type="button" data-ap="am">AM</button><button type="button" data-ap="pm">PM</button></div>`) +
      `</div></div>`;
    if (!fresh) $('#dbody .dbody').style.animation = 'none';
    syncTimeSel(false);
    syncTimeInput();
  }
  function syncTimeSel(smooth) {
    const d = draft.due, c24 = Core.S.clock24;
    const h = c24 ? d.getHours() : (d.getHours() % 12 || 12);
    $$('#dbody [data-h]').forEach((b) => b.classList.toggle('on', Number(b.dataset.h) === h));
    $$('#dbody [data-m]').forEach((b) => b.classList.toggle('on', Number(b.dataset.m) === d.getMinutes()));
    $$('#dbody [data-s]').forEach((b) => b.classList.toggle('on', Number(b.dataset.s) === d.getSeconds()));
    $$('#dbody [data-ap]').forEach((b) => b.classList.toggle('on', (b.dataset.ap === 'pm') === (d.getHours() >= 12)));
    $$('#dbody .tcol:not(.ap)').forEach((col) => {
      const on = $('button.on', col);
      if (on) col.scrollTo({ top: on.offsetTop - col.clientHeight / 2 + on.offsetHeight / 2, behavior: smooth ? 'smooth' : 'auto' });
    });
  }

  // ---- save / delete ---------------------------------------------------------------------
  async function saveDraft() {
    const title = $('#fTitle').value.trim();
    if (!title) {
      const t = $('#fTitle'); t.classList.remove('shake'); void t.offsetWidth; t.classList.add('shake'); t.focus();
      return;
    }
    const payload = { title, notes: $('#fNotes').value.trim(), dueAt: draft.due.toISOString(), repeat: draft.repeat, color: draft.color, priority: draft.priority };
    const btn = $('#fSave'); btn.disabled = true;
    try {
      if (draft.id) {
        await Bridge.call('reminders.update', { id: draft.id, data: payload });
        toast('Changes saved');
      } else {
        const r = await Bridge.call('reminders.add', payload);
        state.flashId = r.id;
        toast(`Reminder set · ${whenLabel(draft.due)}`, { label: 'Undo', run: () => Bridge.call('reminders.delete', { id: r.id }) });
      }
      showWhereItLives(draft.due);
      hideSheet();
      await refresh();
    } catch (e) { toast(e.message); } finally { btn.disabled = false; }
  }
  $('#fSave').addEventListener('click', saveDraft);
  $('#fTitle').addEventListener('keydown', (e) => { if (e.key === 'Enter') { e.preventDefault(); saveDraft(); } });
  $('#fNotes').addEventListener('keydown', (e) => { if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) { e.preventDefault(); saveDraft(); } });
  $('#fDelete').addEventListener('click', () => {
    const r = find(draft.id); if (!r) return;
    hideSheet();
    setTimeout(() => remove(r, listView.get('r:' + r.id)), 250);
  });

  // ======================================================================================
  // Settings
  // ======================================================================================
  /** "Instant" = every minute (checks are conditional requests, so this stays within GitHub's rate limit). */
  const UPDATE_PRESETS = [{ label: 'Instant', min: 1 }, { label: '15 min', min: 15 }, { label: '1 hour', min: 60 }, { label: '6 hours', min: 360 }, { label: 'Daily', min: 1440 }];
  const MAX_UPDATE_MIN = 7 * 24 * 60;
  /** 90 -> {n: 90, unit: 1}, 120 -> {n: 2, unit: 60}, 2880 -> {n: 2, unit: 1440} */
  function splitInterval(m) { const unit = m % 1440 === 0 ? 1440 : m % 60 === 0 ? 60 : 1; return { n: m / unit, unit }; }
  function intervalText(m) {
    if (m <= 1) return 'every minute — near-instant';
    const { n, unit } = splitInterval(m);
    return `every ${n === 1 ? '' : n + ' '}${unit === 1440 ? (n === 1 ? 'day' : 'days') : unit === 60 ? (n === 1 ? 'hour' : 'hours') : (n === 1 ? 'minute' : 'minutes')}`;
  }

  const toggleRow = (key, label, sub) =>
    `<div class="srow"><div class="lbl"><b>${label}</b>${sub ? `<small>${sub}</small>` : ''}</div><button type="button" class="switch" role="switch" data-key="${key}" aria-checked="false" aria-label="${label}"></button></div>`;

  function buildSettings() {
    $('#setBody').innerHTML =
      `<div class="sh-head"><h2>Settings</h2><button class="ibtn" id="stClose" aria-label="Close">${icon('x')}</button></div>` +
      `<div class="group">` +
      `<div class="srow"><div class="lbl"><b>Theme</b><small>Dark, light, or follow Windows</small></div><div class="seg" id="stTheme"><i class="pill"></i><button type="button" data-v="dark">Dark</button><button type="button" data-v="light">Light</button><button type="button" data-v="system">Auto</button></div></div>` +
      `<div class="srow col"><div class="lbl"><b>Accent colour</b><small>Tints the bubble and the interface</small></div><div class="swatches" id="stAccent">${COLORS.map((c) => `<button type="button" class="sw c-${c}" data-v="${c}" aria-label="${c}">${icon('check')}</button>`).join('')}</div></div>` +
      `</div>` +
      `<div class="group">` +
      `<div class="srow col"><div class="lbl"><b>Bubble size</b><small>Also: scroll over the bubble, or drag the grip that appears on its corner</small></div><div class="rng"><input type="range" id="stSize" min="40" max="160" step="2" aria-label="Bubble size"><output id="stSizeOut"></output></div></div>` +
      `<div class="srow col"><div class="lbl"><b>Bubble opacity</b></div><div class="rng"><input type="range" id="stOpacity" min="35" max="100" step="5" aria-label="Bubble opacity"><output id="stOpacityOut"></output></div></div>` +
      toggleRow('snapToEdge', 'Snap to screen edge', 'Off: the bubble stays exactly where you drop it, anywhere on any screen') +
      toggleRow('autoHide', 'Tuck away when idle', 'Needs snapping - slides half off-screen until you hover it') +
      `</div>` +
      `<div class="group">` +
      toggleRow('sound', 'Alert sound', 'A soft chime when a reminder is due') +
      toggleRow('clock24', '24-hour clock') +
      toggleRow('startWithWindows', 'Start with Windows', 'Launches quietly when you sign in') +
      `<div class="srow col"><div class="lbl"><b>Default snooze</b><small>Used by the Snooze button on alerts</small></div><div class="wrap" id="stSnooze">${[5, 10, 15, 30, 60].map((m) => `<button type="button" class="chipbtn" data-v="${m}">${m === 60 ? '1 hour' : m + ' min'}</button>`).join('')}</div></div>` +
      `</div>` +
      `<div class="group">` +
      toggleRow('autoUpdate', 'Update automatically', 'Checks for new versions in the background and downloads them') +
      `<div class="srow col" id="upEveryRow"><div class="lbl"><b>Check for updates</b><small id="upEveryHint"></small></div>` +
      `<div class="wrap" id="upPresets">${UPDATE_PRESETS.map((p) => `<button type="button" class="chipbtn" data-m="${p.min}">${p.label}</button>`).join('')}</div>` +
      `<div class="every"><span>Every</span><input type="number" id="upEvery" min="1" step="1" inputmode="numeric" aria-label="Check interval">` +
      `<div class="seg" id="upUnit"><i class="pill"></i><button type="button" data-u="1">minutes</button><button type="button" data-u="60">hours</button><button type="button" data-u="1440">days</button></div></div></div>` +
      `<div class="srow"><div class="lbl"><b id="upVer">Version</b><small id="upStatus"></small></div><button type="button" class="btn" id="upCheck"></button></div>` +
      `</div>` +
      `<div class="group">` +
      `<div class="srow"><div class="lbl"><b>Try it out</b><small>Sends a reminder that is due right now</small></div><button type="button" class="btn" id="stTest">${icon('zap')}Send test</button></div>` +
      `<div class="srow"><div class="lbl"><b>Quit Reminder Bubble</b><small>No reminders until you start it again</small></div><button type="button" class="btn danger" id="stQuit">Quit</button></div>` +
      `</div>` +
      `<div class="about" id="stAbout"></div>`;
  }

  let lastPatch = 0, patchTimer = 0;
  const pendingPatch = {};
  function patch(obj, delay = 0) {
    Object.assign(state.settings, obj);
    Core.applySettings(state.settings);
    syncSettings();
    Object.assign(pendingPatch, obj);
    lastPatch = Date.now();
    clearTimeout(patchTimer);
    patchTimer = setTimeout(() => {
      const p = Object.assign({}, pendingPatch);
      Object.keys(pendingPatch).forEach((k) => delete pendingPatch[k]);
      Bridge.call('settings.patch', p).catch((e) => toast(e.message));
    }, delay);
  }

  function syncSettings() {
    const s = state.settings;
    $$('#stTheme button').forEach((b) => b.classList.toggle('on', b.dataset.v === s.theme));
    placePill($('#stTheme'));
    $$('#stAccent .sw').forEach((b) => b.classList.toggle('on', b.dataset.v === s.accent));
    $$('#setBody .switch').forEach((b) => b.setAttribute('aria-checked', String(!!s[b.dataset.key])));
    const tuck = $('#setBody .switch[data-key=autoHide]');
    if (tuck) { tuck.closest('.srow').style.opacity = s.snapToEdge ? '' : '.45'; tuck.disabled = !s.snapToEdge; }
    $$('#stSnooze .chipbtn').forEach((b) => b.classList.toggle('on', Number(b.dataset.v) === s.defaultSnooze));
    syncInterval();
    const size = $('#stSize'), op = $('#stOpacity');
    if (document.activeElement !== size) size.value = s.bubbleSize;
    if (document.activeElement !== op) op.value = Math.round(s.bubbleOpacity * 100);
    size.style.setProperty('--p', ((size.value - size.min) / (size.max - size.min) * 100) + '%');
    op.style.setProperty('--p', ((op.value - op.min) / (op.max - op.min) * 100) + '%');
    $('#stSizeOut').textContent = size.value + ' px';
    $('#stOpacityOut').textContent = op.value + '%';
    $('#stAbout').innerHTML = `Reminder Bubble ${esc(state.info.version || '')}<br>${esc(state.info.dbPath || '')}`;
  }

  let everyUnit = 1;     // the unit the user is working in; stays put while they type so 60 min does not turn into 1 hour mid-edit
  function syncInterval() {
    const s = state.settings, m = s.updateIntervalMinutes || 360, input = $('#upEvery');
    const editing = document.activeElement === input;
    if (!editing) everyUnit = splitInterval(m).unit;
    const unit = everyUnit, n = m / unit;
    $$('#upPresets .chipbtn').forEach((b) => b.classList.toggle('on', Number(b.dataset.m) === m));
    $$('#upUnit button').forEach((b) => b.classList.toggle('on', Number(b.dataset.u) === unit));
    placePill($('#upUnit'));
    if (!editing) input.value = Number.isInteger(n) ? n : Math.round(n * 100) / 100;
    input.max = Math.floor(MAX_UPDATE_MIN / unit);
    $('#upEveryHint').textContent = s.autoUpdate === false ? 'Automatic updates are off - use Check now' : `Checks ${intervalText(m)}`;
    const row = $('#upEveryRow'); row.style.opacity = s.autoUpdate === false ? '.45' : '';
    $$('#upEveryRow button, #upEvery').forEach((e) => { e.disabled = s.autoUpdate === false; });
  }

  /** Applies a number + unit from the custom fields (clamped to 1 minute .. 7 days). */
  function setIntervalFromFields(n, unit) {
    if (!Number.isFinite(n) || n < 1) return;
    patch({ updateIntervalMinutes: Math.min(MAX_UPDATE_MIN, Math.max(1, Math.round(n * unit))) }, 250);
  }

  function bindSettings() {
    const body = $('#setBody');
    body.addEventListener('click', (e) => {
      const t = e.target;
      if (t.closest('#stClose')) return hideSheet();
      const sw = t.closest('.switch');
      if (sw) {
        const key = sw.dataset.key, next = !state.settings[key];
        return patch(key === 'snapToEdge' && !next ? { snapToEdge: false, autoHide: false } : { [key]: next });
      }
      const th = t.closest('#stTheme button'); if (th) return patch({ theme: th.dataset.v });
      const ac = t.closest('#stAccent .sw'); if (ac) return patch({ accent: ac.dataset.v });
      const sn = t.closest('#stSnooze .chipbtn'); if (sn) return patch({ defaultSnooze: Number(sn.dataset.v) });
      if (t.closest('#stTest')) return void sendTest();
      if (t.closest('#upCheck')) return void updateAction();
      if (t.closest('#stQuit')) {
        const q = $('#stQuit');
        if (q._armed) return Bridge.send('app.quit');
        q._armed = true; q.textContent = 'Click again to quit';
        setTimeout(() => { q._armed = false; q.textContent = 'Quit'; }, 3000);
      }
    });
    $('#stSize').addEventListener('input', (e) => patch({ bubbleSize: Number(e.target.value) }, 60));
    $('#stOpacity').addEventListener('input', (e) => patch({ bubbleOpacity: Number(e.target.value) / 100 }, 60));
    $('#upPresets').addEventListener('click', (e) => { const b = e.target.closest('.chipbtn'); if (b) patch({ updateIntervalMinutes: Number(b.dataset.m) }); });
    $('#upEvery').addEventListener('input', (e) => setIntervalFromFields(Number(e.target.value), everyUnit));
    $('#upEvery').addEventListener('blur', syncInterval);
    $('#upUnit').addEventListener('click', (e) => { const b = e.target.closest('button'); if (!b) return; everyUnit = Number(b.dataset.u); setIntervalFromFields(Number($('#upEvery').value) || 1, everyUnit); });
  }

  async function sendTest() {
    try {
      await Bridge.call('reminders.add', { title: 'Test reminder ✨', notes: 'If you can see this, alerts are working.', dueAt: new Date().toISOString(), repeat: 'none', color: state.settings.accent, priority: 1 });
      toast('Test sent — watch the bubble');
      if (Bridge.isNative) setTimeout(() => { hideSheet(); setTimeout(() => Bridge.send('panel.close'), 250); }, 700);
    } catch (e) { toast(e.message); }
  }

  function openSettings() { closePopover(true); syncSettings(); showSheet(el.settings); requestAnimationFrame(() => placePill($('#stTheme'))); }
  $('#btnSettings').addEventListener('click', openSettings);

  // ======================================================================================
  // Updates
  // ======================================================================================
  const upLabel = (u) => ({
    idle: 'Not checked yet', checking: 'Checking for updates…', uptodate: "You're up to date",
    available: `Version ${u.latest} is available`, downloading: `Downloading version ${u.latest}…`,
    ready: `Version ${u.latest} is ready to install`, error: u.error || 'Could not check for updates',
  }[u.state] || '');

  function updateAction() {
    const u = state.update;
    if (u.state === 'ready') Bridge.send('update.install');
    else if (u.state !== 'checking' && u.state !== 'downloading') Bridge.call('update.check').then(setUpdate).catch(() => {});
  }

  function setUpdate(u) { state.update = u || state.update; renderUpdate(); }

  function renderUpdate() {
    const u = state.update, bar = $('#upbar');
    // banner: only when there is something to act on
    const show = u.state === 'ready' || u.state === 'available' || u.state === 'downloading';
    bar.classList.toggle('show', show);
    bar.classList.toggle('busy', u.state === 'downloading');
    if (show) {
      $('#upIcon').innerHTML = icon(u.state === 'downloading' ? 'repeat' : 'rocket');
      $('#upTitle').textContent = u.state === 'ready' ? `Update ${u.latest} is ready` : u.state === 'available' ? `Update ${u.latest} available` : `Downloading ${u.latest}…`;
      $('#upSub').textContent = u.state === 'ready' ? 'Restarts in a moment' : u.state === 'available' ? 'Download and install it now' : 'This only takes a moment';
      const b = $('#upBtn'); b.textContent = u.state === 'ready' ? 'Restart & update' : 'Download';
    }
    // settings rows
    const ver = $('#upVer'), st = $('#upStatus'), btn = $('#upCheck');
    if (!ver) return;
    ver.textContent = `Version ${u.current || state.info.version || ''}`;
    st.textContent = upLabel(u) + (u.state === 'uptodate' && u.lastChecked ? ` · checked ${Core.fmtTime(new Date(u.lastChecked))}` : '');
    st.classList.toggle('err', u.state === 'error');
    btn.disabled = u.state === 'checking' || u.state === 'downloading';
    btn.textContent = u.state === 'ready' ? 'Restart & update' : u.state === 'checking' ? 'Checking…' : u.state === 'downloading' ? 'Downloading…' : u.state === 'available' ? 'Download' : 'Check now';
    btn.classList.toggle('primary', u.state === 'ready' || u.state === 'available');
  }
  $('#upBtn').addEventListener('click', updateAction);

  // ======================================================================================
  // Window chrome
  // ======================================================================================
  $('#btnClose').addEventListener('click', () => Bridge.send('panel.close'));
  $('#btnPin').addEventListener('click', () => {
    state.pinned = !state.pinned;
    $('#btnPin').classList.toggle('on', state.pinned);
    $('#btnPin').title = state.pinned ? 'Pinned - stays open' : 'Keep this panel open';
    Bridge.send('panel.pin', { value: state.pinned });
  });
  $('#hdr').addEventListener('pointerdown', (e) => {
    if (e.button !== 0 || e.target.closest('button, input')) return;
    Bridge.send('window.drag');
  });

  document.addEventListener('keydown', (e) => {
    const typing = /^(INPUT|TEXTAREA)$/.test(document.activeElement && document.activeElement.tagName);
    if (e.key === 'Escape') {
      if (openSheetEl) hideSheet();
      else if (el.searchbar.classList.contains('open')) toggleSearch(false);
      else if (el.qin.value) { el.qin.value = ''; updatePreview(); }
      else Bridge.send('panel.close');
      return;
    }
    if (typing || e.ctrlKey || e.metaKey || e.altKey || openSheetEl) return;
    if (e.key === '/') { e.preventDefault(); toggleSearch(true); }
    else if (e.key === 'n' || e.key === 'N') { e.preventDefault(); el.qin.focus(); }
    else if (e.key >= '1' && e.key <= '4') setTab(TABS[Number(e.key) - 1]);
  });

  // ======================================================================================
  // Host events
  // ======================================================================================
  function replay() {
    const app = $('#app');
    app.classList.remove('enter-all'); void app.offsetWidth; app.classList.add('enter-all');
    Array.from(el.list.children).forEach((c, i) => c.style.setProperty('--r', Math.min(i, 12)));
    el.list.classList.remove('replay'); void el.list.offsetWidth; el.list.classList.add('replay');
    setTimeout(() => el.list.classList.remove('replay'), 1200);
  }

  function onShown() {
    document.documentElement.classList.remove('paused');
    queueRefresh();
    replay();
    if (!openSheetEl) setTimeout(() => el.qin.focus({ preventScroll: true }), 220);
  }
  function onHidden() {
    document.documentElement.classList.add('paused');
    closePopover(true);
    if (openSheetEl) hideSheet();
    if (el.searchbar.classList.contains('open')) toggleSearch(false);
  }

  Bridge.on((ev, data) => {
    switch (ev) {
      case 'changed': queueRefresh(); break;
      case 'settings':
        if (Date.now() - lastPatch > 700) { state.settings = data; Core.applySettings(data); syncSettings(); render(); }
        break;
      case 'update': setUpdate(data); break;
      case 'shown': onShown(); break;
      case 'hidden': onHidden(); break;
      case 'open':
        if (data === 'compose') setTimeout(() => openSheet(null), 120);
        else if (data === 'settings') setTimeout(openSettings, 120);
        break;
    }
  });

  // ======================================================================================
  // Start
  // ======================================================================================
  (async function init() {
    buildSettings();
    bindSettings();
    try {
      const info = await Bridge.call('app.ready');
      state.info = info; state.settings = Object.assign(state.settings, info.settings);
    } catch (e) { console.error(e); }
    Core.applySettings(state.settings);
    syncSettings();
    updatePreview();
    Bridge.call('update.status').then(setUpdate).catch(() => {});
    await refresh();
    replay();
    setInterval(() => { if (!document.hidden && !document.documentElement.classList.contains('paused')) render(); }, 15000);
  })();

  // handy for tests / the browser preview
  window.__rb = { state, setTab, openSheet, openSettings, toggleSearch, render, refresh };
})();
