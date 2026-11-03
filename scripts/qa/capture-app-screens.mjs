// Real screenshots of WDM 2.5.0: drives the release exe over CDP against a local range server, 1280x800, dark and light.
import { fileURLToPath } from 'node:url';
const HERE = fileURLToPath(new URL('.', import.meta.url)).replace(/\\/g, '/');
const REPO = HERE + '../../';
import { spawn, execSync } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { createRequire } from 'node:module';
const require = createRequire(REPO + 'package.json');
const { chromium } = require('playwright');

const EXE = process.env.WDM_EXE || REPO + 'target/release/wdm-desktop.exe';
const OUT = REPO + 'docs/media/screenshots';
const SRV = 'http://127.0.0.1:8765';
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const root = fs.mkdtempSync(path.join(os.tmpdir(), 'wdm-shots-'));
const data = path.join(root, 'data'); fs.mkdirSync(data);
const dl = path.join(root, 'Downloads'); fs.mkdirSync(dl);
const demoDir = 'C:\\Users\\Public\\Downloads';

const now = Date.now();
const iso = (minAgo) => new Date(now - minAgo * 60000).toISOString().replace('Z', '+00:00');
const done = (name, bytes, minAgo) => ({ Id: name.replace(/\W/g, '').slice(0, 12).padEnd(32, '0'), Url: `https://example.com/files/${name}`, FileName: name, TargetFilePath: `${demoDir}\\${name}`, Status: 'Completed', TotalBytes: bytes, DownloadedBytes: bytes, AddedAt: iso(minAgo + 2), CompletedAt: iso(minAgo), Options: null });
const rec = (name, bytes, got, status, minAgo, id) => ({ Id: id.padEnd(32, '0'), Url: `https://example.com/files/${name}`, FileName: name, TargetFilePath: `${demoDir}\\${name}`, Status: status, TotalBytes: bytes, DownloadedBytes: got, AddedAt: iso(minAgo), CompletedAt: null, Options: null });
fs.writeFileSync(path.join(data, 'downloads.json'), JSON.stringify([
  rec('project-archive-2026.7z', 1288490188, 502000000, 'Paused', 55, 'seedpaused'),
  rec('installer-x64.msi', 96468992, 0, 'Queued', 20, 'seedqueued'),
  rec('lecture-recordings.zip', 734003200, 734003200, 'Completed', 130, 'seeddone4'),
  done('Annual-Report-2025.pdf', 4823449, 180),
  done('podcast-episode-12.mp3', 58720256, 260),
  done('dataset-sample.zip', 214958080, 420),
  done('cover-photo.jpg', 3984588, 600),
], null, 2));
fs.writeFileSync(path.join(data, 'settings.json'), JSON.stringify({ DefaultDownloadDirectory: dl, DefaultConnections: 8, MaxConcurrentDownloads: 3, ShowStartDownloadDialog: true }, null, 2));

const srv = spawn('python', [HERE + 'rangeserve.py', '8765'], { stdio: 'ignore' });
await sleep(1200);
const app = spawn(EXE, [], { env: { ...process.env, WDM_DATA_DIR: data, WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS: '--remote-debugging-port=9222' }, stdio: 'ignore' });
let browser;
for (let i = 0; i < 60 && !browser; i++) { try { browser = await chromium.connectOverCDP('http://127.0.0.1:9222'); } catch { await sleep(500); } }
let page;
for (let i = 0; i < 40 && !page; i++) { page = browser.contexts().flatMap((c) => c.pages()).find((p) => !p.url().startsWith('devtools') && p.url() !== 'about:blank'); await sleep(250); }
await page.setViewportSize({ width: 1280, height: 800 });
await page.waitForSelector('text=Add URL');

const themed = async (name, fn) => {
  for (const mode of ['dark', 'light']) {
    await page.evaluate((m) => document.documentElement.setAttribute('data-theme', m), mode);
    await sleep(250);
    await page.screenshot({ path: path.join(OUT, `${name}-${mode}.png`) });
  }
  if (fn) await fn();
};
const add = async (url, dir) => {
  await page.getByRole('button', { name: 'Add URL' }).click();
  await page.getByLabel('URL Address').fill(url);
  await page.getByLabel('Save To').fill(dir);
  await page.getByRole('button', { name: 'Download Now' }).click();
};

try {
  // two live downloads at different speeds
  await add(`${SRV}/f/ubuntu-24.04-desktop-amd64.iso?size=6227020800&kbps=1500`, dl);
  await sleep(500);
  await add(`${SRV}/f/blender-4.2-windows-x64.zip?size=1503238553&kbps=420`, dl);
  await sleep(9000);
  await page.locator('.download-row', { hasText: 'ubuntu-24.04' }).first().click();
  await sleep(1500);
  await themed('main');
  for (const m of ['dark', 'light']) fs.copyFileSync(path.join(OUT, `main-${m}.png`), REPO + `docs/media/screenshot-${m}.png`);

  // add dialog
  await page.getByRole('button', { name: 'Add URL' }).click();
  await page.getByLabel('URL Address').fill('https://example.com/releases/design-assets-2026.zip');
  await page.getByLabel('Save To').fill(demoDir);
  await themed('add');
  await page.getByRole('button', { name: 'Cancel' }).click();

  // site grabber
  await page.getByTitle(/Site grabber/).click();
  await page.getByLabel('Web page address to crawl').fill('http://localhost:8765/page');
  await page.getByRole('button', { name: /Crawl Page/ }).click();
  await sleep(2500);
  await page.getByLabel('Save to folder').fill(demoDir);
  await themed('grabber');
  await page.getByRole('button', { name: 'Close' }).click();

  // scheduler
  await page.getByTitle('Scheduler').click();
  await sleep(300);
  await themed('scheduler');
  await page.getByRole('button', { name: 'Close' }).click();

  // options: three tabs
  await page.getByTitle('Settings & Options').click();
  await sleep(300);
  await page.getByLabel('Default folder').fill(demoDir).catch(() => {});
  
  for (const [label, name] of [['General', 'options'], ['Bandwidth', 'options-network'], ['Engine', 'options-connection']]) {
    const t = page.locator('[role=dialog] button', { hasText: new RegExp(label, 'i') }).first();
    if (await t.count()) { await t.click(); await sleep(300); await themed(name); }
  }
  console.log('done');
} finally {
  await browser.close().catch(() => {});
  try { execSync('taskkill /F /IM wdm-desktop.exe', { stdio: 'ignore' }); } catch {}
  srv.kill();
}
