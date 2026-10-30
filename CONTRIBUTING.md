# Contributing

## Setup
.NET 10 SDK; Windows 10/11 to run the app (the core library and tests build on any OS). Node ≥ 20 for the extension tests.

```
dotnet restore Wdm.slnx
dotnet build Wdm.slnx -c Release                    # Windows
dotnet build Wdm.slnx -c Release -p:EnableWindowsTargeting=true   # Linux/macOS: compiles the WPF UI, cannot run it
dotnet test tests/Wdm.Tests -c Release
node --test tests/extension/background.test.mjs
```

## Rules
- Every bug fix ships with a regression test that fails without the fix. Use `TestServer` (in-process, loopback); no internet in tests.
- Tests that use the IPC pipe belong to `[Collection("NamedPipe")]`.
- Keep Core free of UI/OS-specific code; preserve settings, `downloads.json` and `.wdm-state` compatibility (add fields, don't rename).
- Don't weaken security checks to make a test pass; don't add telemetry or network calls other than user-initiated downloads.
- Release steps: see `docs/audit/RELEASE_AUDIT.md` and `docs/audit/OWNER_ACTION_PLAN.md`.

## Browser extension
Load `extension/` unpacked (Developer mode). Native host registration: `Wdm.NativeHost.exe --register [--extension-id <id>]`. Chrome Web Store ID: `flnbghmpkajibfdfhbcfhkcjkhlokfgi`.

## Store screenshots
The popup screenshots in `docs/media/screenshots` are committed. After changing `extension/popup.*`, regenerate them and rebuild the store images:

```
npm install --no-save playwright
node scripts/capture-extension-screens.mjs
.\scripts\make-store-media.ps1
```
