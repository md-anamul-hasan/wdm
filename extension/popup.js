document.getElementById("downloadBtn").addEventListener("click", () => {
  const url = document.getElementById("urlInput").value.trim();
  if (!url) return;

  chrome.runtime.sendNativeMessage(
    "com.wdm.download",
    { action: "download", url },
    (res) => {
      if (chrome.runtime.lastError) {
        alert("Failed to connect to WDM Native Host. Did you run Wdm.NativeHost --register?");
      } else {
        alert("Download sent to WDM!");
        window.close();
      }
    }
  );
});
