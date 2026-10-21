// WDM content script (registered only while the user has granted site access): offers a "Download with WDM"
// button on pages that play media. The button lives in a shadow root so page CSS cannot restyle it.
(() => {
  const SCAN_INTERVAL_MS = 3000;
  const SENT_VISIBLE_MS = 2000;

  const STYLES = `
    :host { all: initial; }
    .panel {
      position: fixed; right: 24px; bottom: 24px; z-index: 2147483647;
      display: flex; align-items: center; gap: 4px;
      padding: 4px 4px 4px 4px; border-radius: 14px;
      background: #2563eb; color: #ffffff;
      font: 600 13px/1 "Segoe UI Variable Text", "Segoe UI", system-ui, -apple-system, sans-serif;
      box-shadow: 0 8px 24px rgba(15, 23, 42, 0.28);
      transition: background 0.15s;
    }
    .panel[data-state="sent"] { background: #16a34a; }
    button { all: unset; box-sizing: border-box; cursor: pointer; color: inherit; }
    button:focus-visible { outline: 2px solid #ffffff; outline-offset: 1px; }
    .go { display: flex; align-items: center; gap: 8px; padding: 9px 12px 9px 12px; border-radius: 10px; }
    .go:hover { background: rgba(255, 255, 255, 0.14); }
    .dismiss { display: grid; place-items: center; width: 28px; height: 28px; border-radius: 8px; opacity: 0.8; }
    .dismiss:hover { background: rgba(255, 255, 255, 0.14); opacity: 1; }
    svg { width: 16px; height: 16px; flex: none; }
    @media (prefers-color-scheme: dark) {
      .panel { background: #4c8dff; color: #0b1220; box-shadow: 0 8px 24px rgba(0, 0, 0, 0.5); }
      .panel[data-state="sent"] { background: #4ade80; }
      button:focus-visible { outline-color: #0b1220; }
      .go:hover, .dismiss:hover { background: rgba(11, 18, 32, 0.12); }
    }
  `;

  const ICON_DOWNLOAD =
    '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">' +
    '<path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"/><polyline points="7 10 12 15 17 10"/><line x1="12" y1="15" x2="12" y2="3"/></svg>';
  const ICON_CLOSE =
    '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" aria-hidden="true">' +
    '<line x1="6" y1="6" x2="18" y2="18"/><line x1="18" y1="6" x2="6" y2="18"/></svg>';

  const mediaUrls = new Set();
  let host = null;      // element attached to the page
  let panel = null;     // .panel inside the shadow root
  let label = null;     // text of the main button

  // ---- messages from the background worker ------------------------------------------------------

  chrome.runtime.onMessage.addListener((msg) => {
    if (msg.type === "WDM_MEDIA_FOUND" && msg.url) addMedia(msg.url);
  });

  // ---- media discovery --------------------------------------------------------------------------

  function addMedia(url) {
    mediaUrls.add(url);
    showPanel();
    updateLabel();
  }

  function scanPageMedia() {
    document.querySelectorAll("video, audio").forEach((el) => {
      if (el.src && !el.src.startsWith("blob:")) addMedia(el.src);
    });
  }

  setInterval(scanPageMedia, SCAN_INTERVAL_MS);

  // ---- floating panel ---------------------------------------------------------------------------

  function updateLabel() {
    if (label) label.textContent = `Download with WDM (${mediaUrls.size})`;
  }

  function showPanel() {
    if (host || mediaUrls.size === 0) return;

    host = document.createElement("div");
    host.id = "wdm-floating-panel";
    const root = host.attachShadow({ mode: "closed" });

    const style = document.createElement("style");
    style.textContent = STYLES;

    panel = document.createElement("div");
    panel.className = "panel";

    const go = document.createElement("button");
    go.className = "go";
    go.type = "button";
    go.innerHTML = ICON_DOWNLOAD;
    label = document.createElement("span");
    go.appendChild(label);
    go.addEventListener("click", sendLatest);

    const dismiss = document.createElement("button");
    dismiss.className = "dismiss";
    dismiss.type = "button";
    dismiss.setAttribute("aria-label", "Hide WDM button");
    dismiss.innerHTML = ICON_CLOSE;
    dismiss.addEventListener("click", hidePanel);

    panel.append(go, dismiss);
    root.append(style, panel);
    updateLabel();
    document.body.appendChild(host);
  }

  function hidePanel() {
    host?.remove();
    host = panel = label = null;
    mediaUrls.clear();
  }

  function sendLatest() {
    const urls = Array.from(mediaUrls);
    chrome.runtime.sendMessage({ action: "download", url: urls[urls.length - 1], referer: window.location.href });

    panel.dataset.state = "sent";
    label.textContent = "Sent to WDM";
    setTimeout(hidePanel, SENT_VISIBLE_MS);
  }
})();
