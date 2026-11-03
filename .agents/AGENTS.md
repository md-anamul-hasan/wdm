# AGENTS.md — WDM (Windows Download Manager)

Guidance for AI coding agents (Codex, etc.) working in this repository.
Review every diff before merging. Keep changes small and reviewable.

> **Note on Architecture Migration:**
> WDM is actively migrating to a **Modern Native Stack (Rust + Tauri + React/TS)**.
> See [`docs/RUST_TAURI_MIGRATION.md`](../docs/RUST_TAURI_MIGRATION.md) for full details on current status and compatibility requirements.
> During the transition, C# code is kept working until parity is reached.

## Solution layout
### Rust / Native Stack (Target)
- `crates/wdm-core` — Rust download engine: HTTP range segmentation, resume state, persistence, DPAPI crypto, zero-merge direct storage writer.
- `crates/wdm-cli` — Standalone terminal downloader (Rust).
- `crates/wdm-nativehost` — Browser Native Messaging Host (Rust).
- `crates/wdm-desktop` — Tauri v2 application core (Rust).
- `src-ui/` — React + TypeScript + Vite frontend.

### .NET 10 / WPF (Legacy / Parity Reference)
- `src/Wdm.Core` — download engine: HTTP range segmentation, resume state, persistence. No UI references.
- `src/Wdm.Ui` — WPF app (MVVM). Talks to Core through view models only.
- `src/Wdm.NativeHost` — console app speaking the browser Native Messaging protocol.
- `src/Wdm.Cli` — terminal downloader.
- `tests/Wdm.Tests` — xUnit tests.

## Hard rules
1. **NativeHost stdout is protocol-only.** It carries length-prefixed JSON frames
   (32-bit native-endian length + UTF-8 JSON) and nothing else. Never call
   `Console.WriteLine`/`println!` there. Send diagnostics to `stderr` or a log file.
2. **No blocking on async work.** No `.Result`, `.Wait()`, or `.GetAwaiter().GetResult()`
   in C#; in Rust use `tokio::task::spawn_blocking` only for blocking file syncs and never block async executors.
3. **UI thread affinity (WPF legacy).** Anything that mutates an `ObservableCollection` or other
   bound state from a background thread must marshal via `Application.Current.Dispatcher`
   (`InvokeAsync`/`BeginInvoke`).
4. **Buffers.**
   - In C#: Use `ArrayPool<byte>.Shared` for segment buffers; always return in `finally`.
   - In Rust: Use zero-copy `bytes::Bytes` or chunk buffers.
5. **Tests.** Every state mutation or new public behavior in `wdm-core` / `Wdm.Core` ships with tests.
6. **Data Compatibility.**
   - On-disk JSON formats (`settings.json`, `downloads.json`, `*.wdm-state`) must remain 100% byte/schema compatible between C# and Rust implementations.
   - Secrets must use Windows DPAPI with entropy `WDM.secret.v1` and prefix `dpapi1:`.
7. **No new dependencies** without stating why in the commit message / PR description.
8. **Never commit** secrets, signing material, or user data paths.

## Build and test

### Rust Stack
```bash
cargo build --workspace
cargo test --workspace
cargo clippy --workspace --all-targets
```

### .NET Stack
```bash
dotnet build Wdm.slnx -c Release
dotnet test tests/Wdm.Tests/Wdm.Tests.csproj --verbosity normal
```
All tests in both stacks must pass before a change is considered done. Report the real pass/fail counts.

## PR / commit conventions
- Conventional commits (`feat(ui): ...`, `test(core): ...`, `feat(rust-core): ...`).
- Note in the commit body which parts were agent-assisted and what was changed in review.
