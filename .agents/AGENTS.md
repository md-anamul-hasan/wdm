# AGENTS.md — WDM (Windows Download Manager)

Guidance for AI coding agents (Codex, etc.) working in this repository.
Review every diff before merging. Keep changes small and reviewable.

## Solution layout
- `WDM.Core` — download engine: HTTP range segmentation, resume state, persistence. No UI references.
- `WDM.UI` — WPF app (MVVM). Talks to Core through view models only.
- `WDM.NativeHost` — console app speaking the browser Native Messaging protocol.
- `tests/WDM.Core.Tests` — xUnit tests.

## Hard rules
1. **NativeHost stdout is protocol-only.** It carries length-prefixed JSON frames
   (32-bit native-endian length + UTF-8 JSON) and nothing else. Never call
   `Console.WriteLine`/`Console.Write` there. Send diagnostics to `stderr` or a log file.
2. **No blocking on async work.** No `.Result`, `.Wait()`, or `.GetAwaiter().GetResult()`
   on download or UI paths. Use `async`/`await` and pass `CancellationToken`.
3. **UI thread affinity.** Anything that mutates an `ObservableCollection` or other
   bound state from a background thread must marshal via `Application.Current.Dispatcher`
   (`InvokeAsync`/`BeginInvoke`). WPF has no `DispatcherQueue`; that is WinUI.
4. **Buffers.** Use `ArrayPool<byte>.Shared` for segment buffers; always return in `finally`.
5. **Tests.** Every state mutation or new public behavior in `WDM.Core` ships with an xUnit test.
   UI-only wiring that cannot be unit-tested must keep its logic in a testable class.
6. **No new dependencies** without stating why in the PR description.
7. **Never commit** secrets, signing material, or user data paths.

## Build and test
```bash
dotnet build WDM.sln -c Release
dotnet test tests/WDM.Core.Tests/WDM.Core.Tests.csproj --verbosity normal
```
All tests must pass before a change is considered done. Report the real pass/fail counts.

## Task 1 — System tray lifecycle (known limitation)
- Minimize/close-to-tray with a balloon notification on first hide.
- Tray menu: Open WDM, Pause All, Resume All, Exit.
- Exit must persist in-progress download state so resume works on next launch.
- Put tray state logic (visible/hidden, pending exit, pause/resume intent) in a plain
  class in `WDM.Core` or a UI-agnostic helper so it can be unit-tested.
- WPF has no built-in tray icon: use `System.Windows.Forms.NotifyIcon` (enable
  `UseWindowsForms`) or the `Hardcodet.NotifyIcon.Wpf` package, and dispose it on exit.
- Do not touch `WDM.NativeHost` or the native messaging protocol.

## PR / commit conventions
- Conventional commits (`feat(ui): ...`, `test(core): ...`).
- Note in the commit body which parts were agent-assisted and what was changed in review.