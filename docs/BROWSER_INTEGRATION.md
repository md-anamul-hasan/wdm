# Browser integration

WDM talks to Chrome, Edge, Brave and Firefox through a small *native messaging host* (`Wdm.NativeHost.exe`) and the *WDM Download Manager Integration* extension. The extension gives the host a download; the host hands it to the running WDM window over a per-user pipe (starting WDM first if needed). A browser download is only cancelled after WDM has confirmed it received the request; if WDM cannot be reached the browser's own download carries on.

Install the extension from the [Chrome Web Store](https://chromewebstore.google.com/detail/wdm-download-manager-inte/flnbghmpkajibfdfhbcfhkcjkhlokfgi) (Chrome, Edge, Brave) or load `extension\` unpacked. Firefox support exists in code but has not been tested.

## Takeover rules (extension 2.0)
Open the extension popup → **Takeover rules**. They apply when *Take over downloads* and the optional all-sites access are on.

| Rule | Effect |
|---|---|
| Also take over these file types | e.g. `epub, dmg` — added to the built-in list |
| Never take over these file types | e.g. `pdf` — always left to the browser (wins over the list above) |
| Never take over downloads from these sites | `example.com` also covers `cdn.example.com`; also applies when you start the download from that site |
| Take over other files bigger than (MB) | for file types WDM does not recognise; documents (text, JSON) are never taken over this way |

The button at the top of the section excludes (or re-includes) the site you are on. Rules are stored locally and never leave your browser.

## When it does not work — `--diagnose`
```
"%LOCALAPPDATA%\Programs\WDM\Wdm.NativeHost.exe" --diagnose
```
(adjust the path if you installed elsewhere). It checks, per browser, that the host is registered, the manifest is valid and points to an existing program, and that your extension is allowed — and says which command repairs what it finds. Exit code 1 means a real problem.

| Symptom | Likely cause | Fix |
|---|---|---|
| Popup says "WDM not found" | Store extension not allowed by the host (WDM 1.0.1 or older), or host not registered | Install WDM 2.0.0 (or run `Wdm.NativeHost.exe --register`), restart the browser |
| Extension works only after you start WDM | Normal: the host starts WDM; if that fails `--diagnose` shows `Wdm.Ui.exe` is missing | Reinstall WDM |
| A download is taken over although it should not be | Site/type not in your rules | Add it under Takeover rules |
| A download stays paused in the browser | Rare: the browser suspended the extension mid-hand-off | It resumes by itself when the extension restarts (extension 1.0.1 and newer); otherwise resume it in the browser's download list |
| Edge Add-ons / other store copy of the extension | New extension ID | `Wdm.NativeHost.exe --register --extension-id <id>` |

## Privacy
See [PRIVACY.md](PRIVACY.md). The host only listens to the browser that started it and forwards to the same Windows user's WDM; cookies are sent only to the site they belong to.
