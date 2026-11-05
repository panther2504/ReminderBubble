// Update UI: banner when an update is ready, "Check now" flow, auto-update toggle. Runs against the mock backend.
const http = require('http'), fs = require('fs'), path = require('path'), assert = require('assert');
const { chromium } = require('playwright');
const ROOT = path.resolve(__dirname, '../../src/ReminderBubble/wwwroot'), TYPES = { '.html': 'text/html', '.js': 'text/javascript', '.css': 'text/css' };
const server = http.createServer((req, res) => {
  const f = path.join(ROOT, decodeURIComponent(req.url.split('?')[0]).replace(/^\/+/, '') || 'index.html');
  if (!f.startsWith(ROOT) || !fs.existsSync(f)) { res.writeHead(404); return res.end(); }
  res.writeHead(200, { 'Content-Type': TYPES[path.extname(f)] || 'application/octet-stream' }); fs.createReadStream(f).pipe(res);
});
(async () => {
  await new Promise((r) => server.listen(0, r));
  const base = `http://localhost:${server.address().port}`;
  const browser = await chromium.launch();
  const errors = [];
  const open = async (query) => {
    const page = await (await browser.newContext({ viewport: { width: 440, height: 700 }, deviceScaleFactor: 2 })).newPage();
    page.on('pageerror', (e) => errors.push(e.message)); page.on('console', (m) => m.type() === 'error' && errors.push(m.text()));
    await page.goto(`${base}/index.html${query}`); await page.waitForSelector('.card'); await page.waitForTimeout(900);
    return page;
  };

  // 1) nothing to update: no banner; Settings shows the version and can check
  let page = await open('');
  assert(!(await page.$eval('#upbar', (e) => e.classList.contains('show'))), 'no banner when up to date');
  await page.click('#btnSettings'); await page.waitForTimeout(600);
  assert(/Version/.test(await page.textContent('#upVer')));
  assert(await page.$eval('.switch[data-key=autoUpdate]', (e) => e.getAttribute('aria-checked') === 'true'), 'auto-update is on by default');
  await page.click('#upCheck');
  await page.waitForFunction(() => /Checking/.test(document.querySelector('#upCheck').textContent));
  await page.waitForFunction(() => /up to date/i.test(document.querySelector('#upStatus').textContent), null, { timeout: 4000 });
  await page.click('.switch[data-key=autoUpdate]'); await page.waitForTimeout(300);
  assert.strictEqual(await page.evaluate(() => JSON.parse(localStorage.getItem('rb.mock.settings')).autoUpdate), false, 'toggle is saved');
  await page.close();

  // 2) an update is ready: banner with Restart & update
  page = await open('?update=ready');
  assert(await page.$eval('#upbar', (e) => e.classList.contains('show')), 'banner shown');
  assert(/9\.9\.9/.test(await page.textContent('#upTitle')) && /Restart/.test(await page.textContent('#upBtn')));
  await page.locator('.app').screenshot({ path: path.join(__dirname, 'update-banner.png') });
  await page.close();

  console.log(errors.length ? 'errors: ' + errors.join('\n') : 'update UI ok');
  await browser.close(); server.close(); process.exit(errors.length ? 1 : 0);
})().catch((e) => { console.error('FAILED', e.message); process.exit(1); });
