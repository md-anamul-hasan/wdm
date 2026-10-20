const statusBadge = document.getElementById("hostStatus");
const takeoverToggle = document.getElementById("takeoverToggle");
const urlInput = document.getElementById("urlInput");
const downloadBtn = document.getElementById("downloadBtn");

document.getElementById("version").textContent = chrome.runtime.getManifest().version;

function setStatus(text, ok) {
  statusBadge.textContent = text;
  statusBadge.style.background = ok ? "#059669" : "#dc2626";
}

chrome.runtime.sendMessage({ action: "ping" }, (res) => {
  if (chrome.runtime.lastError || !res?.connected) {
    setStatus("Host not found", false);
  } else {
    setStatus(`Connected v${res.version}`, true);
  }
});

chrome.storage.local.get("takeover").then(({ takeover = true }) => {
  takeoverToggle.checked = takeover;
});

takeoverToggle.addEventListener("change", () => {
  chrome.storage.local.set({ takeover: takeoverToggle.checked });
});

downloadBtn.addEventListener("click", () => {
  const url = urlInput.value.trim();
  if (!/^https?:\/\//i.test(url)) {
    setStatus("Enter an http(s) URL", false);
    return;
  }

  chrome.runtime.sendMessage({ action: "download", url }, (res) => {
    if (chrome.runtime.lastError || !res?.success) {
      setStatus("Failed - run Wdm.NativeHost --register", false);
    } else {
      setStatus("Sent to WDM", true);
      setTimeout(() => window.close(), 600);
    }
  });
});
