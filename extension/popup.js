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

// ---- takeover rules ---------------------------------------------------------------------------

const extraExt = document.getElementById("extraExt");
const blockedExt = document.getElementById("blockedExt");
const excludedHosts = document.getElementById("excludedHosts");
const minMb = document.getElementById("minMb");
const rulesMsg = document.getElementById("rulesMsg");
const siteBtn = document.getElementById("siteBtn");
let currentHost = "";

const join = (list) => list.join(", ");

function showRules(rules) {
  extraExt.value = join(rules.extraExtensions);
  blockedExt.value = join(rules.blockedExtensions);
  excludedHosts.value = join(rules.excludedHosts);
  minMb.value = String(rules.minUnknownMB);
  renderSiteButton(rules);
}

/** Reads the form; returns the rules to store and any entries that were not valid. */
function readRules() {
  const parts = {
    extraExtensions: WdmRules.sanitizeList(extraExt.value, "ext"),
    blockedExtensions: WdmRules.sanitizeList(blockedExt.value, "ext"),
    excludedHosts: WdmRules.sanitizeList(excludedHosts.value, "host"),
  };
  const rules = WdmRules.normalize({
    extraExtensions: parts.extraExtensions.valid,
    blockedExtensions: parts.blockedExtensions.valid,
    excludedHosts: parts.excludedHosts.valid,
    minUnknownMB: minMb.value,
  });
  const rejected = [...parts.extraExtensions.rejected, ...parts.blockedExtensions.rejected, ...parts.excludedHosts.rejected];
  return { rules, rejected };
}

async function saveRules() {
  const { rules, rejected } = readRules();
  await chrome.storage.local.set({ rules });
  showRules(rules);
  rulesMsg.textContent = rejected.length ? `Saved. Ignored invalid entries: ${rejected.join(", ")}` : "Saved.";
}

async function resetRules() {
  await chrome.storage.local.remove("rules");
  showRules(WdmRules.normalize(null));
  rulesMsg.textContent = "Back to the defaults.";
}

function renderSiteButton(rules) {
  if (!currentHost) { siteBtn.hidden = true; return; }
  const excluded = WdmRules.hostMatches(currentHost, rules.excludedHosts);
  siteBtn.hidden = false;
  siteBtn.textContent = excluded ? `Take over downloads from ${currentHost} again` : `Never take over downloads from ${currentHost}`;
}

siteBtn.addEventListener("click", async () => {
  const { rules } = readRules();
  const excluded = WdmRules.hostMatches(currentHost, rules.excludedHosts);
  const hosts = excluded
    ? rules.excludedHosts.filter((h) => h !== currentHost && !currentHost.endsWith("." + h))
    : [...rules.excludedHosts, currentHost];
  await chrome.storage.local.set({ rules: { ...rules, excludedHosts: hosts } });
  showRules(WdmRules.normalize({ ...rules, excludedHosts: hosts }));
  rulesMsg.textContent = excluded ? `Rules updated for ${currentHost}.` : `${currentHost} will no longer be taken over.`;
});

document.getElementById("saveRules").addEventListener("click", saveRules);
document.getElementById("resetRules").addEventListener("click", resetRules);

(async () => {
  try {
    const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
    if (tab?.url && /^https?:/i.test(tab.url)) currentHost = new URL(tab.url).hostname.toLowerCase();
  } catch { /* no tab information: the shortcut simply stays hidden */ }
  const { rules } = await chrome.storage.local.get("rules");
  showRules(WdmRules.normalize(rules));
})();
