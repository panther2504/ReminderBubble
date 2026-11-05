// Panel open/close ("genie") animation, page side: scales out of / into the bubble, tells the host when it starts, cleans up, honours reduced motion.
const { chromium } = require('playwright'); const assert = require('assert');
const http = require('http'), fs = require('fs'), path = require('path');
const ROOT = path.resolve(__dirname, '../../src/ReminderBubble/wwwroot'), T = { '.html': 'text/html', '.js': 'text/javascript', '.css': 'text/css' };
const srv = http.createServer((q, r) => { const f = path.join(ROOT, q.url.split('?')[0].replace(/^\/+/, '') || 'index.html'); if (!fs.existsSync(f)) { r.writeHead(404); return r.end(); } r.writeHead(200, { 'Content-Type': T[path.extname(f)] || 'x/y' }); fs.createReadStream(f).pipe(r); });
(async () => {
  await new Promise(r => srv.listen(0, r));
  const b = await chromium.launch(); const p = await (await b.newContext({ viewport: { width: 560, height: 700 }, deviceScaleFactor: 1 })).newPage();
  const errs = []; p.on('pageerror', e => errs.push(e.message)); p.on('console', m => m.type() === 'error' && errs.push(m.text()));
  await p.goto(`http://localhost:${srv.address().port}/index.html`); await p.waitForSelector('.card'); await p.waitForTimeout(1200);
  const sent = []; await p.exposeFunction('__sent', (c, a) => sent.push([c, a]));
  await p.evaluate(() => { const o = Bridge.send; Bridge.send = (c, a) => { window.__sent(c, a); o(c, a); }; });

  const frame = async (name, t) => { await p.evaluate((t) => { document.getAnimations().filter(a => a.effect && a.effect.target && a.effect.target.id === 'app').forEach(a => { a.pause(); a.currentTime = t; }); }, t); await p.waitForTimeout(60); if (process.env.GENIE_SHOTS) await p.screenshot({ path: name }); };

  // ---- open: bubble is to the left of the panel (ox<0), vertically in the middle
  await p.evaluate(() => Bridge._emit('expand', { ox: -40, oy: 330, dur: 460 }));
  await p.waitForTimeout(30);
  assert(sent.some(([c]) => c === 'anim.begin'), 'page tells the host its animation began');
  const names = [];
  for (const [i, t] of [[0, 8], [1, 40], [2, 80], [3, 140], [4, 230], [5, 460]]) { await frame(`g-open-${i}.png`, t); names.push(`g-open-${i}.png`); }
  // let it finish naturally and verify it cleans up
  await p.evaluate(() => document.getAnimations().forEach(a => a.play())); await p.waitForTimeout(700);
  const after = await p.$eval('#app', e => ({ t: getComputedStyle(e).transform, o: e.style.transformOrigin, op: getComputedStyle(e).opacity }));
  assert.strictEqual(after.t, 'none', 'transform cleared after opening'); assert.strictEqual(after.o, '', 'origin cleared'); assert.strictEqual(after.op, '1');

  // ---- close: shrinks back to the bubble and stays invisible
  sent.length = 0;
  await p.evaluate(() => Bridge._emit('collapse', { ox: -40, oy: 330, dur: 340 }));
  await p.waitForTimeout(30); assert(sent.some(([c]) => c === 'anim.begin'));
  for (const [i, t] of [[0, 60], [1, 130], [2, 200], [3, 270]]) await frame(`g-close-${i}.png`, t);
  await p.evaluate(() => document.getAnimations().forEach(a => a.play())); await p.waitForTimeout(600);
  const closed = await p.$eval('#app', e => getComputedStyle(e).opacity); assert.strictEqual(closed, '0', 'collapsed content stays invisible');

  // ---- reduced motion: instant
  await p.emulateMedia({ reducedMotion: 'reduce' }); sent.length = 0;
  await p.evaluate(() => Bridge._emit('expand', { ox: -40, oy: 330, dur: 460 })); await p.waitForTimeout(100);
  assert.deepStrictEqual(sent.find(([c]) => c === 'anim.begin')[1], { instant: true }, 'reduced motion -> instant');
  console.log(errs.length ? 'errors ' + errs : 'genie page animation ok'); await b.close(); srv.close();
})().catch(e => { console.error('FAILED', e.message); process.exit(1); });
