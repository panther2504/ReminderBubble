/* Natural-language quick add:  "call mom tomorrow 5pm", "in 20 min stretch", "pay rent every month on the 1st"... */
(function (root) {
  'use strict';

  const DAYS = ['sunday', 'monday', 'tuesday', 'wednesday', 'thursday', 'friday', 'saturday'];
  const DAY_RE = '(sunday|monday|tuesday|wednesday|thursday|friday|saturday|sun|mon|tues|tue|wed|thurs|thur|thu|fri|sat)';
  const MONTHS = ['jan', 'feb', 'mar', 'apr', 'may', 'jun', 'jul', 'aug', 'sep', 'oct', 'nov', 'dec'];
  const MONTH_RE = '(january|february|march|april|may|june|july|august|september|october|november|december|jan|feb|mar|apr|jun|jul|aug|sept|sep|oct|nov|dec)';

  const dayIndex = (s) => DAYS.findIndex((d) => d.startsWith(s.toLowerCase().slice(0, 3)));
  const monthIndex = (s) => MONTHS.indexOf(s.toLowerCase().slice(0, 3));
  const startOfDay = (d) => new Date(d.getFullYear(), d.getMonth(), d.getDate());
  const addDays = (d, n) => { const x = new Date(d); x.setDate(x.getDate() + n); return x; };

  function dateOrder() {
    try {
      const parts = new Intl.DateTimeFormat(undefined).formatToParts(new Date(2000, 11, 25));
      const i = (t) => parts.findIndex((p) => p.type === t);
      return i('day') < i('month') ? 'dmy' : 'mdy';
    } catch { return 'mdy'; }
  }

  /**
   * @returns {{title:string, due:Date|null, repeat:string, hasTime:boolean, hasDay:boolean}}
   */
  function parseQuick(input, now = new Date(), opts = {}) {
    let text = ' ' + String(input || '').replace(/\s+/g, ' ').trim() + ' ';
    const original = text.trim();
    let repeat = 'none';
    let dayDate = null;          // Date at 00:00 of the chosen day
    let explicitDay = false;
    let hh = null, mm = 0;       // chosen time
    let rel = null;              // relative offset in ms
    let ampmGiven = false;
    let vague = null;            // morning / evening ...

    const cut = (re) => {
      const m = re.exec(text);
      if (!m) return null;
      text = text.slice(0, m.index) + ' ' + text.slice(m.index + m[0].length);
      return m;
    };

    // ---- repeats ------------------------------------------------------------------------
    let m;
    if ((m = cut(new RegExp('\\bevery\\s+(other\\s+)?' + DAY_RE + '\\b', 'i')))) {
      repeat = 'weekly'; dayDate = nextWeekday(now, dayIndex(m[2]), false); explicitDay = true;
    } else if ((m = cut(/\bevery\s+(week\s*days?|weekdays?)\b/i)) || (m = cut(/\b(?:on\s+)?weekdays\b/i))) {
      repeat = 'weekdays';
    } else if ((m = cut(/\bevery\s+(day|week|month|year)\b/i))) {
      repeat = { day: 'daily', week: 'weekly', month: 'monthly', year: 'yearly' }[m[1].toLowerCase()];
    } else if ((m = cut(/\b(daily|weekly|monthly|yearly|annually)\b/i))) {
      repeat = { daily: 'daily', weekly: 'weekly', monthly: 'monthly', yearly: 'yearly', annually: 'yearly' }[m[1].toLowerCase()];
    }

    // ---- relative: "in 20 min", "in an hour", "in 2 days" -------------------------------
    if ((m = cut(/\bin\s+(a|an|\d+(?:\.\d+)?)\s*(minutes?|mins?|m|hours?|hrs?|h|days?|d|weeks?|wks?|w)\b/i))) {
      const n = /^an?$/i.test(m[1]) ? 1 : parseFloat(m[1]);
      const u = m[2].toLowerCase()[0];
      const ms = u === 'm' ? 60e3 : u === 'h' ? 3600e3 : u === 'd' ? 86400e3 : 604800e3;
      rel = n * ms;
    }

    // ---- days ---------------------------------------------------------------------------
    if (rel == null) {
      if ((m = cut(/\bday\s+after\s+(tomorrow|tmrw|tmr)\b/i))) { dayDate = addDays(startOfDay(now), 2); explicitDay = true; }
      else if ((m = cut(/\b(tomorrow|tmrw|tmr)\b/i))) { dayDate = addDays(startOfDay(now), 1); explicitDay = true; }
      else if ((m = cut(/\btonight\b/i))) { dayDate = startOfDay(now); explicitDay = true; vague = 'night'; }
      else if ((m = cut(/\btoday\b/i))) { dayDate = startOfDay(now); explicitDay = true; }
      else if ((m = cut(/\bnext\s+week\b/i))) { dayDate = nextWeekday(now, 1, true); explicitDay = true; }
      else if ((m = cut(/\bnext\s+month\b/i))) { const d = startOfDay(now); d.setMonth(d.getMonth() + 1); dayDate = d; explicitDay = true; }
      else if ((m = cut(new RegExp('\\b(?:(on|next|this)\\s+)' + DAY_RE + '\\b', 'i')))) {
        dayDate = nextWeekday(now, dayIndex(m[2]), (m[1] || '').toLowerCase() === 'next'); explicitDay = true;
      } else if ((m = cut(new RegExp('\\b(sunday|monday|tuesday|wednesday|thursday|friday|saturday)\\b', 'i')))) {
        dayDate = nextWeekday(now, dayIndex(m[1]), false); explicitDay = true;
      } else if ((m = cut(new RegExp('\\b(?:on\\s+)?' + MONTH_RE + '\\.?\\s+(\\d{1,2})(?:st|nd|rd|th)?\\b(?:,?\\s+(\\d{4}))?', 'i')))) {
        dayDate = monthDay(now, monthIndex(m[1]), parseInt(m[2], 10), m[3]); explicitDay = !!dayDate;
      } else if ((m = cut(new RegExp('\\b(?:on\\s+(?:the\\s+)?)?(\\d{1,2})(?:st|nd|rd|th)?\\s+(?:of\\s+)?' + MONTH_RE + '\\b(?:,?\\s+(\\d{4}))?', 'i')))) {
        dayDate = monthDay(now, monthIndex(m[2]), parseInt(m[1], 10), m[3]); explicitDay = !!dayDate;
      } else if ((m = cut(/\b(\d{1,2})[\/.](\d{1,2})(?:[\/.](\d{2,4}))?\b/))) {
        const order = opts.dateOrder || dateOrder();
        const a = parseInt(m[1], 10), b = parseInt(m[2], 10);
        const [mo, da] = order === 'dmy' ? [b, a] : [a, b];
        if (mo >= 1 && mo <= 12 && da >= 1 && da <= 31) { dayDate = monthDay(now, mo - 1, da, m[3] && (m[3].length === 2 ? '20' + m[3] : m[3])); explicitDay = !!dayDate; }
        else text += ' ' + m[0];
      } else if ((m = cut(/\bon\s+the\s+(\d{1,2})(?:st|nd|rd|th)\b/i))) {
        const d = parseInt(m[1], 10);
        let t = new Date(now.getFullYear(), now.getMonth(), d);
        if (t < startOfDay(now)) t = new Date(now.getFullYear(), now.getMonth() + 1, d);
        dayDate = t; explicitDay = true;
      }
    }

    // ---- time of day --------------------------------------------------------------------
    if (rel == null) {
      if ((m = cut(/\b(?:at\s+|@\s*)?(\d{1,2})(?::(\d{2}))?\s*([ap])\.?m\.?\b/i))) {
        hh = parseInt(m[1], 10) % 12; if (m[3].toLowerCase() === 'p') hh += 12; mm = m[2] ? parseInt(m[2], 10) : 0; ampmGiven = true;
        if (parseInt(m[1], 10) > 12 || mm > 59) { hh = null; text += ' ' + m[0]; }
      } else if ((m = cut(/\b(?:at\s+)?([01]?\d|2[0-3]):([0-5]\d)\b/))) {
        hh = parseInt(m[1], 10); mm = parseInt(m[2], 10); ampmGiven = true;
      } else if ((m = cut(/\bnoon\b/i))) { hh = 12; mm = 0; ampmGiven = true; }
      else if ((m = cut(/\bmidnight\b/i))) { hh = 0; mm = 0; ampmGiven = true; }
      else if ((m = cut(/\b(?:eod|end of (?:the )?day)\b/i))) { hh = 17; mm = 0; ampmGiven = true; }
      else if ((m = cut(/\b(?:at|@)\s*(\d{1,2})\b(?!\s*[:\/.]\d)/i))) {
        hh = parseInt(m[1], 10); mm = 0;
        if (hh > 23) { hh = null; text += ' ' + m[0]; }
      } else if ((m = cut(/\b(morning|afternoon|evening|night)\b/i))) { vague = m[1].toLowerCase(); }
    }

    // ---- resolve ------------------------------------------------------------------------
    let due = null;
    let hasTime = false;
    if (rel != null) {
      due = new Date(Math.round((now.getTime() + rel) / 60e3) * 60e3);
      hasTime = true;
    } else if (hh != null || vague || dayDate) {
      const base = dayDate ? new Date(dayDate) : startOfDay(now);
      let H = hh, M = mm;
      if (H == null && vague) { H = { morning: 9, afternoon: 14, evening: 18, night: 21 }[vague]; M = 0; hasTime = true; }
      else if (H == null) { H = opts.defaultHour ?? 9; M = 0; }
      else hasTime = true;

      // "at 5" with no am/pm: pick the nearest sensible reading
      if (hh != null && !ampmGiven && H >= 1 && H <= 11) {
        const isToday = startOfDay(base).getTime() === startOfDay(now).getTime();
        const am = new Date(base); am.setHours(H, M, 0, 0);
        const pm = new Date(base); pm.setHours(H + 12, M, 0, 0);
        if (H >= 7) { H = isToday && am <= now ? H + 12 : H; }                 // 7-11: morning unless already past
        else { H = H + 12; }                                                   // 1-6: afternoon / evening
        void pm;
      }
      due = new Date(base); due.setHours(H, M, 0, 0);
      if (!explicitDay && due <= now) due = addDays(due, 1);                  // bare "at 9am" that has passed -> tomorrow
      if (explicitDay && repeat === 'none' && due <= now && vague == null && hasTime && startOfDay(due).getTime() === startOfDay(now).getTime()) {
        // "today at 8am" at 3pm: keep it (it fires immediately) - the user asked for it
      }
    } else if (repeat !== 'none') {
      due = new Date(now); due.setHours(opts.defaultHour ?? 9, 0, 0, 0);
      if (due <= now) due = addDays(due, 1);
    }

    // ---- tidy the title -----------------------------------------------------------------
    let title = text
      .replace(/\b(remind me|remember|reminder)\b\s*(to|about|that)?/ig, ' ')
      .replace(/\s+(at|on|by|in|for|next|this|every|from|@)\s*$/i, ' ')
      .replace(/^\s*(to|about|that)\s+/i, ' ')
      .replace(/\s+/g, ' ')
      .replace(/^[\s,.;:–-]+|[\s,.;:–-]+$/g, '');
    if (!title) title = original.replace(/^[\s,.;:–-]+|[\s,.;:–-]+$/g, '');
    if (title) title = title[0].toUpperCase() + title.slice(1);

    return { title, due, repeat, hasTime, hasDay: explicitDay };
  }

  function nextWeekday(now, idx, forceNext) {
    const today = startOfDay(now);
    let diff = (idx - today.getDay() + 7) % 7;
    if (diff === 0 && forceNext) diff = 7;
    return addDays(today, diff);
  }

  function monthDay(now, month, day, year) {
    if (month < 0 || day < 1 || day > 31) return null;
    const today = startOfDay(now);
    let y = year ? parseInt(year, 10) : today.getFullYear();
    let d = new Date(y, month, day);
    if (d.getMonth() !== month) return null;                      // e.g. 31 Feb
    if (!year && d < today) d = new Date(y + 1, month, day);      // already passed -> next year
    return d;
  }

  const api = { parseQuick };
  if (typeof module !== 'undefined' && module.exports) module.exports = api; else root.QuickParse = api;
})(typeof window !== 'undefined' ? window : globalThis);
