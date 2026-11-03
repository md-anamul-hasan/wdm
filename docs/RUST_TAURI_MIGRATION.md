# Modern Native Stack (Rust + Tauri) Migration Plan & Status

## Overview
WDM is migrating from a C# (.NET 10 / WPF) architecture to a modern native stack using **Rust** for the core download engine, native messaging host, and CLI, paired with **Tauri (v2) + React / TypeScript** for the user interface.

This document tracks architecture, migration phases, compatibility guarantees, and current progress.

---

## Target Architecture

```
                    ┌────────────────────────────┐
                    │    Browser Extension       │
                    │ (Chrome / Edge / Firefox)  │
                    └─────────────┬──────────────┘
                                  │ Stdio (4-byte length + JSON)
                                  ▼
                    ┌────────────────────────────┐
                    │      wdm-nativehost        │
                    │        (Rust CLI)          │
                    └─────────────┬──────────────┘
                                  │ Named Pipe (WDM.Ipc.<User>)
                                  ▼
┌─────────────────────────────────────────────────────────────┐
│                       WDM Application                       │
│  ┌───────────────────────────────────────────────────────┐  │
│  │ Tauri UI (Webview2 + React / TypeScript / Vite)       │  │
│  └──────────────────────────┬────────────────────────────┘  │
│                             │ Tauri IPC commands / events   │
│                             ▼                               │
│  ┌───────────────────────────────────────────────────────┐  │
│  │ wdm-desktop (Tauri Core / Rust)                       │  │
│  └──────────────────────────┬────────────────────────────┘  │
│                             │ Rust API calls                │
│                             ▼                               │
│  ┌───────────────────────────────────────────────────────┐  │
│  │ wdm-core (Rust Workspace Crate)                       │  │
│  │ - Dynamic Multipart Segmenter (Middle-Split)          │  │
│  │ - Zero-Merge StorageWriter                            │  │
│  │ - Resumption Engine & Checkpoints (.wdm-state)        │  │
│  │ - Token-Bucket SpeedLimiter                           │  │
│  │ - Network Prober & HTTP Connection Worker             │  │
│  │ - Windows DPAPI Secret Protection (dpapi1:<base64>)   │  │
│  │ - Named Pipe IPC Server & Client                      │  │
│  └───────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────┘
                               ▲
                               │
                    ┌──────────┴─────────────────┐
                    │          wdm-cli           │
                    │     (Terminal Runner)      │
                    └────────────────────────────┘
```

---

## Strict Compatibility Guarantees
To ensure seamless transitions and backward compatibility with user profiles created by the C# application:
1. **On-Disk Checkpoints (`*.wdm-state`)**:
   - PascalCase JSON serialization.
   - Exact enum mappings and numeric deserialization support.
2. **Download List (`%APPDATA%\WDM\downloads.json`)**:
   - Exact file schema and casing matching C#.
   - Automatic migration of legacy plaintext secrets to encrypted DPAPI on load.
   - Cleansing of credentials upon download completion.
3. **Partial Downloads (`*.wdm-part`)**:
   - Files are actively written to `.wdm-part` and atomically renamed to target file only upon SHA-256 verification and stream completion.
4. **Credential Protection**:
   - Encrypted with Windows DPAPI under `CurrentUser` scope and entropy `WDM.secret.v1`, tagged with prefix `dpapi1:`.
5. **IPC Pipe Protocol**:
   - Named pipe `WDM.Ipc.<Username>` using `ExternalDownloadRequest` JSON frames.
6. **Native Messaging Host Protocol**:
   - Protocol-only stdout/stdin: 32-bit native-endian length prefix followed by UTF-8 JSON.

---

## Migration Phases & Roadmap

### Phase 1: Rust Core Engine (`wdm-core`) — [COMPLETED]
- [x] Workspace scaffolding (`Cargo.toml`, `.gitignore`).
- [x] Domain models: `DownloadStatus`, `SegmentState`, `DownloadSegment`, `DownloadMetadata`, `DownloadProgress`, `AppSettings`, `DownloadRequestOptions`.
- [x] Security: Windows DPAPI cipher with entropy, legacy plaintext migration, and memory zeroing.
- [x] Segmentation engine: `DynamicSegmenter` with middle-split dynamic rebalancing.
- [x] Storage: Direct-offset concurrent zero-merge `StorageWriter`.
- [x] Checkpoint persistence: `ResumeManager` (.wdm-state) and `DownloadStore` (downloads.json).
- [x] Rate limiting: Token-bucket `SpeedLimiter` with token-debt handling.
- [x] HTTP engine: `HttpRequestHelper` with credential-safe manual redirect handling.
- [x] Prober: HEAD with range-fallback probing, Content-Disposition RFC 5987 parser, filename sanitization, filename reservation.
- [x] Connection worker: range-request streaming, stall timeouts, exponential backoff.
- [x] Orchestrator: `DownloadEngine` supporting dynamic connection rebalancing, progress reporting, pause/resume, single-connection fallback on Range ignored, and SHA-256 checksum verification.
- [x] Test suite: 48 unit tests + 15 integration tests against custom mock HTTP servers (all passing).

### Phase 2: System IPC, CLI & NativeHost — [IN PROGRESS]
- [ ] Named pipe server and client (`WdmPipe`) implementation in `wdm-core`.
- [ ] Command line parser matching IDM flags (`/d`, `/p`, `/f`, `/n`, `/a`).
- [ ] Browser Native Messaging host registration and stdio loop (`wdm-nativehost`).
- [ ] Standalone terminal client (`wdm-cli`).
- [ ] Site grabber link extractor (`SiteGrabberService`).

### Phase 3: Tauri Desktop Application (`wdm-desktop` / UI) — [PENDING]
- [ ] Tauri v2 project initialization in workspace.
- [ ] React + TypeScript + Vite frontend.
- [ ] System tray implementation (minimize to tray, pause/resume all, notification on first hide, exit).
- [ ] Main window (download table, progress cards, segment visualizer, categories, search/filter).
- [ ] Modals: Add Download dialog, Batch Grabber dialog, Options/Settings dialog, Scheduler dialog.
- [ ] Native dark/light mode synchronization.

### Phase 4: Installer, CI & Packaging — [PENDING]
- [ ] Build scripts (`cargo tauri build`).
- [ ] Inno Setup / WiX installer script updates.
- [ ] GitHub Actions workflow updates (`build-test-release.yml` with Rust matrix).
- [ ] Browser extension verification with the Rust native host.

### Phase 5: C# Deprecation & Cleanup — [PENDING]
- [ ] Full parity verification across all tests.
- [ ] Removal of `Wdm.slnx`, `src/Wdm.*`, and `tests/Wdm.Tests`.
- [ ] Final documentation update.

