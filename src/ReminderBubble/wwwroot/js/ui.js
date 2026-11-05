/* UI primitives: ripple, popover, toast, keyed list reconciliation with FLIP motion. */
(function () {
  'use strict';
  const { h } = window.Core;
  const reduceMotion = () => window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;

  // ---- ripple on any .btn / .ibtn / .tab press -------------------------------------------
  document.addEventListener('pointerdown', (e) => {
    const t = e.target.closest('.btn, .ibtn, .tab, .seg button, .chipbtn');
    if (!t || t.disabled) return;
    const r = t.getBoundingClientRect();
    const size = Math.max(r.width, r.height) * 2;
    const s = h('span', 'ripple');
    s.style.cssText = `width:${size}px;height:${size}px;left:${e.clientX - r.left - size / 2}px;top:${e.clientY - r.top - size / 2}px`;
    if (getComputedStyle(t).position === 'static') t.style.position = 'relative';
    if (getComputedStyle(t).overflow === 'visible') t.style.overflow = 'hidden';
    t.appendChild(s);
    s.addEventListener('animationend', () => s.remove());
  }, { passive: true });

  // ---- popover ---------------------------------------------------------------------------
  let current = null;
  function closePopover(immediate) {
    if (!current) return;
    const { el, off } = current; current = null; off();
    if (immediate || reduceMotion()) el.remove();
    else { el.classList.add('out'); el.addEventListener('animationend', () => el.remove(), { once: true }); }
  }

  /**
   * Opens a small menu next to `anchor`. items: [{label, hint, onClick}] or '-' for a divider.
   * `host` is the positioned ancestor to render into (defaults to .app).
   */
  function popover(anchor, items, host = document.querySelector('.app') || document.body) {
    closePopover(true);
    const el = h('div', 'pop');
    items.forEach((it) => {
      if (it === '-') { el.appendChild(h('hr')); return; }
      const b = h('button', '', `<span>${window.Core.esc(it.label)}</span>${it.hint ? `<small>${window.Core.esc(it.hint)}</small>` : ''}`);
      b.addEventListener('click', () => { closePopover(); it.onClick && it.onClick(); });
      el.appendChild(b);
    });
    host.appendChild(el);

    const hr = host.getBoundingClientRect(), ar = anchor.getBoundingClientRect();
    const w = el.offsetWidth, hgt = el.offsetHeight;
    let left = ar.right - hr.left - w;                       // right-align with the anchor
    left = Math.max(10, Math.min(left, hr.width - w - 10));
    let top = ar.bottom - hr.top + 8, oy = '0%';
    if (top + hgt > hr.height - 10) { top = ar.top - hr.top - hgt - 8; oy = '100%'; }   // flip above when no room
    el.style.left = left + 'px'; el.style.top = Math.max(10, top) + 'px';
    el.style.setProperty('--ox', ((ar.left + ar.width / 2 - hr.left - left) / w * 100).toFixed(0) + '%');
    el.style.setProperty('--oy', oy);

    const onDown = (e) => { if (!el.contains(e.target) && !anchor.contains(e.target)) closePopover(); };
    const onKey = (e) => { if (e.key === 'Escape') { e.stopPropagation(); closePopover(); } };
    // capture phase so we run before other Escape handlers
    setTimeout(() => document.addEventListener('pointerdown', onDown, true), 0);
    document.addEventListener('keydown', onKey, true);
    current = { el, off: () => { document.removeEventListener('pointerdown', onDown, true); document.removeEventListener('keydown', onKey, true); } };
    return el;
  }

  // ---- toast -----------------------------------------------------------------------------
  let toastHost = null;
  function toast(text, action, ms = 5200) {
    if (!toastHost) { toastHost = h('div', 'toast-host'); (document.querySelector('.app') || document.body).appendChild(toastHost); }
    toastHost.querySelectorAll('.toast:not(.out)').forEach(dismiss);      // one at a time keeps it calm
    const t = h('div', 'toast');
    t.style.setProperty('--dur', ms + 'ms');
    t.appendChild(h('span', 'msg', window.Core.esc(text)));
    if (action) {
      const b = h('button', 'act', window.Core.esc(action.label));
      b.addEventListener('click', () => { dismiss(t); action.run(); });
      t.appendChild(b);
    }
    t.appendChild(h('i', 'bar'));
    toastHost.appendChild(t);
    t._timer = setTimeout(() => dismiss(t), ms);
    return t;
  }
  function dismiss(t) {
    if (t.classList.contains('out')) return;
    clearTimeout(t._timer);
    t.classList.add('out');
    t.addEventListener('animationend', () => t.remove(), { once: true });
  }

  // ---- keyed reconciliation with FLIP ----------------------------------------------------
  /**
   * Keeps `container`'s children in sync with `desired` ([{key, ...}]) without rebuilding what did not change:
   *  - new nodes animate in (class `enter`, staggered by `--i`)
   *  - removed nodes are lifted out of the flow and animate away (class `leave`, or `leave-<style>`)
   *  - surviving nodes that moved glide to their new place (FLIP)
   * make(d) -> element, update(el, d) -> void.
   */
  function createList(container, make, update) {
    const nodes = new Map();
    let firstPaint = true;

    function render(desired, opts = {}) {
      const animate = !reduceMotion() && !opts.instant;
      const swap = !!opts.swap;            // whole list replaced (tab change): drop old nodes at once, animate the new ones in
      const before = new Map();
      nodes.forEach((n, k) => before.set(k, n.getBoundingClientRect().top));
      const wanted = new Set(desired.map((d) => d.key));

      // 1) removals
      nodes.forEach((n, k) => {
        if (wanted.has(k)) return;
        nodes.delete(k);
        if (!animate || swap) { n.remove(); return; }
        const style = n.dataset.leave || 'out';
        n.style.cssText += `;position:absolute;top:${n.offsetTop}px;left:${n.offsetLeft}px;width:${n.offsetWidth}px;margin:0;pointer-events:none;z-index:0`;
        n.classList.add('leave', 'leave-' + style);
        setTimeout(() => n.remove(), 380);
      });

      // 2) create / update / order
      const live = (n) => { while (n && n.classList.contains('leave')) n = n.nextElementSibling; return n; };
      let cursor = live(container.firstElementChild);
      const fresh = new Set();
      desired.forEach((d, i) => {
        let n = nodes.get(d.key);
        if (!n) {
          n = make(d); n.dataset.key = d.key; nodes.set(d.key, n); fresh.add(d.key);
          if (animate) { n.classList.add('enter'); n.style.setProperty('--i', Math.min(i, 12)); n.addEventListener('animationend', () => n.classList.remove('enter'), { once: true }); }
        } else update(n, d);
        if (n === cursor) cursor = live(cursor.nextElementSibling);
        else container.insertBefore(n, cursor);
      });

      // 3) FLIP the survivors
      if (animate && !swap && !firstPaint) {
        nodes.forEach((n, k) => {
          if (fresh.has(k) || !before.has(k)) return;
          const dy = before.get(k) - n.getBoundingClientRect().top;
          if (Math.abs(dy) < 1) return;
          n.animate([{ transform: `translateY(${dy}px)` }, { transform: 'none' }], { duration: 460, easing: 'cubic-bezier(.22,1,.36,1)' });
        });
      }
      firstPaint = false;
    }
    return { render, nodes, get: (k) => nodes.get(k) };
  }

  window.UI = { popover, closePopover, toast, createList, reduceMotion };
})();
