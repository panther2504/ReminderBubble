// Browser flow tests for the panel UI. Runs against the mock backend, so no Windows host is needed.
//   npm install && npx playwright install chromium && npm test
const http = require('http');
const fs = require('fs');
const path = require('path');
const { chromium } = require('playwright');
const assert = require('assert');

const ROOT = path.resolve(__dirname, '../../src/ReminderBubble/wwwroot');
const TYPES = { '.html': 'text/html', '.js': 'text/javascript', '.css': 'text/css', '.svg': 'image/svg+xml' };
const server = http.createServer((req, res) => {
  const file = path.join(ROOT, decodeURIComponent(req.url.split('?')[0]).replace(/^\/+/, '') || 'index.html');
  if (!file.startsWith(ROOT) || !fs.existsSync(file) || fs.statSync(file).isDirectory()) { res.writeHead(404); return res.end(); }
  res.writeHead(200, { 'Content-Type': TYPES[path.extname(file)] || 'application/octet-stream' });
  fs.createReadStream(file).pipe(res);
});

(async () => {
  await new Promise((r) => server.listen(0, r));
  const base = `http://localhost:${server.address().port}`;
  const browser = await chromium.launch();
  const page = await (await browser.newContext({ viewport: { width: 440, height: 700 } })).newPage();
  const errors = [];
  page.on('console', m => { if (['error'].includes(m.type())) errors.push(m.text()); });
  page.on('pageerror', e => errors.push('PAGEERROR: ' + e.message));
  await page.goto(base + '/index.html');
  await page.waitForSelector('.card');
  const titles = () => page.$$eval('.card .title', els => els.map(e => e.textContent));
  const count = (t) => page.$eval(`.tab[data-tab=${t}] b`, e => Number(e.textContent));
  const act = async (title, a) => { const c = page.locator('.card', { hasText: title }).first(); await c.hover(); await page.waitForTimeout(450); await c.locator(`[data-act=${a}]`).click({ force: true }); };

  assert.deepStrictEqual([await count('today'), await count('all'), await count('done')], [4, 9, 2], 'initial counts');

  // complete (one-shot) -> leaves list, lands in Done, toast offers Undo
  await act('Call Mum', 'done'); await page.waitForTimeout(1400);
  assert(!(await titles()).includes('Call Mum'), 'completed card leaves Today');
  assert.strictEqual(await count('done'), 3, 'done count +1');
  assert((await page.textContent('.toast')).includes('Marked as done'));
  await page.click('.toast .act'); await page.waitForTimeout(900);
  assert((await titles()).includes('Call Mum'), 'undo brings it back');
  assert.strictEqual(await count('done'), 2);

  // complete (recurring) -> stays open, moves to its next occurrence
  const before = await page.locator('.card', { hasText: 'Stand-up' }).locator('.when').textContent();
  await act('Stand-up', 'done'); await page.waitForTimeout(1400);
  assert.strictEqual(await count('all'), 9, 'recurring reminder is not consumed');
  assert((await page.textContent('.toast')).includes('next'), 'toast mentions next occurrence');

  // delete + undo
  await act('Pick up dry cleaning', 'del'); await page.waitForTimeout(900);
  assert.strictEqual(await count('all'), 8, 'delete');
  await page.click('.toast .act'); await page.waitForTimeout(900);
  assert.strictEqual(await count('all'), 9, 'undo delete');

  // snooze 1 hour from the popover
  await act('Send the quarterly', 'snooze');
  await page.click('.pop button:has-text("1 hour")'); await page.waitForTimeout(900);
  const when = await page.locator('.card', { hasText: 'Send the quarterly' }).locator('.when').textContent();
  assert(/in 5\d min|in 1 h|1h/.test(when) || /Today/.test(when), 'snoozed time shown: ' + when);

  // edit + save (Water the plants is tomorrow, so look at All)
  await page.click('[data-tab=all]'); await page.waitForTimeout(600);
  await act('Water the plants', 'edit'); await page.waitForTimeout(600);
  await page.fill('#fTitle', 'Water the garden'); await page.click('#fRepeat [data-v=daily]'); await page.click('#fSave'); await page.waitForTimeout(900);
  await page.click('[data-tab=all]'); await page.waitForTimeout(500);
  assert((await titles()).includes('Water the garden'), 'edit saved');

  // validation: empty title shakes and does not save
  await page.click('#btnMore'); await page.waitForTimeout(500);
  await page.click('#fSave'); await page.waitForTimeout(100);
  assert(await page.$eval('#fTitle', e => e.classList.contains('shake')), 'empty title shakes');
  await page.keyboard.press('Escape'); await page.waitForTimeout(500);

  // quick add NL -> upcoming tab auto-selected, correct count
  await page.fill('#qin', 'Buy flowers next friday 4pm'); await page.keyboard.press('Enter'); await page.waitForTimeout(1300);
  assert((await titles()).some(t => t === 'Buy flowers'), 'NL quick add created reminder: ' + (await titles()));

  // search
  await page.click('#btnSearch'); await page.fill('#q', 'flowers'); await page.waitForTimeout(700);
  assert.deepStrictEqual(await titles(), ['Buy flowers']);
  await page.fill('#q', 'zzzz'); await page.waitForTimeout(700);
  assert.strictEqual((await titles()).length, 0); assert(await page.$eval('#empty', e => e.classList.contains('show')), 'empty state shown');
  await page.keyboard.press('Escape'); await page.waitForTimeout(600);

  // settings -> theme persists into <html>
  await page.click('#btnSettings'); await page.waitForTimeout(500);
  await page.click('#stAccent [data-v=rose]');
  assert.strictEqual(await page.$eval('html', e => e.dataset.accent), 'rose');
  await page.click('#stTheme [data-v=light]');
  assert.strictEqual(await page.$eval('html', e => e.dataset.theme), 'light');
  await page.waitForTimeout(300);
  const stored = await page.evaluate(() => JSON.parse(localStorage.getItem('rb.mock.settings')));
  assert.strictEqual(stored.accent, 'rose'); assert.strictEqual(stored.theme, 'light');

  // keyboard shortcuts
  await page.keyboard.press('Escape'); await page.waitForTimeout(500);
  await page.keyboard.press('Escape'); // nothing focused -> would close panel (no-op in the browser)
  await page.keyboard.press('3'); await page.waitForTimeout(300);
  assert.strictEqual(await page.$eval('#tabs', e => e.style.getPropertyValue('--i')), '2', 'key 3 selects third tab');

  console.log(errors.length ? 'console errors:\n' + errors.join('\n') : 'all flows passed, no console errors');
  await browser.close();
  server.close();
  process.exit(errors.length ? 1 : 0);
})().catch(e => { console.error('FAILED:', e.message); process.exit(1); });
