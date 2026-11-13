import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { createRequire } from 'node:module';

const require = createRequire(import.meta.url);
const { chromium } = require('playwright');

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const distDir = path.join(root, 'src-ui', 'dist');
const mediaDir = path.join(root, 'docs', 'media');
const screenshotsDir = path.join(mediaDir, 'screenshots');

if (!fs.existsSync(screenshotsDir)) {
  fs.mkdirSync(screenshotsDir, { recursive: true });
}

// 1. Simple static server for dist
const server = http.createServer((req, res) => {
  let filePath = path.join(distDir, req.url === '/' ? 'index.html' : req.url.split('?')[0]);
  if (!fs.existsSync(filePath)) {
    filePath = path.join(distDir, 'index.html');
  }

  const ext = path.extname(filePath);
  const contentTypes = {
    '.html': 'text/html',
    '.js': 'text/javascript',
    '.css': 'text/css',
    '.png': 'image/png',
    '.svg': 'image/svg+xml',
    '.json': 'application/json',
  };

  const contentType = contentTypes[ext] || 'application/octet-stream';
  fs.readFile(filePath, (err, content) => {
    if (err) {
      res.writeHead(500);
      res.end('Error loading file');
    } else {
      res.writeHead(200, { 'Content-Type': contentType });
      res.end(content);
    }
  });
});

await new Promise((resolve) => server.listen(0, '127.0.0.1', resolve));
const port = server.address().port;
const appUrl = `http://127.0.0.1:${port}`;
console.log(`[WDM UI] Local static server listening on ${appUrl}`);

// 2. Mock state for downloads (PascalCase to match Rust & TypeScript interfaces)
const mockDownloads = [
  {
    Id: 'dl-1',
    Url: 'https://releases.ubuntu.com/24.04/ubuntu-24.04-desktop-amd64.iso',
    FileName: 'ubuntu-24.04-desktop-amd64.iso',
    TargetFilePath: 'C:\\Users\\anamm\\Downloads\\ubuntu-24.04-desktop-amd64.iso',
    Status: 'Downloading',
    TotalBytes: 4950000000,
    DownloadedBytes: 3564000000,
    AddedAt: new Date(Date.now() - 3600000).toISOString(),
    CompletedAt: null,
    Options: { Connections: 16 }
  },
  {
    Id: 'dl-2',
    Url: 'https://update.code.visualstudio.com/latest/win32-x64-user/stable',
    FileName: 'VSCodeUserSetup-x64-1.89.1.exe',
    TargetFilePath: 'C:\\Users\\anamm\\Downloads\\VSCodeUserSetup-x64-1.89.1.exe',
    Status: 'Completed',
    TotalBytes: 100030000,
    DownloadedBytes: 100030000,
    AddedAt: new Date(Date.now() - 7200000).toISOString(),
    CompletedAt: new Date(Date.now() - 7100000).toISOString(),
    Options: { Connections: 8 }
  },
  {
    Id: 'dl-3',
    Url: 'https://www.python.org/ftp/python/3.12.3/python-3.12.3-amd64.exe',
    FileName: 'python-3.12.3-amd64.exe',
    TargetFilePath: 'C:\\Users\\anamm\\Downloads\\python-3.12.3-amd64.exe',
    Status: 'Completed',
    TotalBytes: 26320000,
    DownloadedBytes: 26320000,
    AddedAt: new Date(Date.now() - 14400000).toISOString(),
    CompletedAt: new Date(Date.now() - 14350000).toISOString(),
    Options: { Connections: 8 }
  },
  {
    Id: 'dl-4',
    Url: 'https://download.blender.org/release/Blender4.1/blender-4.1.0-windows-x64.msi',
    FileName: 'blender-4.1.0-windows-x64.msi',
    TargetFilePath: 'C:\\Users\\anamm\\Downloads\\blender-4.1.0-windows-x64.msi',
    Status: 'Paused',
    TotalBytes: 335544320,
    DownloadedBytes: 150994944,
    AddedAt: new Date(Date.now() - 28800000).toISOString(),
    CompletedAt: null,
    Options: { Connections: 16 }
  },
  {
    Id: 'dl-5',
    Url: 'https://nodejs.org/dist/v22.0.0/node-v22.0.0-x64.msi',
    FileName: 'node-v22.0.0-x64.msi',
    TargetFilePath: 'C:\\Users\\anamm\\Downloads\\node-v22.0.0-x64.msi',
    Status: 'Completed',
    TotalBytes: 32700000,
    DownloadedBytes: 32700000,
    AddedAt: new Date(Date.now() - 86400000).toISOString(),
    CompletedAt: new Date(Date.now() - 86350000).toISOString(),
    Options: { Connections: 8 }
  },
  {
    Id: 'dl-6',
    Url: 'https://github.com/git-for-windows/git/releases/download/v2.45.0.windows.1/Git-2.45.0-64-bit.exe',
    FileName: 'Git-2.45.0-64-bit.exe',
    TargetFilePath: 'C:\\Users\\anamm\\Downloads\\Git-2.45.0-64-bit.exe',
    Status: 'Completed',
    TotalBytes: 61800000,
    DownloadedBytes: 61800000,
    AddedAt: new Date(Date.now() - 172800000).toISOString(),
    CompletedAt: new Date(Date.now() - 172700000).toISOString(),
    Options: { Connections: 8 }
  }
];

