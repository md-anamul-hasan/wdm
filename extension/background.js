const HOST_NAME = "com.wdm.download";

// File types WDM takes over by default (same list IDM ships with: Options > File types).
const DEFAULT_EXTENSIONS = new Set((
  "3gp 7z aac ace aif apk arj asf avi bin bz2 exe gz gzip img iso lzh m4a m4v mkv mov mp3 mp4 mpa mpe mpeg mpg " +
  "msi msu ogg ogv pdf pps ppt qt ra rar rm rmvb sea sit sitx tar tif tiff wav wma wmv z zip"
).split(" "));

const MIN_UNKNOWN_TYPE_BYTES = 1024 * 1024;
const MEDIA_REGEX = /\.(mp4|webm|mkv|flv|mp3|aac)(\?.*)?$/i;

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

chrome.runtime.onInstalled.addListener(() => {
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
});

chrome.contextMenus.onClicked.addListener(async (info, tab) => {
  if (info.menuItemId === "wdm_download_link") {
    const targetUrl = info.linkUrl || info.srcUrl;
    if (targetUrl) await sendDownloadToWdm(targetUrl, tab?.url);
  } else if (info.menuItemId === "wdm_download_page" && tab?.id != null) {
    try {
      const links = await chrome.tabs.sendMessage(tab.id, { type: "WDM_COLLECT_LINKS" });
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

chrome.downloads.onCreated.addListener(async (item) => {
  if (!(await isTakeoverEnabled()) || !shouldTakeOver(item)) return;

  try { await chrome.downloads.pause(item.id); } catch { /* already finished or not pausable */ }

  const url = item.finalUrl || item.url;
  const accepted = await sendDownloadToWdm(url, item.referrer);

  try {
    if (accepted) {
      await chrome.downloads.cancel(item.id);
      await chrome.downloads.erase({ id: item.id });
    } else {
      await chrome.downloads.resume(item.id);
    }
  } catch { /* download vanished while we were deciding */ }
});

// ---- media sniffing ---------------------------------------------------------------------------

chrome.webRequest.onHeadersReceived.addListener(
  (details) => {
    if (details.tabId < 0) return;
    if (details.type === "media" || MEDIA_REGEX.test(details.url)) {
      chrome.tabs.sendMessage(details.tabId, { type: "WDM_MEDIA_FOUND", url: details.url }).catch(() => {});
    }
  },
  { urls: ["<all_urls>"] },
  ["responseHeaders"]
);
