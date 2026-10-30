// Renders the REAL extension popup (extension/popup.html) with a mocked `chrome` API and saves the screenshots
// that scripts/make-store-media.ps1 turns into Chrome Web Store images.
//
// You only need to run this when the popup (extension/popup.*) changes: the PNGs are committed.
// One-time setup (installs Playwright into node_modules without touching package.json; uses Edge/Chrome already on your PC):
//   npm install --no-save playwright
//   node scripts/capture-extension-screens.mjs
// Output: docs/media/screenshots/extension-popup-{on,access,rules}-{dark,light}.png (2x scale, 340 px wide popup).
// Also fails if the popup throws or logs an error, so it doubles as a smoke test of popup.js / rules.js.
import { createRequire } from "node:module";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const require = createRequire(import.meta.url);
let chromium;
try {
  ({ chromium } = require("playwright"));
} catch {
  console.error("Playwright is not installed. From the repository root run:\n\n  npm install --no-save playwright\n\nand then run this script again.");
  process.exit(2);
}

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const popupUrl = pathToFileURL(path.join(root, "extension", "popup.html")).href;
const outDir = path.join(root, "docs", "media", "screenshots");
const fs = await import("node:fs");
const version = JSON.parse(fs.readFileSync(path.join(root, "extension", "manifest.json"), "utf8")).version; // extension version ("Version x" in the footer)
const hostVersion = /<Version>([^<]+)<\/Version>/.exec(fs.readFileSync(path.join(root, "Directory.Build.props"), "utf8"))[1]; // app version ("Connected vx")

// What the popup should look like in each shot
const scenes = {
  on: { allSites: true, rules: undefined, openRules: false, host: "" },
  access: { allSites: false, rules: undefined, openRules: false, host: "" },
  rules: {
    allSites: true,
    openRules: true,
    host: "downloads.example.com",
    rules: { extraExtensions: ["epub", "dmg"], blockedExtensions: ["pdf"], excludedHosts: ["intranet.local", "example.org"], minUnknownMB: 1 },
  },
};

function mockChrome({ allSites, rules, host, version, hostVersion }) {
  const store = { takeover: true, ...(rules ? { rules } : {}) };
  window.chrome = {
    runtime: {
      lastError: undefined,
      getManifest: () => ({ version }),
      sendMessage: (msg, cb) => cb({ connected: true, version: hostVersion }),
    },
    permissions: { contains: async () => allSites, request: async () => true, remove: async () => true },
    storage: { local: { get: async (k) => (typeof k === "string" ? { [k]: store[k] } : store), set: async (o) => Object.assign(store, o), remove: async (k) => delete store[k] } },
    tabs: { query: async () => (host ? [{ url: `https://${host}/files` }] : []) },
  };
}

// Prefer Playwright's own Chromium; if it was never downloaded, fall back to the Edge or Chrome installed on the PC
async function launchBrowser() {
  const attempts = [{}, { channel: "msedge" }, { channel: "chrome" }];
  const errors = [];
  for (const options of attempts) {
    try { return await chromium.launch(options); } catch (e) { errors.push(`${options.channel ?? "playwright chromium"}: ${e.message.split("\n")[0]}`); }
  }
  console.error("No browser could be started:\n  " + errors.join("\n  ") + "\nInstall one with: npx playwright install chromium");
  process.exit(2);
}

const browser = await launchBrowser();
const problems = [];

for (const scheme of ["dark", "light"]) {
  for (const [name, scene] of Object.entries(scenes)) {
    const context = await browser.newContext({ viewport: { width: 340, height: 600 }, deviceScaleFactor: 2, colorScheme: scheme });
    const page = await context.newPage();
    page.on("pageerror", (e) => problems.push(`${name}/${scheme}: ${e.message}`));
    page.on("console", (m) => { if (m.type() === "error") problems.push(`${name}/${scheme}: console error: ${m.text()}`); });

    await page.addInitScript(mockChrome, { ...scene, version, hostVersion });
    await page.goto(popupUrl);
    await page.waitForFunction(() => document.getElementById("hostStatusText")?.textContent?.startsWith("Connected"));
    if (scene.openRules) {
      await page.evaluate(() => { document.getElementById("rules").open = true; });
      await page.waitForFunction(() => document.getElementById("extraExt").value !== "");
    }

    const file = path.join(outDir, `extension-popup-${name}-${scheme}.png`);
    if (scene.openRules) {
      // Chrome caps a popup at 600 px, so with the rules open the user scrolls down: capture that view (from the
      // "Take over downloads" switch to the bottom) instead of an 850 px tall strip that no real popup ever shows.
      const top = await page.evaluate(() => Math.max(0, document.querySelector(".switch").getBoundingClientRect().top + window.scrollY - 12));
      const height = await page.evaluate(() => document.documentElement.scrollHeight);
      await page.screenshot({ path: file, fullPage: true, clip: { x: 0, y: top, width: 340, height: height - top } });
    } else {
      await page.locator("body").screenshot({ path: file });
    }
    console.log("wrote", path.relative(root, file));
    await context.close();
  }
}

await browser.close();
if (problems.length) {
  console.error("\nPopup problems:\n" + problems.join("\n"));
  process.exit(1);
}
