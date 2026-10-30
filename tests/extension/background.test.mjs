// Runs extension/background.js against a mocked `chrome` API. Run: node --test tests/extension
import { test } from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs";
import vm from "node:vm";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../../extension");
const source = fs.readFileSync(path.join(root, "background.js"), "utf8");

function load({ hostReply = { status: "ok" }, allSites = true, takeover = true, journal = {}, rules } = {}) {
  const calls = [];
  const listeners = {};
  const store = { wdmHandoffs: journal };
  const ev = (name) => ({ addListener: (fn) => { listeners[name] = fn; } });
  const chrome = {
    runtime: {
      onInstalled: ev("installed"), onStartup: ev("startup"), onMessage: ev("message"),
      getURL: (p) => p, lastError: undefined,
      sendNativeMessage: (host, msg, cb) => { calls.push(["native", msg]); cb(hostReply); },
    },
    contextMenus: { create() {}, onClicked: ev("menu") },
    storage: {
      local: { get: async () => ({ takeover, rules }) },
      session: {
        get: async (k) => ({ [k]: structuredClone(store[k]) }),
        set: async (o) => Object.assign(store, structuredClone(o)),
        remove: async (k) => { delete store[k]; },
      },
    },
    permissions: { contains: async () => allSites, onAdded: ev("padd"), onRemoved: ev("prem") },
    cookies: { getAll: async () => [{ name: "s", value: "1" }] },
    downloads: {
      onCreated: ev("created"),
      pause: async (id) => calls.push(["pause", id]),
      resume: async (id) => calls.push(["resume", id]),
      cancel: async (id) => calls.push(["cancel", id]),
      erase: async ({ id }) => calls.push(["erase", id]),
    },
    scripting: { getRegisteredContentScripts: async () => [], registerContentScripts: async () => {}, unregisterContentScripts: async () => {}, executeScript: async () => [] },
    tabs: { create() {}, sendMessage: async () => {} },
    webRequest: { onHeadersReceived: ev("headers") },
  };
  const ctx = vm.createContext({
    chrome, console, navigator: { userAgent: "UA" }, URL, Set, Promise,
    importScripts: (file) => vm.runInContext(fs.readFileSync(path.join(root, file), "utf8"), ctx), // as in a real service worker
  });
  vm.runInContext(source, ctx);
  return { calls, listeners, store };
}

const item = (over = {}) => ({ id: 7, url: "https://example.com/a.zip", filename: "a.zip", referrer: "https://example.com/", ...over });
const names = (calls) => calls.filter((c) => c[0] !== "native").map((c) => c[0]);

test("accepted handoff cancels and erases the browser download", async () => {
  const { calls, listeners, store } = load();
  await new Promise((r) => setTimeout(r, 5));
  await listeners.created(item());
  assert.deepEqual(names(calls), ["pause", "cancel", "erase"]);
  assert.deepEqual(store.wdmHandoffs, {});
});

test("rejected handoff leaves the browser download intact (resumed)", async () => {
  const { calls, listeners } = load({ hostReply: { status: "error" } });
  await new Promise((r) => setTimeout(r, 5));
  await listeners.created(item());
  assert.deepEqual(names(calls), ["pause", "resume"]);
});

test("unreachable native host leaves the browser download intact", async () => {
  const { calls, listeners } = load({ hostReply: null });
  await new Promise((r) => setTimeout(r, 5));
  await listeners.created(item());
  assert.deepEqual(names(calls), ["pause", "resume"]);
});

test("no takeover without all-sites access, when disabled, or in incognito", async () => {
  for (const [opts, it] of [[{ allSites: false }, item()], [{ takeover: false }, item()], [{}, item({ incognito: true })]]) {
    const { calls, listeners } = load(opts);
    await new Promise((r) => setTimeout(r, 5));
    await listeners.created(it);
    assert.deepEqual(calls, []);
  }
});

test("non-http and small unknown-type downloads are ignored", async () => {
  const { calls, listeners } = load();
  await new Promise((r) => setTimeout(r, 5));
  await listeners.created(item({ url: "blob:https://example.com/x", filename: "x.zip" }));
  await listeners.created(item({ url: "https://example.com/page", filename: "page", mime: "text/html", totalBytes: 5e6 }));
  await listeners.created(item({ url: "https://example.com/blob", filename: "blob", mime: "application/octet-stream", totalBytes: 10 }));
  assert.deepEqual(calls, []);
});

test("worker restart resumes downloads orphaned before WDM answered", async () => {
  const { calls } = load({ journal: { 11: "pending" } });
  await new Promise((r) => setTimeout(r, 10));
  assert.deepEqual(calls, [["resume", 11]]);
});

test("worker restart finishes cancelling downloads WDM already accepted", async () => {
  const { calls } = load({ journal: { 12: "accepted" } });
  await new Promise((r) => setTimeout(r, 10));
  assert.deepEqual(names(calls), ["cancel", "erase"]);
});

test("manifest is MV3, keeps host access optional, and declares Firefox data collection", () => {
  const m = JSON.parse(fs.readFileSync(path.join(root, "manifest.json"), "utf8"));
  assert.equal(m.manifest_version, 3);
  assert.deepEqual(m.optional_host_permissions, ["<all_urls>"]);
  assert.equal(m.host_permissions, undefined);
  assert.deepEqual(m.browser_specific_settings.gecko.data_collection_permissions.required, ["none"]);
  assert.deepEqual(m.background.scripts, ["rules.js", "background.js"]); // Firefox loads rules.js first; Chrome uses importScripts
  for (const f of [...Object.values(m.icons), m.background.service_worker, m.action.default_popup]) {
    assert.ok(fs.existsSync(path.join(root, f)), `missing ${f}`);
  }
});

test("a site excluded in the rules is never taken over", async () => {
  const { calls, listeners } = load({ rules: { excludedHosts: ["example.com"] } });
  await new Promise((r) => setTimeout(r, 5));
  await listeners.created(item({ url: "https://cdn.example.com/a.zip" }));
  assert.deepEqual(calls, []);
});

test("user-added file types are taken over", async () => {
  const { calls, listeners } = load({ rules: { extraExtensions: ["epub"] } });
  await new Promise((r) => setTimeout(r, 5));
  await listeners.created(item({ url: "https://example.com/book.epub", filename: "book.epub" }));
  assert.deepEqual(names(calls), ["pause", "cancel", "erase"]);
});

test("every file the manifest and popup reference exists", () => {
  const m = JSON.parse(fs.readFileSync(path.join(root, "manifest.json"), "utf8"));
  for (const f of m.background.scripts) assert.ok(fs.existsSync(path.join(root, f)), `missing ${f}`);
  const html = fs.readFileSync(path.join(root, "popup.html"), "utf8");
  for (const [, ref] of html.matchAll(/(?:src|href)="([^"#]+)"/g)) {
    if (/^https?:/.test(ref)) continue;
    assert.ok(fs.existsSync(path.join(root, ref)), `popup.html references missing ${ref}`);
  }
});
