# Contributing

## Setup
Rust (1.80+), Node 20+ and Windows 10/11 to run the app (the core crate and its tests build on any OS). Read `.agents/AGENTS.md` first.

```
cd src-ui && npm ci && npm run build && cd ..   # the desktop crate embeds src-ui/dist, so build the UI first
cargo build --workspace
cargo test --workspace
cargo clippy --workspace --all-targets -- -D warnings
node --test "tests/extension/*.test.mjs"
```

## Rules
- Every bug fix ships with a regression test that fails without the fix. Engine tests use the in-process loopback servers in `crates/wdm-core/tests/common`; no internet in tests.
- Keep `wdm-core` free of UI/OS-specific code; preserve settings, `downloads.json` and `.wdm-state` compatibility (add fields, don't rename).
- Don't weaken security checks to make a test pass; don't add telemetry or network calls other than user-initiated downloads.
- Check a change to the window by driving the release exe, not only the Vite page (see `AGENTS.md`, rule 12).
- Release steps: see `docs/audit/RELEASE_AUDIT.md` and `docs/audit/OWNER_ACTION_PLAN.md`.

## Browser extension
Load `extension/` unpacked (Developer mode). Native host registration: `wdm-nativehost.exe --register [--extension-id <id>]`. Chrome Web Store ID: `flnbghmpkajibfdfhbcfhkcjkhlokfgi`.

## Store screenshots
The popup screenshots in `docs/media/screenshots` are committed. After changing `extension/popup.*`, regenerate them and rebuild the store images:

```
npm install --no-save playwright
node scripts/capture-extension-screens.mjs
.\scripts\make-store-media.ps1
```
