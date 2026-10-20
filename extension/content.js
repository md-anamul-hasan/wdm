// WDM Video / Media floating download panel
(() => {
  let floatingBtn = null;
  let activeMediaUrls = new Set();

  chrome.runtime.onMessage.addListener((msg, sender, sendResponse) => {
    if (msg.type === "WDM_MEDIA_FOUND" && msg.url) {
      activeMediaUrls.add(msg.url);
      ensureFloatingButton();
    } else if (msg.type === "WDM_COLLECT_LINKS") {
      // Every distinct http(s) link on the page ("Download all links with WDM")
      const links = new Set();
      document.querySelectorAll("a[href]").forEach((a) => {
        if (/^https?:/i.test(a.href)) links.add(a.href);
      });
      sendResponse(Array.from(links));
    }
  });

  // Also check HTML5 video elements directly
  function scanPageMedia() {
    const videos = document.querySelectorAll("video, audio");
    videos.forEach((v) => {
      if (v.src && !v.src.startsWith("blob:")) {
        activeMediaUrls.add(v.src);
        ensureFloatingButton();
      }
    });
  }

  setInterval(scanPageMedia, 3000);

  function ensureFloatingButton() {
    if (activeMediaUrls.size === 0) return;
    if (floatingBtn) return;

    floatingBtn = document.createElement("div");
    floatingBtn.id = "wdm-floating-panel";
    floatingBtn.innerHTML = `
      <div style="
        position: fixed;
        bottom: 24px;
        right: 24px;
        z-index: 2147483647;
        background: #007acc;
        color: white;
        padding: 10px 16px;
        border-radius: 20px;
        font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
        font-size: 13px;
        font-weight: 600;
        box-shadow: 0 4px 16px rgba(0,0,0,0.3);
        cursor: pointer;
        display: flex;
        align-items: center;
        gap: 8px;
        transition: transform 0.2s, background 0.2s;
      ">
        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5">
          <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"></path>
          <polyline points="7 10 12 15 17 10"></polyline>
          <line x1="12" y1="15" x2="12" y2="3"></line>
        </svg>
        <span>Download with WDM (${activeMediaUrls.size})</span>
      </div>
    `;

    floatingBtn.addEventListener("click", () => {
      const urls = Array.from(activeMediaUrls);
      const targetUrl = urls[urls.length - 1]; // most recent
      chrome.runtime.sendMessage({
        action: "download",
        url: targetUrl,
        referer: window.location.href
      });
      floatingBtn.style.background = "#28a745";
      floatingBtn.querySelector("span").textContent = "Sent to WDM!";
      setTimeout(() => {
        if (floatingBtn) floatingBtn.remove();
        floatingBtn = null;
        activeMediaUrls.clear();
      }, 2000);
    });

    document.body.appendChild(floatingBtn);
  }
})();
