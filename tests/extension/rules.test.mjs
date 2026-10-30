// Pure tests for extension/rules.js (takeover rules). Run: node --test tests/extension/rules.test.mjs
import { test } from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs";
import vm from "node:vm";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../../extension");
const ctx = vm.createContext({ URL });
vm.runInContext(fs.readFileSync(path.join(root, "rules.js"), "utf8"), ctx);
const R = ctx.WdmRules;

// Values created inside the vm context have another realm's prototypes; compare their JSON form
const eq = (actual, expected) => assert.deepStrictEqual(JSON.parse(JSON.stringify(actual)), expected);

const defaults = R.normalize(null);
const item = (over = {}) => ({ id: 1, url: "https://example.com/a.zip", filename: "a.zip", referrer: "", mime: "", totalBytes: 0, ...over });
const MB = 1024 * 1024;

test("defaults reproduce the original behaviour", () => {
  assert.equal(R.shouldTakeOver(item(), defaults), true);
  assert.equal(R.shouldTakeOver(item({ url: "https://e.com/x", filename: "x.PDF" }), defaults), true);
  assert.equal(R.shouldTakeOver(item({ url: "https://e.com/page", filename: "page", mime: "text/html", totalBytes: 5 * MB }), defaults), false);
  assert.equal(R.shouldTakeOver(item({ url: "https://e.com/blob", filename: "blob", mime: "application/octet-stream", totalBytes: 10 }), defaults), false);
  assert.equal(R.shouldTakeOver(item({ url: "https://e.com/blob", filename: "blob", mime: "application/octet-stream", totalBytes: 2 * MB }), defaults), true);
  assert.equal(R.shouldTakeOver(item({ incognito: true }), defaults), false);
  assert.equal(R.shouldTakeOver(item({ url: "blob:https://e.com/1", filename: "a.zip" }), defaults), false);
  assert.equal(R.shouldTakeOver(item({ url: "ftp://e.com/a.zip" }), defaults), false);
});

test("final URL wins over the original URL", () => {
  assert.equal(R.shouldTakeOver(item({ url: "https://e.com/redirect", finalUrl: "https://cdn.e.com/file.iso", filename: "file.iso" }), defaults), true);
});

test("extra and blocked file types", () => {
  const rules = R.normalize({ extraExtensions: [".EPUB", "*.dmg"], blockedExtensions: ["pdf"] });
  eq(rules.extraExtensions, ["epub", "dmg"]);
  assert.equal(R.shouldTakeOver(item({ filename: "b.epub", url: "https://e.com/b.epub" }), rules), true);
  assert.equal(R.shouldTakeOver(item({ filename: "c.pdf", url: "https://e.com/c.pdf" }), rules), false);
  assert.equal(R.shouldTakeOver(item({ filename: "d.zip", url: "https://e.com/d.zip" }), rules), true);
});

test("blocking beats adding", () => {
  const rules = R.normalize({ extraExtensions: ["epub"], blockedExtensions: ["epub"] });
  assert.equal(R.shouldTakeOver(item({ filename: "b.epub", url: "https://e.com/b.epub" }), rules), false);
});

test("excluded sites include subdomains but not look-alikes", () => {
  const rules = R.normalize({ excludedHosts: ["example.com"] });
  assert.equal(R.shouldTakeOver(item({ url: "https://example.com/a.zip" }), rules), false);
  assert.equal(R.shouldTakeOver(item({ url: "https://dl.example.com/a.zip" }), rules), false);
  assert.equal(R.shouldTakeOver(item({ url: "https://notexample.com/a.zip" }), rules), true);
  assert.equal(R.shouldTakeOver(item({ url: "https://example.com.evil.net/a.zip" }), rules), true);
});

test("a download started from an excluded page is skipped (referrer)", () => {
  const rules = R.normalize({ excludedHosts: ["intranet.local"] });
  assert.equal(R.shouldTakeOver(item({ url: "https://cdn.net/a.zip", referrer: "https://intranet.local/downloads" }), rules), false);
});

test("minimum size for unrecognised files is configurable and clamped", () => {
  const big = R.normalize({ minUnknownMB: 50 });
  const f = (bytes) => item({ url: "https://e.com/x", filename: "x", mime: "application/octet-stream", totalBytes: bytes });
  assert.equal(R.shouldTakeOver(f(10 * MB), big), false);
  assert.equal(R.shouldTakeOver(f(60 * MB), big), true);
  assert.equal(R.normalize({ minUnknownMB: -5 }).minUnknownMB, 0.1);
  assert.equal(R.normalize({ minUnknownMB: 1e9 }).minUnknownMB, 10240);
  assert.equal(R.normalize({ minUnknownMB: "abc" }).minUnknownMB, 1);
});

test("normalize survives garbage from storage", () => {
  for (const raw of [undefined, null, 5, "x", [], { extraExtensions: 7, excludedHosts: {} }, { extraExtensions: [null, {}, "ok"] }]) {
    const r = R.normalize(raw);
    assert.ok(Array.isArray(r.extraExtensions) && Array.isArray(r.blockedExtensions) && Array.isArray(r.excludedHosts));
    assert.ok(Number.isFinite(r.minUnknownMB));
  }
});

test("sanitizeList reports what it rejects and cleans hosts", () => {
  const ext = R.sanitizeList("epub, ../x, dmg;;apk  a/b", "ext");
  eq(ext.valid, ["epub", "dmg", "apk"]);
  eq([...ext.rejected].sort(), ["../x", "a/b"].sort());

  const hosts = R.sanitizeList("https://WWW.Example.com:8080/path, *.cdn.net, bad_host!, 192.168.0.1, localhost", "host");
  eq(hosts.valid, ["www.example.com", "cdn.net", "192.168.0.1", "localhost"]);
  eq(hosts.rejected, ["bad_host!"]);
});

test("lists are de-duplicated and capped", () => {
  const many = Array.from({ length: 500 }, (_, i) => `ext${i}`);
  assert.equal(R.sanitizeList(many, "ext").valid.length, 200);
  eq(R.sanitizeList("zip, ZIP, .zip", "ext").valid, ["zip"]);
});