// Mock segments for visualizer (16 active segments across 4.95 GB)
const mockSegments = [];
const segCount = 16;
const total = 4950000000;
const segSize = Math.floor(total / segCount);
for (let i = 0; i < segCount; i++) {
  const start = i * segSize;
  const end = (i === segCount - 1) ? total - 1 : start + segSize - 1;
  const pct = 0.5 + 0.45 * Math.sin((i + 1) * 0.7);
  const current = start + Math.floor((end - start) * pct);
  mockSegments.push({
    SegmentId: i + 1,
    StartOffset: start,
    CurrentOffset: current,
    EndOffset: end,
    State: 'Downloading',
    SpeedBytesPerSecond: 3000000,
    DownloadedBytes: current - start,
    TotalBytes: end - start + 1
  });
}

const mockProgress = {
  TotalBytes: total,
  DownloadedBytes: 3564000000,
  SpeedBytesPerSecond: 48500000, // ~48.5 MB/s
  EstimatedSecondsRemaining: 28,
  Segments: mockSegments,
  Status: 'Downloading'
};

function getMockSettings(isDark) {
  return {
    DefaultDownloadDirectory: 'C:\\Users\\anamm\\Downloads',
    DefaultConnections: 16,
    MaxConcurrentDownloads: 5,
    DefaultSpeedLimitKb: 5000,
    IsSpeedLimiterEnabled: false,
    AutoOpenFileOnComplete: false,
    ConfirmBeforeDelete: true,
    EnableDynamicMiddleSplit: true,
    ShowStartDownloadDialog: true,
    NetworkTimeoutSeconds: 30,
    UserAgent: 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) WDM/2.5.5',
    SchedulerEnabled: true,
    SchedulerStartTime: '02:00',
    SchedulerStopTime: '06:00',
    SchedulerDays: 127,
    SchedulerShutdownOnComplete: false,
    IsDarkMode: isDark
  };
}

async function launchBrowser() {
  const attempts = [{ channel: 'msedge' }, { channel: 'chrome' }, {}];
  for (const opts of attempts) {
    try {
      return await chromium.launch({
        ...opts,
        headless: true,
        args: ['--force-device-scale-factor=1', '--font-render-hinting=medium']
      });
    } catch {
      // try next
    }
  }
  throw new Error('Could not find Edge or Chrome on this machine');
}

