// Loads the real extension in installed Chrome / Edge and pings the real native host through the browser.
// Registers the host in HKCU for the test and always unregisters afterwards. Usage: node browsers.mjs [wdm-nativehost.exe]
import { fileURLToPath } from 'node:url';
import { execFileSync } from 'node:child_process';
import { createRequire } from 'node:module';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
const REPO = fileURLToPath(new URL('../../', import.meta.url));
const HOST = path.resolve(process.argv[2] || REPO + 'target/release/wdm-nativehost.exe');
const { chromium } = createRequire(REPO + 'package.json')('playwright');
let pass = 0, total = 0;
const check = (n, ok, x = '') => { total++; if (ok) pass++; console.log(`${ok ? 'PASS' : 'FAIL'}  ${n}${x ? '  -> ' + x : ''}`); };

const BROWSERS = [
  { name: 'Chrome', key: 'Google/Chrome', exe: ['C:/Program Files/Google/Chrome/Application/chrome.exe'] },
  { name: 'Edge', key: 'Microsoft/Edge', exe: ['C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'] },
  { name: 'Brave', key: 'BraveSoftware/Brave-Browser', exe: ['C:/Program Files/BraveSoftware/Brave-Browser/Application/brave.exe', (process.env.LOCALAPPDATA || '').replaceAll(String.fromCharCode(92), '/') +'/BraveSoftware/Brave-Browser/Application/brave.exe'] },
];

execFileSync(HOST, ['--register'], { stdio: 'ignore' });
try {
  for (const b of BROWSERS) {
    const exe = b.exe.find((p) => fs.existsSync(p));
    if (!exe) { console.log(`SKIP  ${b.name}: not installed on this PC`); continue; }
    // Registration check (what the browser reads): HKCU key -> manifest file with this host and the sideloaded origin
    const reg = execFileSync('reg', ['query', ['HKCU', 'Software', ...b.key.split('/'), 'NativeMessagingHosts', 'com.wdm.download'].join(String.fromCharCode(92)), '/ve']).toString();
    const mf = reg.match(/REG_SZ\s+(.+)/)?.[1]?.trim();
    const m = mf && fs.existsSync(mf) ? JSON.parse(fs.readFileSync(mf, 'utf8')) : null;
    check(`${b.name}: registry points to a valid manifest`, !!m && path.resolve(m.path).toLowerCase() === HOST.toLowerCase()
      && m.allowed_origins.includes('chrome-extension://kdjgcdbahccolkkagbckkchfpcchcfne/'), mf);
    const profile = fs.mkdtempSync(path.join(os.tmpdir(), `wdm-${b.name}-`));
    const ext = path.resolve(REPO, 'extension');
    // Branded Chrome 137+ ignores --load-extension; Extensions.loadUnpacked over CDP still works with this flag.
    // Playwright adds --disable-extensions by default, which must be dropped or no extension runs.
    const ctx = await chromium.launchPersistentContext(profile, {
      executablePath: exe, headless: false, ignoreDefaultArgs: ['--disable-extensions'],
      args: ['--enable-unsafe-extension-debugging', '--no-first-run'],
    });
    try {
      const cdp = await ctx.browser().newBrowserCDPSession();
      await cdp.send('Extensions.loadUnpacked', { path: ext });
      let sw = ctx.serviceWorkers()[0];
      if (!sw) sw = await ctx.waitForEvent('serviceworker', { timeout: 8000 }).catch(() => null);
      check(`${b.name}: extension service worker starts`, !!sw, sw?.url());
      if (!sw) continue;
      const r = await sw.evaluate(() => new Promise((res) =>
        chrome.runtime.sendNativeMessage('com.wdm.download', { action: 'ping' }, (x) => res({ x, err: chrome.runtime.lastError?.message }))));
      check(`${b.name}: native host answers ping via the browser`, r.x?.status === 'ok', JSON.stringify(r));
    } finally { await ctx.close(); }
  }
} finally { execFileSync(HOST, ['--unregister'], { stdio: 'ignore' }); }
console.log(`${pass}/${total} passed`);
process.exit(pass === total ? 0 : 1);
