/* Shared helpers: DOM, icons, dates & formatting, settings -> theme. */
(function () {
  'use strict';

  const $ = (s, r = document) => r.querySelector(s);
  const $$ = (s, r = document) => Array.from(r.querySelectorAll(s));
  function h(tag, cls, html) {
    const e = document.createElement(tag);
    if (cls) e.className = cls;
    if (html != null) e.innerHTML = html;
    return e;
  }
  const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));

  // ---- icons (24px grid, stroke) --------------------------------------------------------
  const P = {
    bell: '<path d="M6 8a6 6 0 0 1 12 0c0 7 3 9 3 9H3s3-2 3-9"/><path d="M10.3 21a1.94 1.94 0 0 0 3.4 0"/>',
    plus: '<path d="M5 12h14M12 5v14"/>',
    check: '<path d="M20 6 9 17l-5-5"/>',
    x: '<path d="M18 6 6 18M6 6l12 12"/>',
    trash: '<path d="M3 6h18"/><path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6"/><path d="M8 6V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"/>',
    clock: '<circle cx="12" cy="12" r="9.5"/><path d="M12 6.5V12l3.5 2"/>',
    repeat: '<path d="m17 2 4 4-4 4"/><path d="M3 11v-1a4 4 0 0 1 4-4h14"/><path d="m7 22-4-4 4-4"/><path d="M21 13v1a4 4 0 0 1-4 4H3"/>',
    sliders: '<path d="M4 21v-7M4 10V3M12 21v-9M12 8V3M20 21v-5M20 12V3M1 14h6M9 8h6M17 16h6"/>',
    pin: '<path d="M12 17v5"/><path d="M9 10.76a2 2 0 0 1-1.11 1.79l-1.78.9A2 2 0 0 0 5 15.24V16a1 1 0 0 0 1 1h12a1 1 0 0 0 1-1v-.76a2 2 0 0 0-1.11-1.79l-1.78-.9A2 2 0 0 1 15 10.76V7a1 1 0 0 1 1-1 2 2 0 0 0 0-4H8a2 2 0 0 0 0 4 1 1 0 0 1 1 1z"/>',
    search: '<circle cx="11" cy="11" r="7.5"/><path d="m21 21-4.3-4.3"/>',
    calendar: '<rect x="3" y="4.5" width="18" height="17" rx="3"/><path d="M16 2.5v4M8 2.5v4M3 10h18"/>',
    pencil: '<path d="M17 3a2.85 2.83 0 1 1 4 4L7.5 20.5 2 22l1.5-5.5Z"/><path d="m15 5 4 4"/>',
    down: '<path d="m6 9 6 6 6-6"/>',
    left: '<path d="m15 18-6-6 6-6"/>',
    right: '<path d="m9 18 6-6-6-6"/>',
    sparkle: '<path d="M12 3l1.8 5.2L19 10l-5.2 1.8L12 17l-1.8-5.2L5 10l5.2-1.8z"/><path d="M19 15l.7 2.3L22 18l-2.3.7L19 21l-.7-2.3L16 18l2.3-.7z"/>',
    up: '<path d="M12 19V5M5 12l7-7 7 7"/>',
    flag: '<path d="M4 22V4"/><path d="M4 4h13l-2.5 4.5L17 13H4"/>',
    moon: '<path d="M21 12.8A9 9 0 1 1 11.2 3a7 7 0 0 0 9.8 9.8z"/>',
    sun: '<circle cx="12" cy="12" r="4"/><path d="M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4"/>',
    monitor: '<rect x="2.5" y="3.5" width="19" height="13" rx="2.5"/><path d="M8 21h8M12 17v4"/>',
    power: '<path d="M12 2v10"/><path d="M18.4 6.6a9 9 0 1 1-12.8 0"/>',
    volume: '<path d="M11 5 6 9H2.5v6H6l5 4z"/><path d="M15.5 8.5a5 5 0 0 1 0 7M18.5 5.5a9 9 0 0 1 0 13"/>',
    undo: '<path d="M3 7v6h6"/><path d="M3 13a9 9 0 1 0 3-6.7L3 9"/>',
    list: '<path d="M8 6h13M8 12h13M8 18h13M3 6h.01M3 12h.01M3 18h.01"/>',
    zap: '<path d="M13 2 3 14h9l-1 8 10-12h-9z"/>',
    info: '<circle cx="12" cy="12" r="9.5"/><path d="M12 16v-4M12 8h.01"/>',
    external: '<path d="M15 3h6v6M10 14 21 3M18 14v5a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h5"/>',
    rocket: '<path d="M4.5 16.5c-1.5 1.3-2 5-2 5s3.7-.5 5-2c.7-.8.7-2.1-.1-2.9a2.2 2.2 0 0 0-2.9-.1z"/><path d="m12 15-3-3a22 22 0 0 1 2-3.95A12.9 12.9 0 0 1 22 2c0 2.7-.8 7.5-6 11a22 22 0 0 1-4 2z"/><path d="M9 12H4s.55-3.03 2-4c1.62-1.08 5 0 5 0M12 15v5s3.03-.55 4-2c1.08-1.62 0-5 0-5"/>',
  };
  const icon = (name, cls) => `<svg class="i${cls ? ' ' + cls : ''}" viewBox="0 0 24 24" aria-hidden="true">${P[name] || ''}</svg>`;

  // ---- dates --------------------------------------------------------------------------
  const startOfDay = (d) => { const x = new Date(d); x.setHours(0, 0, 0, 0); return x; };
  const addDays = (d, n) => { const x = new Date(d); x.setDate(x.getDate() + n); return x; };
  const sameDay = (a, b) => a.getFullYear() === b.getFullYear() && a.getMonth() === b.getMonth() && a.getDate() === b.getDate();
  const dayDiff = (a, b) => Math.round((startOfDay(a) - startOfDay(b)) / 86400e3);
  const pad2 = (n) => String(n).padStart(2, '0');

  const S = { clock24: false };          // updated from settings
  const timeFmt = () => new Intl.DateTimeFormat(undefined, { hour: 'numeric', minute: '2-digit', hour12: !S.clock24 });
  const fmtTime = (d) => timeFmt().format(d).replace(/\s([AaPp]\.?[Mm]\.?)$/, '\u202f$1');   // keep "AM" glued to the time
  const fmtDateShort = (d) => new Intl.DateTimeFormat(undefined, { weekday: 'short', month: 'short', day: 'numeric' }).format(d);
  const fmtDateLong = (d) => new Intl.DateTimeFormat(undefined, { weekday: 'long', month: 'long', day: 'numeric' }).format(d);
  const fmtMonth = (d) => new Intl.DateTimeFormat(undefined, { month: 'long', year: 'numeric' }).format(d);

  function relShort(ms) {
    const m = Math.round(Math.abs(ms) / 60e3);
    if (m < 1) return 'moments';
    if (m < 60) return m + ' min';
    const hrs = Math.floor(m / 60), rm = m % 60;
    if (m < 60 * 6) return hrs + (rm ? 'h ' + rm + 'm' : ' h');
    if (m < 60 * 24) return hrs + ' h';
    const dd = Math.round(m / 1440);
    return dd + (dd === 1 ? ' day' : ' days');
  }

  /** "Today, 5:00 PM" / "Tomorrow, 9:00 AM" / "Mon, Nov 9 · 9:00 AM" - plus a tone for colouring. */
  function whenLabel(due, now = new Date()) {
    const dd = dayDiff(due, now);
    const t = fmtTime(due);
    if (dd === 0) return `Today, ${t}`;
    if (dd === 1) return `Tomorrow, ${t}`;
    if (dd === -1) return `Yesterday, ${t}`;
    if (dd > 1 && dd < 7) return `${new Intl.DateTimeFormat(undefined, { weekday: 'long' }).format(due)}, ${t}`;
    return `${fmtDateShort(due)} · ${t}`;
  }

  /** Label for the card meta row. */
  function dueInfo(r, now = new Date()) {
    const due = r.due;
    const diff = due - now;
    if (!r.isDone && diff <= 0) {
      return { text: diff > -60e3 ? 'Due now' : `${relShort(diff)} overdue`, tone: 'danger', due: true };
    }
    if (!r.isDone && diff < 60 * 60e3) return { text: `in ${relShort(diff)}`, tone: 'accent', due: false };
    return { text: whenLabel(due, now), tone: dayDiff(due, now) === 0 ? 'accent' : '', due: false };
  }

  /** Snooze choices shared by the panel and the alert popup. */
  function snoozeOptions(now = new Date()) {
    const out = [];
    const add = (label, date) => {
      const sameDayHint = sameDay(date, now) ? fmtTime(date) : whenLabel(date, now);
      out.push({ label, hint: sameDayHint, date, minutes: Math.max(1, Math.ceil((date - now) / 60e3)) });
    };
    [['10 minutes', 10], ['30 minutes', 30], ['1 hour', 60], ['3 hours', 180]].forEach(([l, m]) => add(l, new Date(now.getTime() + m * 60e3)));
    const eve = new Date(now); eve.setHours(18, 0, 0, 0);
    if (eve - now > 3 * 3600e3 + 60e3) add('This evening', eve);
    const tom = addDays(startOfDay(now), 1); tom.setHours(9, 0, 0, 0); add('Tomorrow morning', tom);
    const mon = addDays(startOfDay(now), ((8 - now.getDay()) % 7) || 7); mon.setHours(9, 0, 0, 0); add('Next Monday', mon);
    return out;
  }

  const lcFirst = (s) => s.replace(/^./, (c) => c.toLowerCase());   // "Tomorrow, 9:00 AM" -> "tomorrow, 9:00 AM"

  const REPEAT = { none: 'Never', daily: 'Daily', weekdays: 'Weekdays', weekly: 'Weekly', monthly: 'Monthly', yearly: 'Yearly' };

  // ---- reminders from the host ----------------------------------------------------------
  function hydrate(r) {
    r.due = new Date(r.dueAt);
    r.completed = r.completedAt ? new Date(r.completedAt) : null;
    return r;
  }
  const dehydrate = (r) => { const { due, completed, ...rest } = r; return rest; };

  // ---- settings -> theme ----------------------------------------------------------------
  const mq = window.matchMedia ? window.matchMedia('(prefers-color-scheme: light)') : null;
  let lastSettings = null;
  function applySettings(s) {
    lastSettings = s || lastSettings;
    if (!lastSettings) return;
    const theme = lastSettings.theme === 'system' ? (mq && mq.matches ? 'light' : 'dark') : lastSettings.theme;
    document.documentElement.dataset.theme = theme;
    document.documentElement.dataset.accent = lastSettings.accent;
    S.clock24 = !!lastSettings.clock24;
  }
  if (mq && mq.addEventListener) mq.addEventListener('change', () => applySettings());

  window.Core = {
    $, $$, h, esc, icon, startOfDay, addDays, sameDay, dayDiff, pad2, fmtTime, fmtDateShort, fmtDateLong, fmtMonth,
    relShort, whenLabel, lcFirst, dueInfo, snoozeOptions, REPEAT, hydrate, dehydrate, applySettings, S,
  };
})();
