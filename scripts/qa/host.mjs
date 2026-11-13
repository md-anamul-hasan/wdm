// Speaks Chrome's native-messaging protocol to wdm-nativehost.exe: ping, cold-start hand-off, warm hand-off.
import { fileURLToPath } from 'node:url';
const HERE = fileURLToPath(new URL('.', import.meta.url)).replace(/\\/g, '/');
const REPO = HERE + '../../';
import { spawn, execSync } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';

// product version comes from Cargo.toml so a bump cannot silently break this check
const VERSION = fs.readFileSync(REPO + 'Cargo.toml', 'utf8').match(/^version\s*=\s*"([^"]+)"/m)[1];
const HOST = process.argv[2] || REPO + 'target/release/wdm-nativehost.exe';
const data = fs.mkdtempSync(path.join(os.tmpdir(), 'wdm-host-'));
const env = { ...process.env, WDM_DATA_DIR: data };
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
let ok = 0, total = 0;
const check = (n, c, x = '') => { total++; if (c) ok++; console.log(`${c ? 'PASS' : 'FAIL'}  ${n}${x ? '  -> ' + x : ''}`); };

function talk(msg) {
  return new Promise((resolve, reject) => {
    const p = spawn(HOST, ['chrome-extension://flnbghmpkajibfdfhbcfhkcjkhlokfgi/'], { env });
    let buf = Buffer.alloc(0);
    p.stdout.on('data', (d) => {
      buf = Buffer.concat([buf, d]);
      if (buf.length >= 4 && buf.length >= 4 + buf.readUInt32LE(0)) {
        const len = buf.readUInt32LE(0);
        const text = buf.subarray(4, 4 + len).toString('utf8');
        const extra = buf.length - 4 - len;
        p.stdin.end(); resolve({ reply: JSON.parse(text), extra });
      }
    });
    p.on('error', reject);
    setTimeout(() => { p.kill(); reject(new Error('timeout')); }, 25000);
    const body = Buffer.from(JSON.stringify(msg));
    const head = Buffer.alloc(4); head.writeUInt32LE(body.length, 0);
    p.stdin.write(Buffer.concat([head, body]));
  });
}

try { execSync('taskkill /F /IM wdm-desktop.exe', { stdio: 'ignore' }); } catch {}
let r = await talk({ action: 'ping' });
check('ping -> pong with version, stdout is protocol only', r.reply.message === 'pong' && r.reply.version === VERSION && r.extra === 0, JSON.stringify(r.reply));
r = await talk({ action: 'download', url: 'file:///c:/x.exe' });
check('non-http url refused', r.reply.status === 'error', r.reply.message);

// cold start: WDM is not running; host must start it and hand the request over
const t0 = Date.now();
r = await talk({ action: 'batch', urls: ['http://127.0.0.1:8765/f/h1.bin?size=1000', 'http://127.0.0.1:8765/f/h2.bin?size=1000'], referer: 'http://127.0.0.1:8765/', cookies: 'a=b' });
check('cold start: host launches WDM and delivers', r.reply.status === 'ok', `${JSON.stringify(r.reply)} ${Date.now() - t0}ms`);
await sleep(2500);
const list = JSON.parse(fs.readFileSync(path.join(data, 'downloads.json'), 'utf8').replace(/^\uFEFF/, ''));
check('cold start: both URLs listed as Queued', list.length === 2 && list.every((x) => x.Status === 'Queued'), list.map((x) => x.FileName + ':' + x.Status).join(','));
const raw = fs.readFileSync(path.join(data, 'downloads.json'), 'utf8');
check('cookies stored DPAPI-encrypted, not plaintext', !raw.includes('a=b') && raw.includes('dpapi1:'), raw.includes('dpapi1:') ? 'dpapi1: present' : 'no secret marker');

// warm: WDM running
r = await talk({ action: 'download', url: 'http://127.0.0.1:8765/f/h3.bin?size=1000' });
check('warm hand-off ok', r.reply.status === 'ok', JSON.stringify(r.reply));
await sleep(1500);

// --diagnose reports healthy pipe
const diag = execSync(`"${HOST}" --diagnose`, { env, encoding: 'utf8' });
check('--diagnose sees WDM running', /running|listening/i.test(diag), diag.split('\n').filter((l) => /WDM running|listening|running/i.test(l)).join(' | ').slice(0, 160));
try { execSync('taskkill /F /IM wdm-desktop.exe', { stdio: 'ignore' }); } catch {}
console.log(`\n${ok}/${total} passed`);
