# AGENTS.md — WDM (Windows Download Manager)

Guidance for AI coding agents (Codex, etc.) working in this repository.
Review every diff before merging. Keep changes small and reviewable.

## Solution layout

The solution is built on a **Modern Native Stack (Rust + Tauri v2 + React / TypeScript)**:
- `crates/wdm-core` — Rust download engine: HTTP range segmentation, resume state, persistence, DPAPI crypto, zero-merge direct storage writer, named pipe IPC, site grabber, and media resolver.
- `crates/wdm-cli` — Standalone terminal downloader (`wdm.exe`).
- `crates/wdm-nativehost` — Browser Native Messaging Host (`wdm-nativehost.exe`).
- `crates/wdm-desktop` — Tauri v2 desktop application core (`wdm-desktop.exe`).
- `src-ui/` — React 19 + TypeScript + Vite frontend.

## Hard rules

1. **NativeHost stdout is protocol-only.** It carries length-prefixed JSON frames
   (32-bit native-endian length + UTF-8 JSON) and nothing else. Never call
   `println!`/`print!` or write to stdout there. Send diagnostics to `stderr` or a log file.
2. **No blocking on async work.** Never block async executors or call synchronous blocking operations inside async tasks. Use `tokio::task::spawn_blocking` only for long-running disk or crypto operations.
3. **Data Compatibility.**
   - On-disk JSON formats (`settings.json`, `downloads.json`, `*.wdm-state`) must remain 100% byte/schema compatible (PascalCase JSON fields).
   - Secrets must use Windows DPAPI under `CurrentUser` scope with entropy `WDM.secret.v1` and prefix `dpapi1:`.
   - Named pipe IPC address must be `WDM.Ipc.<username>` handling `ExternalDownloadRequest` frames.
4. **Buffers.** Use zero-copy `bytes::Bytes` or chunk buffers; avoid unbounded allocations.
5. **Tests.** Every state mutation or new public behavior in `crates/wdm-core` ships with tests.
6. **No new dependencies** without stating why in the commit message / PR description.
7. **Never commit** secrets, signing material, or user data paths.

## Desktop app rules (found the hard way in 2.5.0)

8. **UI contract.** Events and commands send the UI names and plain numbers (see `progress_json` in `crates/wdm-desktop/src/state.rs`), never the on-disk serde forms (`DownloadStatus` is a number there, segment state is skipped).
9. **Capabilities.** `crates/wdm-desktop/capabilities/default.json` grants the window only what it uses. Without it the window gets no events. Add a permission there when the UI starts using a new plugin.
10. **Embedded UI.** `custom-protocol` stays a default feature of `wdm-desktop`, and `src-ui` is built before any cargo step (CI, release). A binary without it opens a blank window.
11. **Names from outside** (browser, pipe, command line) go through `sanitize_file_name` before they become a path.
12. **QA of the window** means driving the release exe (`WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=9222`, Playwright `connectOverCDP`) against a local range server, not the Vite page with a mocked `invoke`.

## Build and test

```bash
# Build Rust workspace
cargo build --workspace

# Run all Rust tests
cargo test --workspace

# Lint with Clippy
cargo clippy --workspace --all-targets -- -D warnings

# Build frontend UI
cd src-ui && npm run build

# Run browser extension tests
node --test "tests/extension/*.test.mjs"
```

All tests must pass before a change is considered done. Report the real pass/fail counts.

## PR / commit conventions

- Conventional commits (`feat(ui): ...`, `test(core): ...`, `feat(desktop): ...`).
- Note in the commit body which parts were agent-assisted and what was changed in review.
