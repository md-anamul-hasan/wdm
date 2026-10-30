# UI/UX audit — **static only**

`Wdm.Ui` compiles (`dotnet build -p:EnableWindowsTargeting=true`, 0 warnings) but was **never run**; no screenshots were taken. Everything below comes from reading XAML/code-behind and the existing screenshots in `docs/media`. Treat as hypotheses to confirm on Windows.

| ID | Sev | Observation | Evidence | Suggested fix |
|---|---|---|---|---|
| AUD-023 | P2 | Scheduler fired only if a 15 s tick landed in the exact `HH:mm` minute; sleep/hibernate, a blocked UI thread or a DST jump skipped it. Also nothing runs while WDM is closed. | `MainViewModel.Scheduler.cs` (old `now == StartTime`) | **Fixed** (`ScheduleTrigger`, 9 tests, 30-min lateness cap). "Closed app" is by design; say so in the UI text. |
| AUD-026 | P2 | No `AutomationProperties.*` anywhere in XAML: icon-only buttons will have no accessible name for screen readers. 54 `Button` usages, 21 tooltips. | `grep AutomationProperties` = 0 | Name icon-only buttons (`AutomationProperties.Name`), mark decorative icons; verify with Accessibility Insights. |
| AUD-027 | P3 | 8 styles set `FocusVisualStyle=null`; 3 control templates draw their own `IsKeyboardFocused` trigger, the other 5 may show no keyboard focus. | `Themes/Styles.xaml` | Confirm per control with Tab-through; add a shared focus ring. |
| AUD-028 | P3 | No tray icon: closing the window ends downloads (`Closing → ViewModel.Shutdown()`); browser takeover then has to relaunch WDM for each download. | `MainWindow.xaml.cs:65`, no NotifyIcon | Tray + "minimise to tray on close" (opt-in), pause/resume all, count. No startup entry without consent. |
| AUD-029 | P3 | Shutdown-on-complete uses `shutdown /s /t 60` and a modal Cancel dialog; Cancel runs `shutdown /a` (verified in code). If WDM is killed during the 60 s the PC still powers off. | `Scheduler.cs` | Acceptable; mention in the setting's description. |
| AUD-030 | P3 | CLI exits 0 when Ctrl+C pauses a download; scripts cannot tell "paused" from "done". | `Wdm.Cli/Program.cs` | Exit code 130 on pause (behaviour change → owner decision). |
| — | OK | Duplicate URL guard (`AddDownload` reuses the existing row), dark/light theme, high-DPI via `app.manifest` (present), keyboard shortcuts (`InputBindings` ×6), min sizes (16 uses). | read | confirm on device |

Not assessed: layout at 125–200 % DPI, text scaling, contrast ratios, Windows 10 vs 11 rendering, empty/error states, every dialog flow.
