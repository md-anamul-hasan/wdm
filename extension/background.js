const HOST_NAME = "com.wdm.download";

// File types WDM takes over by default (same list IDM ships with: Options > File types).
const DEFAULT_EXTENSIONS = new Set((
  "3gp 7z aac ace aif apk arj asf avi bin bz2 exe gz gzip img iso lzh m4a m4v mkv mov mp3 mp4 mpa mpe mpeg mpg " +
  "msi msu ogg ogv pdf pps ppt qt ra rar rm rmvb sea sit sitx tar tif tiff wav wma wmv z zip"
).split(" "));

const MIN_UNKNOWN_TYPE_BYTES = 1024 * 1024;
const MEDIA_REGEX = /\.(mp4|webm|mkv|flv|mp3|aac)(\?.*)?$/i;

// Site access is optional. Without it the extension works from explicit clicks only (activeTab): the toolbar
// popup, the right-click menus. With it WDM can also take over downloads, use the visitor's login cookies for
// them and spot media on pages. The user grants it from the popup.
const ALL_SITES = { origins: ["<all_urls>"] };
const MEDIA_SCRIPT_ID = "wdm-media";

const hasAllSites = () => chrome.permissions.contains(ALL_SITES);

// ---- settings ---------------------------------------------------------------------------------

async function isTakeoverEnabled() {
  const { takeover = true } = await chrome.storage.local.get("takeover");
  return takeover;
}

// ---- native host ------------------------------------------------------------------------------

function sendToHost(message) {
  return new Promise((resolve) => {
    try {
      chrome.runtime.sendNativeMessage(HOST_NAME, message, (response) => {
        if (chrome.runtime.lastError) {
          console.warn("[WDM] Native host unavailable:", chrome.runtime.lastError.message);
          resolve(null);
        } else {
          resolve(response);
        }
      });
    } catch (err) {
      console.error("[WDM] Native messaging error:", err);
      resolve(null);
    }
  });
}

/** Cookies are only readable for sites the extension has access to; anything else is sent without. */
async function cookieHeaderFor(url) {
  try {
    const cookies = await chrome.cookies.getAll({ url });
    return cookies.map((c) => `${c.name}=${c.value}`).join("; ");
  } catch {
    return "";
  }
}

async function sendDownloadToWdm(url, referer, fileName) {
  const response = await sendToHost({
    action: "download",
    url,
    referer: referer || "",
    cookies: await cookieHeaderFor(url),
    userAgent: navigator.userAgent,
    fileName: fileName || ""
  });
  return response?.status === "ok";
}

async function sendBatchToWdm(urls, referer) {
  if (urls.length === 0) return false;
  const response = await sendToHost({
    action: "batch",
    urls,
    referer: referer || "",
    cookies: await cookieHeaderFor(urls[0]),
    userAgent: navigator.userAgent
  });
  return response?.status === "ok";
}

// ---- context menus ----------------------------------------------------------------------------

chrome.runtime.onInstalled.addListener(({ reason }) => {
  chrome.contextMenus.create({
    id: "wdm_download_link",
    title: "Download with WDM",
    contexts: ["link", "video", "audio"]
  });
  chrome.contextMenus.create({
    id: "wdm_download_page",
    title: "Download all links with WDM",
    contexts: ["page"]
  });

  syncSiteAccess();

  // First run: show the popup in a tab so the user can decide about site access
  if (reason === "install") chrome.tabs.create({ url: chrome.runtime.getURL("popup.html?welcome=1") });
});

chrome.runtime.onStartup.addListener(syncSiteAccess);

/** Runs inside the page (activeTab grants access after the click); returns every distinct http(s) link. */
function collectPageLinks() {
  const links = new Set();
  document.querySelectorAll("a[href]").forEach((a) => {
    if (/^https?:/i.test(a.href)) links.add(a.href);
  });
  return Array.from(links);
}

chrome.contextMenus.onClicked.addListener(async (info, tab) => {
  if (info.menuItemId === "wdm_download_link") {
    const targetUrl = info.linkUrl || info.srcUrl;
    if (targetUrl) await sendDownloadToWdm(targetUrl, tab?.url);
  } else if (info.menuItemId === "wdm_download_page" && tab?.id != null) {
    try {
      const [{ result: links }] = await chrome.scripting.executeScript({ target: { tabId: tab.id }, func: collectPageLinks });
      await sendBatchToWdm(links || [], tab.url);
    } catch (err) {
      console.warn("[WDM] Could not collect page links:", err);
    }
  }
});

// ---- messages from content script / popup -----------------------------------------------------

chrome.runtime.onMessage.addListener((msg, sender, sendResponse) => {
  if (msg.action === "download" && msg.url) {
    sendDownloadToWdm(msg.url, msg.referer || sender.tab?.url).then((ok) => sendResponse({ success: ok }));
    return true; // async response
  }
  if (msg.action === "ping") {
    sendToHost({ action: "ping" }).then((res) => sendResponse({ connected: res?.status === "ok", version: res?.version }));
    return true;
  }
});

