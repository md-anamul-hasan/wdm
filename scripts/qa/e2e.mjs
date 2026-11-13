// WDM 2.5.5 end-to-end QA: drives the real release app (Tauri + WebView2) over CDP.
import { fileURLToPath } from 'node:url';
const HERE = fileURLToPath(new URL('.', import.meta.url)).replace(/\\/g, '/');
const REPO = HERE + '../../';
import { spawn, execSync } from 'node:child_process';
import fs from 'node:fs';
import net from 'node:net';
import os from 'node:os';
import path from 'node:path';
import { createRequire } from 'node:module';

const require = createRequire(REPO + 'package.json');
const { chromium } = require('playwright');

const EXE = process.env.WDM_EXE || REPO + 'target/release/wdm-desktop.exe';
const ROOT = fs.mkdtempSync(path.join(os.tmpdir(), 'wdm-e2e-'));
const SRV = 'http://127.0.0.1:8765';
const PIPE = `\\\\.\\pipe\\WDM.Ipc.${process.env.USERNAME}`;
const results = [];
const check = (name, ok, extra = '') => { results.push(ok); console.log(`${ok ? 'PASS' : 'FAIL'}  ${name}${extra ? '  -> ' + extra : ''}`); };
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const expectedByte = (i) => i % 251;

function sendPipe(obj) {
  return new Promise((resolve, reject) => {
    const c = net.connect(PIPE, () => { c.end(JSON.stringify(obj)); });
    c.on('close', resolve); c.on('error', reject);
  });
}

async function startApp(dataDir, extraArgs = []) {
  fs.mkdirSync(dataDir, { recursive: true });
  const child = spawn(EXE, extraArgs, {
    env: { ...process.env, WDM_DATA_DIR: dataDir, WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS: '--remote-debugging-port=9222' },
    stdio: 'ignore',
  });
  let browser;
  for (let i = 0; i < 60; i++) {
    try { browser = await chromium.connectOverCDP('http://127.0.0.1:9222'); break; } catch { await sleep(500); }
  }
  if (!browser) throw new Error('could not attach to WebView2');
  let page;
  for (let i = 0; i < 40 && !page; i++) {
    page = browser.contexts().flatMap((c) => c.pages()).find((p) => !p.url().startsWith('devtools') && p.url() !== 'about:blank'); if (!page && i === 39) console.log('pages:', browser.contexts().flatMap((c) => c.pages()).map((p) => p.url()));
    if (!page) await sleep(250);
  }
  await page.waitForSelector('text=Add URL', { timeout: 15000 });
  return { child, browser, page };
}
function killApp() { try { execSync('taskkill /F /IM wdm-desktop.exe', { stdio: 'ignore' }); } catch {} }
const rowText = async (page, name) => (await page.locator('.download-row', { hasText: name }).first().innerText().catch(() => '')).replace(/\s+/g, ' ');
async function waitRow(page, name, re, ms = 60000) {
  const end = Date.now() + ms;
  while (Date.now() < end) { const t = await rowText(page, name); if (re.test(t)) return t; await sleep(400); }
  return await rowText(page, name);
}
function verify(file, size) {
  if (!fs.existsSync(file)) return 'missing';
  const b = fs.readFileSync(file);
  if (b.length !== size) return `size ${b.length}`;
  for (const i of [0, 1, 250, 251, 65535, 65536, 1000003, size >> 1, size - 1]) if (b[i] !== expectedByte(i)) return `byte ${i}`;
  return 'ok';
}
async function addViaDialog(page, url, dir) {
  await page.getByRole('button', { name: 'Add URL' }).click();
  await page.getByLabel('URL Address').fill(url);
  await page.getByLabel('Save To').fill(dir);
  await page.getByRole('button', { name: 'Download Now' }).click();
}

