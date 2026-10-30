// Takeover rules: which browser downloads WDM takes over. Pure functions, no browser APIs, shared by the background
// worker (importScripts / manifest "scripts") and the popup (<script>). Stored in chrome.storage.local under "rules".
(() => {
  // File types WDM takes over by default (same list IDM ships with: Options > File types).
  const DEFAULT_EXTENSIONS = (
    "3gp 7z aac ace aif apk arj asf avi bin bz2 exe gz gzip img iso lzh m4a m4v mkv mov mp3 mp4 mpa mpe mpeg mpg " +
    "msi msu ogg ogv pdf pps ppt qt ra rar rm rmvb sea sit sitx tar tif tiff wav wma wmv z zip"
  ).split(" ");

  const MB = 1024 * 1024;
  const MAX_ENTRIES = 200;
  const MIN_SIZE_MB = 0.1;
  const MAX_SIZE_MB = 10240;

  const cleanExtension = (s) => String(s).trim().toLowerCase().replace(/^[*.\s]+/, "");
  const isExtension = (s) => /^[a-z0-9]{1,12}$/.test(s);

  /** "https://www.Example.com:8080/path" -> "www.example.com"; "*.example.com" -> "example.com". */
  function cleanHost(s) {
    let host = String(s).trim().toLowerCase();
    host = host.replace(/^[a-z][a-z0-9+.-]*:\/\//, "").split(/[/?#]/)[0];
    return host.replace(/^\*\./, "").replace(/:\d+$/, "").replace(/\.$/, "");
  }
  const isHost = (h) => h.length > 0 && h.length <= 253 && /^[a-z0-9]([a-z0-9-]*[a-z0-9])?(\.[a-z0-9]([a-z0-9-]*[a-z0-9])?)*$/.test(h);

  /** Splits free text ("epub, dmg;apk") into entries. */
  const parseList = (text) => String(text ?? "").split(/[\s,;]+/).filter(Boolean);

  /** @returns {{valid: string[], rejected: string[]}} entries cleaned, de-duplicated and capped; bad ones reported, not kept. */
  function sanitizeList(values, kind) {
    const list = Array.isArray(values) ? values : parseList(values);
    const clean = kind === "host" ? cleanHost : cleanExtension;
    const ok = kind === "host" ? isHost : isExtension;
    const valid = [];
    const rejected = [];
    for (const raw of list) {
      const value = clean(raw);
      if (!ok(value)) { rejected.push(String(raw)); continue; }
      if (!valid.includes(value) && valid.length < MAX_ENTRIES) valid.push(value);
    }
    return { valid, rejected };
  }

  /** Turns whatever is in storage (possibly missing, old or hand-edited) into safe rules. */
  function normalize(raw) {
    const r = raw && typeof raw === "object" ? raw : {};
    const size = Number(r.minUnknownMB);
    return {
      extraExtensions: sanitizeList(r.extraExtensions, "ext").valid,
      blockedExtensions: sanitizeList(r.blockedExtensions, "ext").valid,
      excludedHosts: sanitizeList(r.excludedHosts, "host").valid,
      minUnknownMB: Number.isFinite(size) ? Math.min(MAX_SIZE_MB, Math.max(MIN_SIZE_MB, size)) : 1,
    };
  }

  function extensionOf(name) {
    const clean = (name || "").split(/[?#]/)[0];
    const dot = clean.lastIndexOf(".");
    return dot >= 0 ? clean.slice(dot + 1).toLowerCase() : "";
  }

  const hostMatches = (host, list) => !!host && list.some((entry) => host === entry || host.endsWith("." + entry));

  function hostOf(url) {
    try { return new URL(url).hostname.toLowerCase(); } catch { return ""; }
  }

  /** Should WDM take over this browser download (a chrome.downloads.DownloadItem)? */
  function shouldTakeOver(item, rules) {
    if (item.incognito) return false;
    const url = item.finalUrl || item.url || "";
    if (!/^https?:/i.test(url)) return false;

    // A site is excluded if the file comes from it or the user is on it when starting the download
    if (hostMatches(hostOf(url), rules.excludedHosts) || hostMatches(hostOf(item.referrer || ""), rules.excludedHosts)) return false;

    let pathname = "";
    try { pathname = new URL(url).pathname; } catch { return false; }
    const ext = extensionOf(item.filename) || extensionOf(pathname);

    if (rules.blockedExtensions.includes(ext)) return false;
    if (DEFAULT_EXTENSIONS.includes(ext) || rules.extraExtensions.includes(ext)) return true;

    // Unknown type: only large, non-document files
    const mime = (item.mime || "").toLowerCase();
    const isDocument = mime.startsWith("text/") || mime === "application/json" || mime === "application/xhtml+xml";
    return !isDocument && item.totalBytes >= rules.minUnknownMB * MB;
  }

  globalThis.WdmRules = { DEFAULT_EXTENSIONS, normalize, sanitizeList, parseList, cleanHost, hostMatches, shouldTakeOver, extensionOf };
})();