// ---- browser download takeover ----------------------------------------------------------------
// The browser starts the download first; we pause it, offer it to WDM, and only cancel the browser's
// copy once WDM confirms it has taken the job. If WDM is unreachable the browser download resumes untouched.

function extensionOf(name) {
  const clean = (name || "").split(/[?#]/)[0];
  const dot = clean.lastIndexOf(".");
  return dot >= 0 ? clean.slice(dot + 1).toLowerCase() : "";
}

function shouldTakeOver(item) {
  if (item.incognito) return false;
  const url = item.finalUrl || item.url || "";
  if (!/^https?:/i.test(url)) return false;

  const ext = extensionOf(item.filename) || extensionOf(new URL(url).pathname);
  if (DEFAULT_EXTENSIONS.has(ext)) return true;

  const mime = (item.mime || "").toLowerCase();
  const isDocument = mime.startsWith("text/") || mime === "application/json" || mime === "application/xhtml+xml";
  return !isDocument && item.totalBytes >= MIN_UNKNOWN_TYPE_BYTES;
}

// The service worker can be killed while a handoff is in flight (WDM may take seconds to start), which would leave
// the browser's copy paused forever. Handoffs are therefore journalled in session storage and settled on worker start:
// "pending" -> resume the browser download; "accepted" -> WDM already owns it, finish cancelling the browser copy.
const HANDOFF_KEY = "wdmHandoffs";

async function setHandoff(id, state) {
  const { [HANDOFF_KEY]: journal = {} } = await chrome.storage.session.get(HANDOFF_KEY);
  if (state) journal[id] = state; else delete journal[id];
  await chrome.storage.session.set({ [HANDOFF_KEY]: journal });
}

async function settleOrphanedHandoffs() {
  const { [HANDOFF_KEY]: journal = {} } = await chrome.storage.session.get(HANDOFF_KEY);
  for (const [id, state] of Object.entries(journal)) {
    try {
      if (state === "accepted") {
        await chrome.downloads.cancel(Number(id));
        await chrome.downloads.erase({ id: Number(id) });
      } else {
        await chrome.downloads.resume(Number(id));
      }
    } catch { /* download finished or vanished */ }
  }
  await chrome.storage.session.remove(HANDOFF_KEY);
}

chrome.downloads.onCreated.addListener(async (item) => {
  // Taking over needs the visitor's cookies for the download's site, otherwise logged-in downloads would fail in WDM.
  // Without site access the browser simply keeps its download.
  if (!(await isTakeoverEnabled()) || !shouldTakeOver(item) || !(await hasAllSites())) return;

  try { await chrome.downloads.pause(item.id); } catch { /* already finished or not pausable */ }
  await setHandoff(item.id, "pending");

  const url = item.finalUrl || item.url;
  const accepted = await sendDownloadToWdm(url, item.referrer);
  if (accepted) await setHandoff(item.id, "accepted");

  try {
    if (accepted) {
      await chrome.downloads.cancel(item.id);
      await chrome.downloads.erase({ id: item.id });
    } else {
      await chrome.downloads.resume(item.id);
    }
  } catch { /* download vanished while we were deciding */ }
  await setHandoff(item.id, null);
});

// Every worker start is a fresh context, so any journalled handoff belongs to a worker that died mid-flight.
settleOrphanedHandoffs().catch((err) => console.warn("[WDM] Could not settle earlier downloads:", err));

// ---- media sniffing (needs site access) -------------------------------------------------------

function onMediaHeaders(details) {
  if (details.tabId < 0) return;
  if (details.type === "media" || MEDIA_REGEX.test(details.url)) {
    chrome.tabs.sendMessage(details.tabId, { type: "WDM_MEDIA_FOUND", url: details.url }).catch(() => {});
  }
}

// Registered at start-up so the worker is woken for it; Chrome delivers events only for sites the user granted.
try {
  chrome.webRequest.onHeadersReceived.addListener(onMediaHeaders, { urls: ["<all_urls>"] }, ["responseHeaders"]);
} catch (err) {
  console.warn("[WDM] Media detection unavailable until site access is granted:", err.message);
}

/** The page button is a content script, only registered while the user has granted site access. */
async function syncSiteAccess() {
  const granted = await hasAllSites();
  const registered = (await chrome.scripting.getRegisteredContentScripts({ ids: [MEDIA_SCRIPT_ID] })).length > 0;

  if (granted && !registered) {
    await chrome.scripting.registerContentScripts([{
      id: MEDIA_SCRIPT_ID,
      matches: ["<all_urls>"],
      js: ["content.js"],
      runAt: "document_end",
      persistAcrossSessions: true
    }]);
  } else if (!granted && registered) {
    await chrome.scripting.unregisterContentScripts({ ids: [MEDIA_SCRIPT_ID] });
  }
}

chrome.permissions.onAdded.addListener(syncSiteAccess);
chrome.permissions.onRemoved.addListener(syncSiteAccess);