const DL = path.join(ROOT, 'dl'); fs.mkdirSync(DL, { recursive: true });
const srv = spawn('python', [HERE + 'rangeserve.py', '8765'], { stdio: 'ignore' });
await sleep(1200);
let app;
try {
  // T1: data written by the C# edition loads
  const real = path.join(ROOT, 'data-real');
  fs.mkdirSync(real);
  for (const f of ['downloads.json', 'settings.json']) fs.copyFileSync(path.join(process.env.APPDATA, 'WDM', f), path.join(real, f));
  app = await startApp(real);
  const n = await app.page.locator('.download-row').count();
  check('T1 C#-era downloads.json loads (2 rows)', n === 2, `rows=${n}`);
  check('T1 statuses are Completed', /Completed/.test(await rowText(app.page, 'stream.mkv')));
  await app.page.screenshot({ path: path.join(ROOT, 't1.png') });
  // T2 second instance hands off and exits
  const t0 = Date.now();
  const second = spawn(EXE, [], { env: { ...process.env, WDM_DATA_DIR: real }, stdio: 'ignore' });
  const code = await new Promise((r) => { second.on('exit', r); setTimeout(() => r('timeout'), 10000); });
  check('T2 second launch exits (single instance)', code === 0, `code=${code} ${Date.now() - t0}ms`);
  const count = execSync('tasklist /FI "IMAGENAME eq wdm-desktop.exe" /NH').toString().split('\n').filter((l) => /wdm-desktop/.test(l)).length;
  check('T2 only one wdm-desktop process', count === 1, `count=${count}`);
  app.browser.close().catch(() => {}); killApp(); await sleep(1500);

  // fresh profile
  const data = path.join(ROOT, 'data');
  app = await startApp(data);
  let page = app.page;

  // T3 add dialog -> download now starts and completes (the 2.0.x-native bug: it never started)
  await addViaDialog(page, `${SRV}/f/small.bin?size=20971520`, DL);
  let t = await waitRow(page, 'small.bin', /Completed/);
  check('T3 Add dialog download completes', /Completed/.test(t), t.slice(0, 110));
  check('T3 content correct', verify(path.join(DL, 'small.bin'), 20971520) === 'ok', verify(path.join(DL, 'small.bin'), 20971520));
  check('T3 no .wdm-part/.wdm-state left', !fs.existsSync(path.join(DL, 'small.bin.wdm-part')) && !fs.existsSync(path.join(DL, 'small.bin.wdm-state')));

  // T4 pause / resume with live status + segments
  await addViaDialog(page, `${SRV}/f/slow.bin?size=62914560&kbps=400`, DL);
  t = await waitRow(page, 'slow.bin', /[KM]B\/s/, 20000);
  check('T4 shows Downloading with speed', /Downloading/.test(t) && /[KM]B\/s/.test(t), t.slice(0, 140));
  await page.locator('.download-row', { hasText: 'slow.bin' }).first().click();
  const segTxt = (await page.locator('body').innerText()).replace(/\s+/g, ' ');
  check('T4 stream visualizer lists segments with state', /Downloading/.test(segTxt) && /Segment|#\d|Stream/i.test(segTxt), '');
  await page.screenshot({ path: path.join(ROOT, 't4-active.png') });
  await page.getByRole('button', { name: /^Pause/ }).first().click();
  t = await waitRow(page, 'slow.bin', /Paused/, 8000);
  check('T4 Pause -> Paused', /Paused/.test(t), t.slice(0, 100));
  await page.getByRole('button', { name: /^Resume/ }).first().click();
  t = await waitRow(page, 'slow.bin', /Downloading|Connecting/, 10000);
  check('T4 Resume -> running again', /Downloading|Connecting/.test(t), t.slice(0, 100));
  t = await waitRow(page, 'slow.bin', /Completed/, 90000);
  check('T4 resumed download completes', /Completed/.test(t), t.slice(0, 100));
  check('T4 resumed content correct', verify(path.join(DL, 'slow.bin'), 62914560) === 'ok', verify(path.join(DL, 'slow.bin'), 62914560));

  // T5 error shows a message
  await addViaDialog(page, `${SRV}/gone`, DL);
  t = await waitRow(page, 'gone', /Error/, 30000);
  check('T5 failed download shows Error with a reason', /Error/.test(t) && t.length > 40, t.slice(0, 200));

  // T6 pipe: traversal-safe name, duplicate reuse, batch keeps every address
  fs.rmSync(path.join(ROOT, 'evil.bin'), { force: true });
  const sub = path.join(DL, 'sub'); fs.mkdirSync(sub);
  await sendPipe({ Urls: [`${SRV}/f/evil.bin?size=1000`], FileName: '..\\..\\escaped.bin', DestinationDirectory: sub, Silent: true });
  await sleep(3000);
  check('T6 traversal name stays inside the folder', !fs.existsSync(path.join(DL, 'escaped.bin')) && !fs.existsSync(path.join(ROOT, 'escaped.bin')) && fs.readdirSync(sub).some((f) => f.includes('escaped.bin')), fs.readdirSync(sub).join(','));
  const before = await page.locator('.download-row').count();
  await sendPipe({ Urls: [`${SRV}/f/small2.bin?size=2000&x=1`], Silent: true, QueueOnly: true, DestinationDirectory: DL });
  await sendPipe({ Urls: [`${SRV}/f/small2.bin?size=2000&x=1`], Silent: true, QueueOnly: true, DestinationDirectory: DL });
  await sleep(1500);
  const after = await page.locator('.download-row').count();
  check('T6 duplicate queued URL adds one row', after === before + 1, `${before}->${after}`);
  await sendPipe({ Urls: [`${SRV}/f/b1.bin?size=3000`, `${SRV}/f/b2.bin?size=3000`, `${SRV}/f/b3.bin?size=3000`], DestinationDirectory: DL });
  await sleep(2000);
  const after2 = await page.locator('.download-row').count();
  check('T6 browser batch of 3 keeps all 3', after2 === after + 3, `${after}->${after2}`);

  // T7 browser single (dialog on) opens Add dialog
  await sendPipe({ Urls: [`${SRV}/f/dialog.bin?size=5000`] });
  await sleep(1500);
  const dlg = await page.locator('[role=dialog]').count();
  const val = dlg ? await page.getByLabel('URL Address').inputValue() : '';
  check('T7 single browser request opens the Add dialog with the URL', dlg === 1 && val.includes('dialog.bin'), val);
  await page.getByRole('button', { name: 'Cancel' }).click();

  // T8 interrupted run comes back paused
  await addViaDialog(page, `${SRV}/f/kill.bin?size=104857600&kbps=200`, DL);
  await waitRow(page, 'kill.bin', /Downloading/, 20000);
  app.browser.close().catch(() => {}); killApp(); await sleep(2000);
  app = await startApp(data); page = app.page;
  t = await rowText(page, 'kill.bin');
  check('T8 killed download reloads as Paused (not stuck Connecting)', /Paused/.test(t), t.slice(0, 100));
  await page.locator('.download-row', { hasText: 'kill.bin' }).first().click();
  await page.getByRole('button', { name: /^Resume/ }).first().click();
  t = await waitRow(page, 'kill.bin', /[KM]B\/s/, 15000);
  check('T8 resumes after a crash', /[KM]B\/s/.test(t), t.slice(0, 100));
  await page.screenshot({ path: path.join(ROOT, 't8.png') });

  // T9 open_file refuses unlisted paths
  const refused = await page.evaluate(async () => { try { await window.__TAURI_INTERNALS__.invoke('open_file', { path: 'C:\\Windows\\System32\\calc.exe' }); return 'opened'; } catch (e) { return String(e); } });
  check('T9 open_file refuses an unlisted path', /Only downloaded files/.test(refused), refused);

  // T10 accessibility: every control has a name
  const unnamed = await page.evaluate(() => [...document.querySelectorAll('button,input,select')].filter((e) => {
    const n = (e.getAttribute('aria-label') || e.getAttribute('title') || e.innerText || '').trim();
    const lab = e.labels && e.labels.length;
    return !n && !lab;
  }).length);
  check('T10 main window: all buttons/inputs have names', unnamed === 0, `unnamed=${unnamed}`);
  const consoleErrors = [];
  page.on('pageerror', (e) => consoleErrors.push(String(e)));
  await page.getByTitle('Settings & Options').click();
  await sleep(500);
  const unnamed2 = await page.evaluate(() => [...document.querySelectorAll('[role=dialog] button,[role=dialog] input,[role=dialog] select')].filter((e) => {
    const n = (e.getAttribute('aria-label') || e.getAttribute('title') || e.innerText || '').trim();
    return !n && !(e.labels && e.labels.length);
  }).length);
  check('T10 settings dialog: all controls named', unnamed2 === 0, `unnamed=${unnamed2}`);
  check('T10 no page errors (CSP etc.)', consoleErrors.length === 0, consoleErrors.join(' | ').slice(0, 200));
} catch (e) {
  console.log('FAIL  exception', e && e.stack || e);
  results.push(false);
} finally {
  try { app && app.browser.close(); } catch {}
  killApp(); srv.kill();
  console.log(`\n${results.filter(Boolean).length}/${results.length} passed. artifacts: ${ROOT}`);
}
