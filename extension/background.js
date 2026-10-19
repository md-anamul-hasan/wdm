const HOST_NAME = "com.wdm.download";

// Context Menu Setup
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

chrome.contextMenus.onClicked.addListener((info, tab) => {
  if (info.menuItemId === "wdm_download_link") {
    const targetUrl = info.linkUrl || info.srcUrl;
    if (targetUrl) {
      sendDownloadToWdm(targetUrl, tab?.url);
    }
  }
});

// Intercept browser downloads
chrome.downloads.onDeterminingFilename.addListener((item, suggest) => {
  // If download was not triggered by user canceling WDM, pass to WDM
  if (item.url && !item.url.startsWith("blob:") && !item.url.startsWith("data:")) {
    sendDownloadToWdm(item.url, item.referrer);
    // Cancel chrome built-in download
    chrome.downloads.cancel(item.id);
  }
});

// Sniff media requests
const mediaRegex = /\.(mp4|m3u8|webm|mkv|flv|mp3|aac|ts)(\?.*)?$/i;
chrome.webRequest.onHeadersReceived.addListener(
  (details) => {
    if (details.type === "media" || mediaRegex.test(details.url)) {
      chrome.tabs.query({ active: true, currentWindow: true }, (tabs) => {
        if (tabs[0]?.id === details.tabId) {
          chrome.tabs.sendMessage(details.tabId, {
            type: "WDM_MEDIA_FOUND",
            url: details.url
          }).catch(() => {});
        }
      });
    }
  },
  { urls: ["<all_urls>"] },
  ["responseHeaders"]
);

function sendDownloadToWdm(url, referer) {
  try {
    chrome.runtime.sendNativeMessage(
      HOST_NAME,
      {
        action: "download",
        url: url,
        referer: referer || ""
      },
      (response) => {
        if (chrome.runtime.lastError) {
          console.warn("[WDM Extension] Native host warning:", chrome.runtime.lastError.message);
        } else {
          console.log("[WDM Extension] Native host response:", response);
        }
      }
    );
  } catch (err) {
    console.error("[WDM Extension] Native messaging error:", err);
  }
}