async function captureScreens() {
  const browser = await launchBrowser();

  for (const isDark of [true, false]) {
    const themeName = isDark ? 'dark' : 'light';
    console.log(`[WDM UI] Capturing ${themeName} theme...`);

    const context = await browser.newContext({
      viewport: { width: 1280, height: 800 },
      deviceScaleFactor: 1
    });

    const page = await context.newPage();

    // Inject Tauri mock
    await page.addInitScript(({ settings, downloads, progress }) => {
      window.__mockSettings = settings;
      window.__mockDownloads = downloads;
      window.__mockProgress = progress;

      const eventHandlers = {};

      window.__TAURI_INTERNALS__ = {
        invoke: async (cmd, args) => {
          if (cmd === 'get_settings') return window.__mockSettings;
          if (cmd === 'get_downloads') return window.__mockDownloads;
          if (cmd === 'plugin:event|listen') {
            const eventName = args.event;
            const handlerId = args.handler;
            eventHandlers[eventName] = handlerId;
            return () => {};
          }
          return null;
        },
        transformCallback: (callback) => {
          window.__eventCallback = callback;
          // Trigger progress event on startup
          setTimeout(() => {
            callback({
              event: 'download-progress',
              payload: {
                id: 'dl-1',
                progress: window.__mockProgress
              }
            });
          }, 100);
          return 1;
        }
      };
    }, { settings: getMockSettings(isDark), downloads: mockDownloads, progress: mockProgress });

    await page.goto(appUrl);
    await page.waitForSelector('.download-row', { timeout: 10000 });

    // Select active download dl-1
    await page.click('.download-row');

    await page.waitForTimeout(500);

    // 1. Capture Main Window (1280x800)
    const mainShotPath = path.join(mediaDir, `screenshot-${themeName}.png`);
    await page.screenshot({ path: mainShotPath });
    console.log(`[WDM UI] Saved: ${mainShotPath}`);

    // Copy to screenshots directory
    const shotMainPath = path.join(screenshotsDir, `main-${themeName}.png`);
    fs.copyFileSync(mainShotPath, shotMainPath);

    // 2. Open & Capture Add Download Dialog
    const addBtn = await page.$('button:has-text("Add URL")');
    if (addBtn) {
      await addBtn.click();
      await page.waitForSelector('.modal-card');
      await page.waitForTimeout(300);
      const addPath = path.join(screenshotsDir, `add-${themeName}.png`);
      await page.screenshot({ path: addPath });
      console.log(`[WDM UI] Saved: ${addPath}`);
      await page.click('.modal-card button:has-text("Cancel")');
      await page.waitForTimeout(200);
    }

    // 3. Open & Capture Batch Grabber Dialog
    const grabBtn = await page.$('button[title*="Site grabber"]');
    if (grabBtn) {
      await grabBtn.click();
      await page.waitForSelector('.modal-card');
      await page.waitForTimeout(300);
      const grabPath = path.join(screenshotsDir, `grabber-${themeName}.png`);
      await page.screenshot({ path: grabPath });
      console.log(`[WDM UI] Saved: ${grabPath}`);
      await page.click('.modal-card button:has-text("Cancel")');
      await page.waitForTimeout(200);
    }

    // 4. Open & Capture Scheduler Dialog
    const schedBtn = await page.$('button[title*="Scheduler"]');
    if (schedBtn) {
      await schedBtn.click();
      await page.waitForSelector('.modal-card');
      await page.waitForTimeout(300);
      const schedPath = path.join(screenshotsDir, `scheduler-${themeName}.png`);
      await page.screenshot({ path: schedPath });
      console.log(`[WDM UI] Saved: ${schedPath}`);
      await page.click('.modal-card button:has-text("Cancel")');
      await page.waitForTimeout(200);
    }

    // 5. Open & Capture Options/Settings Dialog
    const optBtn = await page.$('button[title*="Settings"]');
    if (optBtn) {
      await optBtn.click();
      await page.waitForSelector('.modal-card');
      await page.waitForTimeout(300);
      const optPath = path.join(screenshotsDir, `options-${themeName}.png`);
      await page.screenshot({ path: optPath });
      console.log(`[WDM UI] Saved: ${optPath}`);
      await page.click('.modal-card button:has-text("Cancel")');
      await page.waitForTimeout(200);
    }

    await context.close();
  }

  await browser.close();
  server.close();
  console.log('[WDM UI] All screenshots captured successfully!');
}

captureScreens().catch((err) => {
  console.error('[WDM UI] Screenshot capture failed:', err);
  server.close();
  process.exit(1);
});
