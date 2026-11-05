/* JS <-> C# bridge.  Inside the app it talks to WebView2; in a plain browser it falls back to MockBackend (mock.js). */
(function () {
  'use strict';
  const wv = window.chrome && window.chrome.webview;
  const listeners = new Set();
  const pending = new Map();
  let seq = 0;

  function handle(msg) {
    if (!msg) return;
    if (msg.id) {
      const p = pending.get(msg.id);
      if (!p) return;
      pending.delete(msg.id);
      msg.ok ? p.resolve(msg.data) : p.reject(new Error(msg.error || 'Request failed'));
    } else if (msg.ev) {
      listeners.forEach((fn) => { try { fn(msg.ev, msg.data); } catch (e) { console.error(e); } });
    }
  }
  if (wv) wv.addEventListener('message', (e) => handle(e.data));

  window.Bridge = {
    isNative: !!wv,
    /** Request/response call. */
    call(cmd, args) {
      if (!wv) return window.MockBackend.call(cmd, args);
      return new Promise((resolve, reject) => {
        const id = ++seq;
        pending.set(id, { resolve, reject });
        wv.postMessage({ id, cmd, args });
      });
    },
    /** Fire-and-forget. */
    send(cmd, args) {
      if (wv) wv.postMessage({ cmd, args });
      else if (window.MockBackend) window.MockBackend.call(cmd, args).catch(() => {});
    },
    /** Subscribe to events pushed by the host: fn(name, data). Returns an unsubscribe function. */
    on(fn) { listeners.add(fn); return () => listeners.delete(fn); },
    _emit(ev, data) { handle({ ev, data }); },
  };
})();
