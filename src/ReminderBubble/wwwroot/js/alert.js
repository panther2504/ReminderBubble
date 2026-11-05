/* Reminder Bubble - alert popup */
(function () {
  'use strict';
  const { $, $$, h, esc, icon, whenLabel, lcFirst, relShort, snoozeOptions, hydrate } = Core;
  const { createList } = UI;
  const MAX = 3;

  if (!Bridge.isNative) document.documentElement.classList.add('preview');

  const state = { items: [], settings: { defaultSnooze: 10 } };
  const COLORS = ['violet', 'blue', 'teal', 'green', 'amber', 'rose'];
  const alist = $('#alist');

  $('#bell').innerHTML = icon('bell');
  $('#aClose').innerHTML = icon('x');

  const view = createList(alist, make, update);

  // ---- data ------------------------------------------------------------------------------
  let refreshing = false, again = false;
  async function refresh() {
    if (refreshing) { again = true; return; }
    refreshing = true;
    try {
      const list = (await Bridge.call('reminders.active')).map(hydrate).sort((a, b) => a.due - b.due);
      state.items = list;
      render();
    } finally { refreshing = false; if (again) { again = false; refresh(); } }
  }

  // ---- rendering -------------------------------------------------------------------------
  function render() {
    const now = new Date();
    const shown = state.items.slice(0, MAX);
    view.render(shown.map((r) => ({ key: 'r:' + r.id, r, now })));

    const n = state.items.length;
    $('#aTitle').textContent = n > 1 ? `${n} reminders due` : 'Reminder due';
    $('#aSub').textContent = n ? (state.items[0].due > new Date(now.getTime() - 60e3) ? 'Right now' : `Since ${lcFirst(whenLabel(state.items[0].due, now))}`) : '';
    const more = n - shown.length;
    $('#afoot').hidden = more <= 0;
    $('#aOpen').innerHTML = more > 0 ? `+${more} more · open all ${icon('right')}` : '';
    if (!n && lastCount > 0) Bridge.send('alert.close');
    lastCount = n;
  }
  let lastCount = 0;

  function make(d) {
    const n = h('article', 'acard');
    n.dataset.id = d.r.id;
    update(n, d);
    return n;
  }

  function update(n, d) {
    const { r, now } = d;
    COLORS.forEach((c) => n.classList.remove('c-' + c));
    n.classList.add('c-' + r.color);
    const diff = r.due - now;
    const when = diff > -60e3 ? 'Due now' : `${relShort(diff)} overdue`;
    const snz = state.settings.defaultSnooze || 10;
    const sig = [r.title, r.notes, when, snz, +r.due].join('\u0001');
    if (n._sig === sig) return;
    n._sig = sig;
    const wasOpen = n.classList.contains('open');
    const opts = snoozeOptions(now).filter((o) => o.minutes !== snz).slice(0, 6);
    n.innerHTML =
      `<i class="stripe"></i><div class="at">${esc(r.title)}</div>` +
      (r.notes ? `<div class="an">${esc(r.notes)}</div>` : '') +
      `<div class="aw ${diff > -60e3 ? 'now' : ''}">${icon('clock')}${esc(when)}${r.repeat !== 'none' ? ' · repeats ' + esc(Core.REPEAT[r.repeat].toLowerCase()) : ''}</div>` +
      `<div class="aa"><button class="btn primary sm" data-act="done">${icon('check')}Done</button>` +
      `<button class="btn sm snz" data-act="snooze" data-min="${snz}">Snooze ${snz >= 60 ? snz / 60 + ' h' : snz + ' min'}</button>` +
      `<button class="btn sm more" data-act="more" title="More snooze options" aria-label="More snooze options">${icon('down')}</button></div>` +
      `<div class="srow2"><div>${opts.map((o) => `<button class="chipbtn" data-act="snooze" data-min="${o.minutes}">${esc(o.label)}</button>`).join('')}</div></div>`;
    n.classList.toggle('open', wasOpen);
  }

  // ---- actions ---------------------------------------------------------------------------
  alist.addEventListener('click', async (e) => {
    const b = e.target.closest('[data-act]'); if (!b) return;
    const card = b.closest('.acard');
    const id = Number(card.dataset.id);
    switch (b.dataset.act) {
      case 'more': card.classList.toggle('open'); break;
      case 'done':
        card.dataset.leave = 'done';
        await Bridge.call('reminders.complete', { id }).catch(console.error);
        break;
      case 'snooze':
        card.dataset.leave = 'snooze';
        await Bridge.call('reminders.snooze', { id, minutes: Number(b.dataset.min) }).catch(console.error);
        break;
    }
  });
  $('#aClose').addEventListener('click', () => Bridge.send('alert.close'));
  $('#aOpen').addEventListener('click', () => Bridge.send('alert.openPanel'));
  document.addEventListener('keydown', (e) => { if (e.key === 'Escape') Bridge.send('alert.close'); });

  // ---- keep the native window exactly as tall as the content -----------------------------
  let lastH = 0;
  new ResizeObserver(() => {
    const hgt = Math.ceil($('#alert').getBoundingClientRect().height);
    if (hgt && hgt !== lastH) { lastH = hgt; Bridge.send('alert.size', { height: hgt }); }
  }).observe($('#alert'));

  // ---- host events -----------------------------------------------------------------------
  Bridge.on((ev, data) => {
    if (ev === 'changed') refresh();
    else if (ev === 'settings') { state.settings = data; Core.applySettings(data); render(); }
    else if (ev === 'shown') {
      document.documentElement.classList.remove('paused');
      const app = $('#alert'); app.classList.remove('enter-all'); void app.offsetWidth; app.classList.add('enter-all');
      refresh();
    } else if (ev === 'hidden') document.documentElement.classList.add('paused');
  });

  setInterval(() => { if (!document.documentElement.classList.contains('paused')) render(); }, 15000);

  (async function init() {
    try { const info = await Bridge.call('app.ready'); state.settings = Object.assign(state.settings, info.settings); Core.applySettings(info.settings); } catch (e) { console.error(e); }
    await refresh();
  })();
})();
