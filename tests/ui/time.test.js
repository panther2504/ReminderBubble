// Time picker: every minute/second selectable, typed hh:mm:ss, seconds kept through save. Runs against the mock backend.
const { chromium } = require('playwright'); const assert = require('assert'); const http = require('http'), fs = require('fs'), path = require('path');
const ROOT = path.resolve(__dirname, '../../src/ReminderBubble/wwwroot'), T = { '.html': 'text/html', '.js': 'text/javascript', '.css': 'text/css' };
const srv = http.createServer((q, r) => { const f = path.join(ROOT, q.url.split('?')[0].replace(/^\/+/, '') || 'index.html'); if (!fs.existsSync(f)) { r.writeHead(404); return r.end(); } r.writeHead(200, { 'Content-Type': T[path.extname(f)] || 'x/y' }); fs.createReadStream(f).pipe(r); });
(async () => {
  await new Promise(r => srv.listen(0, r)); const base = 'http://localhost:' + srv.address().port;
  const b = await chromium.launch(); const ctx = await b.newContext({ viewport: { width: 440, height: 700 }, deviceScaleFactor: 2 }); const p = await ctx.newPage();
  const errs = []; p.on('pageerror', e => errs.push(e.message)); p.on('console', m => m.type() === 'error' && errs.push(m.text()));
  await p.goto(base + '/index.html'); await p.waitForSelector('.card');
  await p.click('#btnMore'); await p.waitForTimeout(700);
  await p.fill('#fTitle', 'Seconds test'); await p.click('#pickTime'); await p.waitForTimeout(700);
  assert.strictEqual(await p.$$eval('#dbody [data-m]', e => e.length), 60, '60 minute buttons');
  assert.strictEqual(await p.$$eval('#dbody [data-s]', e => e.length), 60, '60 second buttons');
  await p.click('#dbody [data-m="47"]'); await p.click('#dbody [data-s="13"]');
  let t = await p.textContent('#timeText'); assert(/:47:13/.test(t), 'picked 47:13 -> ' + t);
  await p.waitForTimeout(500); 
  await p.fill('#tType', '7:05:30 pm'); await p.press('#tType', 'Tab');
  t = await p.textContent('#timeText'); assert(/7:05:30/.test(t) && /PM/i.test(t), 'typed time -> ' + t);
  await p.fill('#tType', '25:99'); await p.press('#tType', 'Tab');
  assert(await p.$eval('#tType', e => e.classList.contains('bad')), 'bad input flagged');
  assert(/7:05:30/.test(await p.textContent('#timeText')), 'bad input leaves time unchanged');
  await p.click('#fSave'); await p.waitForTimeout(1500);
  await p.click('[data-tab=all]'); await p.waitForTimeout(700);
  const when = await p.locator('.card', { hasText: 'Seconds test' }).locator('.when').textContent();
  assert(/7:05:30/.test(when), 'card shows seconds: ' + when);
  const iso = await p.evaluate(() => JSON.parse(localStorage.getItem('rb.mock.reminders')).find(r => r.title === 'Seconds test').dueAt);
  assert(new Date(iso).getSeconds() === 30, 'stored with seconds ' + iso);
  console.log(errs.length ? 'errors: ' + errs : 'time picker ok: ' + when); await b.close(); srv.close(); process.exit(errs.length ? 1 : 0);
})().catch(e => { console.error('FAILED', e.message); process.exit(1); });
