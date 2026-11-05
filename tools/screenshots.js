// Regenerates docs/*.jpg from the real UI (mock backend). Usage: cd tests/ui && node ../../tools/screenshots.js
const http = require('http'), fs = require('fs'), path = require('path');
const { chromium } = require('playwright');
const ROOT = path.resolve(__dirname, '../src/ReminderBubble/wwwroot'), OUT = path.resolve(__dirname, '../docs');
const TYPES = { '.html': 'text/html', '.js': 'text/javascript', '.css': 'text/css' };
const server = http.createServer((req, res) => {
  const f = path.join(ROOT, decodeURIComponent(req.url.split('?')[0]).replace(/^\/+/, '') || 'index.html');
  if (!f.startsWith(ROOT) || !fs.existsSync(f)) { res.writeHead(404); return res.end(); }
  res.writeHead(200, { 'Content-Type': TYPES[path.extname(f)] || 'application/octet-stream' }); fs.createReadStream(f).pipe(res);
});
(async () => {
  await new Promise((r) => server.listen(0, r));
  const base = `http://localhost:${server.address().port}`;
  const browser = await chromium.launch();
  const ctx = await browser.newContext({ viewport: { width: 440, height: 700 }, deviceScaleFactor: 2 });
  const page = await ctx.newPage();
  const shot = async (name, sel = '.app') => { await page.waitForTimeout(900); await page.locator(sel).screenshot({ path: path.join(OUT, name + '.png') }); };

  await page.goto(base + '/index.html'); await page.waitForSelector('.card'); await page.waitForTimeout(1200);
  await shot('panel-dark');
  await page.click('#btnSettings'); await page.waitForTimeout(400); await page.click('#stTheme [data-v=light]'); await page.click('#stAccent [data-v=blue]');
  await page.keyboard.press('Escape'); await shot('panel-light');
  await page.click('#btnSettings'); await page.waitForTimeout(400); await page.click('#stTheme [data-v=dark]'); await page.click('#stAccent [data-v=violet]'); await page.keyboard.press('Escape'); await page.waitForTimeout(600);
  await page.fill('#qin', 'Call mom tomorrow 5pm every week'); await shot('quick-add');
  await page.fill('#qin', ''); await page.click('[data-tab=all]'); await page.waitForTimeout(500);
  const c = page.locator('.card', { hasText: 'Dentist' }); await c.hover(); await page.waitForTimeout(450); await c.locator('[data-act=edit]').click({ force: true });
  await page.waitForTimeout(700); await page.click('#pickDate'); await shot('edit-sheet');

  const alert = await ctx.newPage(); await alert.setViewportSize({ width: 388, height: 560 });
  await alert.goto(base + '/alert.html'); await alert.waitForTimeout(500);
  await alert.evaluate(async () => { await MockBackend.call('reminders.add', { title: 'Stand-up with design', notes: 'Zoom link is in the invite', dueAt: new Date(Date.now() - 3 * 60e3).toISOString(), repeat: 'weekdays', color: 'blue', priority: 1 }); });
  await alert.waitForTimeout(2000);
  const h = Math.ceil(await alert.evaluate(() => document.getElementById('alert').getBoundingClientRect().height));
  await alert.screenshot({ path: path.join(OUT, 'alert.png'), clip: { x: 0, y: 0, width: 388, height: h } });
  await browser.close(); server.close();
})();
