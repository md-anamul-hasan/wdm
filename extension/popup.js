const statusPill = document.getElementById("hostStatus");
const statusText = document.getElementById("hostStatusText");
const takeoverToggle = document.getElementById("takeoverToggle");
const sendForm = document.getElementById("sendForm");
const urlInput = document.getElementById("urlInput");

document.getElementById("version").textContent = chrome.runtime.getManifest().version;

/** @param {"pending"|"ok"|"warn"|"error"} state */
function setStatus(text, state) {
  statusText.textContent = text;
  statusPill.dataset.state = state;
}

function send(message) {
  return new Promise((resolve) => {
    chrome.runtime.sendMessage(message, (response) => resolve(chrome.runtime.lastError ? null : response));
  });
}

async function checkHost() {
  const res = await send({ action: "ping" });
  if (res?.connected) setStatus(`Connected v${res.version}`, "ok");
  else setStatus("WDM not found", "error");
}

async function sendUrl(event) {
  event.preventDefault();

  const url = urlInput.value.trim();
  if (!/^https?:\/\//i.test(url)) {
    setStatus("Enter an http(s) link", "warn");
    urlInput.focus();
    return;
  }

  const res = await send({ action: "download", url });
  if (res?.success) {
    setStatus("Sent to WDM", "ok");
    setTimeout(() => window.close(), 600);
  } else {
    setStatus("Could not reach WDM", "error");
  }
}

chrome.storage.local.get("takeover").then(({ takeover = true }) => {
  takeoverToggle.checked = takeover;
});

takeoverToggle.addEventListener("change", () => chrome.storage.local.set({ takeover: takeoverToggle.checked }));
sendForm.addEventListener("submit", sendUrl);

checkHost();
