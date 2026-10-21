const statusPill = document.getElementById("hostStatus");
const statusText = document.getElementById("hostStatusText");
const takeoverToggle = document.getElementById("takeoverToggle");
const sendForm = document.getElementById("sendForm");
const urlInput = document.getElementById("urlInput");

const accessCard = document.getElementById("access");
const accessTitle = document.getElementById("accessTitle");
const accessText = document.getElementById("accessText");
const accessBtn = document.getElementById("accessBtn");
const takeoverHint = document.getElementById("takeoverHint");
const takeoverRow = takeoverToggle.closest(".switch");

const ALL_SITES = { origins: ["<all_urls>"] };

document.getElementById("version").textContent = chrome.runtime.getManifest().version;
if (new URLSearchParams(location.search).has("welcome")) document.body.classList.add("welcome");

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

/** Site access is optional: the card either asks for it or lets the user turn it off again. */
async function renderAccess() {
  const granted = await chrome.permissions.contains(ALL_SITES);

  accessCard.hidden = false;
  accessTitle.textContent = granted ? "All-sites access is on" : "Allow access to all sites";
  accessText.textContent = granted
    ? "WDM can take over downloads, use your login for them and spot videos on pages."
    : "Optional. Lets WDM take over downloads, use your login for them and spot videos on pages. " +
      "Nothing is read except links you send to WDM. Without it, the popup and right-click menus still work.";
  accessBtn.textContent = granted ? "Turn off" : "Allow access";

  takeoverToggle.disabled = !granted;
  takeoverRow.classList.toggle("disabled", !granted);
  takeoverHint.textContent = granted ? "Large files and archives open in WDM" : "Needs all-sites access";
}

accessBtn.addEventListener("click", async () => {
  // permissions.request must run straight from the click
  if (await chrome.permissions.contains(ALL_SITES)) await chrome.permissions.remove(ALL_SITES);
  else await chrome.permissions.request(ALL_SITES);
  renderAccess();
});

chrome.storage.local.get("takeover").then(({ takeover = true }) => {
  takeoverToggle.checked = takeover;
});

takeoverToggle.addEventListener("change", () => chrome.storage.local.set({ takeover: takeoverToggle.checked }));
sendForm.addEventListener("submit", sendUrl);

checkHost();
renderAccess();
