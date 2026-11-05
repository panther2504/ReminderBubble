/* Browser-only stand-in for the C# backend, so the UI can be opened and developed in any browser.
   Persists to localStorage and mirrors the host's behaviour (recurrence, snooze, due ticks). */
(function () {
  'use strict';
  if (window.chrome && window.chrome.webview) return;       // real host present - not needed

  const K = 'rb.mock.reminders', KS = 'rb.mock.settings', KN = 'rb.mock.next';
  const DEFAULTS = { theme: 'dark', accent: 'violet', bubbleSize: 64, bubbleOpacity: 1, snapToEdge: false, autoHide: false, autoUpdate: true, sound: true, startWithWindows: false, clock24: false, defaultSnooze: 10, welcomed: true };
  const iso = (d) => new Date(d).toISOString();
  const load = (k, d) => { try { return JSON.parse(localStorage.getItem(k)) ?? d; } catch { return d; } };
  const save = (k, v) => { try { localStorage.setItem(k, JSON.stringify(v)); } catch { /* private mode */ } };

  let items = load(K, null);
  let settings = Object.assign({}, DEFAULTS, load(KS, {}));
  let nextId = load(KN, 1);
  let update = { state: /update=ready/.test(location.search) ? 'ready' : 'idle', current: 'preview', latest: /update=ready/.test(location.search) ? '9.9.9' : null, error: null, lastChecked: null };

  function seed() {
    const now = new Date();
    const at = (dd, h, m) => { const d = new Date(now); d.setDate(d.getDate() + dd); d.setHours(h, m, 0, 0); return d; };
    const mk = (title, due, o = {}) => ({
      id: nextId++, title, notes: o.notes || '', dueAt: iso(due), repeat: o.repeat || 'none', color: o.color || 'violet', priority: o.priority ?? 1,
      isDone: !!o.isDone, notified: !!o.notified, completedAt: o.completedAt || null, createdAt: iso(now),
    });
    const past = new Date(now.getTime() - 22 * 60e3);
    items = [
      mk('Send the quarterly report', past, { notes: 'Attach the final PDF and CC finance.', color: 'rose', priority: 2, notified: true }),
      mk('Stand-up with the design team', new Date(now.getTime() + 38 * 60e3), { color: 'blue', repeat: 'weekdays' }),
      mk('Pick up dry cleaning', at(0, 18, 30), { color: 'amber', notes: 'Ticket is in the blue jacket.' }),
      mk('Call Mum', at(0, 20, 0), { color: 'green' }),
      mk('Renew car insurance', at(1, 9, 0), { color: 'teal', priority: 2, notes: 'Compare two quotes first.' }),
      mk('Water the plants', at(1, 8, 0), { color: 'green', repeat: 'weekly' }),
      mk('Dentist appointment', at(3, 15, 30), { color: 'violet' }),
      mk('Plan weekend trip', at(5, 19, 0), { color: 'amber', priority: 0 }),
      mk('Pay rent', at(9, 9, 0), { color: 'rose', repeat: 'monthly', priority: 2 }),
      mk('Morning stretch', at(-1, 7, 30), { color: 'teal', isDone: true, notified: true, completedAt: iso(at(0, 7, 41)) }),
      mk('Back up photos', at(-2, 21, 0), { color: 'blue', isDone: true, notified: true, completedAt: iso(at(-1, 10, 5)) }),
    ];
    persist();
  }
  const persist = () => { save(K, items); save(KN, nextId); };
  if (!items) seed();

  function nextOccurrence(due, repeat, now) {
    const a = new Date(due); let n = new Date(a);
    for (let i = 1; i < 5000; i++) {
      if (repeat === 'daily') { n = new Date(a); n.setDate(a.getDate() + i); }
      else if (repeat === 'weekly') { n = new Date(a); n.setDate(a.getDate() + 7 * i); }
      else if (repeat === 'monthly') { n = new Date(a); n.setMonth(a.getMonth() + i); }
      else if (repeat === 'yearly') { n = new Date(a); n.setFullYear(a.getFullYear() + i); }
      else if (repeat === 'weekdays') { do { n.setDate(n.getDate() + 1); } while ([0, 6].includes(n.getDay())); }
      else return a;
      if (n > now) break;
    }
    return n;
  }
  const find = (id) => items.find((r) => r.id === id);
  const changed = () => { persist(); window.Bridge._emit('changed'); };

  const handlers = {
    'app.ready': () => ({ version: 'preview', dbPath: '(browser preview - localStorage)', settings }),
    'reminders.list': () => items.slice().sort((a, b) => (a.isDone - b.isDone) || (new Date(a.dueAt) - new Date(b.dueAt))),
    'reminders.active': () => items.filter((r) => !r.isDone && r.notified),
    'reminders.add': (a) => {
      const r = { id: nextId++, title: String(a.title || '').trim(), notes: (a.notes || '').trim(), dueAt: iso(a.dueAt), repeat: a.repeat || 'none', color: a.color || 'violet', priority: a.priority ?? 1, isDone: false, notified: false, completedAt: null, createdAt: iso(new Date()) };
      if (!r.title) throw new Error('A reminder needs a title.');
      items.push(r); changed(); return r;
    },
    'reminders.update': (a) => {
      const r = find(a.id); if (!r) return null;
      const d = a.data; if (!String(d.title || '').trim()) throw new Error('A reminder needs a title.');
      if (iso(d.dueAt) !== r.dueAt) r.notified = false;
      Object.assign(r, { title: d.title.trim(), notes: (d.notes || '').trim(), dueAt: iso(d.dueAt), repeat: d.repeat, color: d.color, priority: d.priority });
      changed(); return r;
    },
    'reminders.delete': (a) => { items = items.filter((r) => r.id !== a.id); changed(); return null; },
    'reminders.complete': (a) => {
      const r = find(a.id); if (!r) return null; const now = new Date();
      if (r.repeat !== 'none') { r.dueAt = iso(nextOccurrence(r.dueAt, r.repeat, now)); r.notified = false; r.completedAt = iso(now); }
      else { r.isDone = true; r.notified = true; r.completedAt = iso(now); }
      changed(); return r;
    },
    'reminders.snooze': (a) => { const r = find(a.id); if (!r) return null; r.dueAt = iso(Date.now() + a.minutes * 60e3); r.notified = false; r.isDone = false; changed(); return r; },
    'reminders.restore': (a) => { items = items.filter((r) => r.id !== a.id); items.push(a); changed(); return a; },
    'reminders.clearDone': () => { items = items.filter((r) => !r.isDone); changed(); return null; },
    'update.status': () => update,
    'update.check': () => { update = Object.assign({}, update, { state: 'checking' }); window.Bridge._emit('update', update);
      setTimeout(() => { update = Object.assign({}, update, { state: /update=ready/.test(location.search) ? 'ready' : 'uptodate', latest: '9.9.9', lastChecked: new Date().toISOString() }); window.Bridge._emit('update', update); }, 900); return update; },
    'update.install': () => null,
    'settings.get': () => settings,
    'settings.patch': (a) => { settings = Object.assign({}, settings, a); save(KS, settings); window.Bridge._emit('settings', settings); return settings; },
  };

  window.MockBackend = {
    call(cmd, args) {
      return new Promise((resolve, reject) => {
        // a little latency makes the preview feel like the real thing and exposes race conditions early
        setTimeout(() => {
          try { resolve(handlers[cmd] ? JSON.parse(JSON.stringify(handlers[cmd](args || {}) ?? null)) : null); }
          catch (e) { reject(e); }
        }, 12);
      });
    },
  };

  // the scheduler tick
  setInterval(() => {
    const now = Date.now(); let any = false;
    items.forEach((r) => { if (!r.isDone && !r.notified && new Date(r.dueAt).getTime() <= now) { r.notified = true; any = true; } });
    if (any) changed();
  }, 1000);
})();
