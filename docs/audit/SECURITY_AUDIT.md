# WDM security audit

Scope: `src/Wdm.Core`, `Wdm.NativeHost`, `Wdm.Cli` (read), `extension/`, scripts, workflow, installer script. WPF UI read statically only.
Method: source review, then a failing test for each claim before fixing. IDs refer to ISSUE_REGISTER.md.

## Trust boundaries

| Boundary | Untrusted input | Control found |
|---|---|---|
| Web page → extension | page DOM (`<video src>`), links | Content script only sends on user click; shadow root; static innerHTML only |
| Extension → native host | JSON over stdio | Chrome enforces `allowed_origins`; host caps messages at 10 MB; only `ping`/`download`/`batch` |
| Native host → WDM | JSON over named pipe | `CurrentUserOnly` on both ends; 1 MB cap; **now** read timeout (AUD-006) and http(s) re-validation (AUD-007) |
| Server → engine | headers, body, redirects, file names | Manual redirects; creds/cookies host-bound; HTTPS→HTTP downgrade strips creds; file name sanitised; **now** Content-Range check (AUD-002) |
| Disk → engine | `.wdm-state` | **Now** validated against the file and the probe (AUD-001/004) |

## Findings

| Sev | ID | Summary | Status |
|---|---|---|---|
| High | AUD-009 | Store extension not authorized by native host (functional break, not an exposure) | Fixed in source, needs release |
| High | AUD-002 | Wrong `Content-Range` ⇒ silent data corruption | Fixed |
| High | AUD-004 | Checkpoint is trusted input (path, segments) | Fixed |
| Medium | AUD-013 | Cookies / passwords stored in plaintext | Open (approval needed) |
| Medium | AUD-019 | Bundled yt-dlp/ffmpeg fetched unverified | Open |
| Medium | AUD-020 | Workflow actions not SHA-pinned; broad token scope | Open |
| Low | AUD-008 | CR/LF header smuggling | Fixed |
| Low | AUD-006/007 | IPC DoS-by-silence, URL re-validation | Fixed |
| Info | — | `UseShellExecute=true` launch of `Wdm.Ui.exe` resolves a fixed path next to the host — no user input | OK |
| Info | — | yt-dlp is started with `ArgumentList` and `--` before the URL ⇒ no argument injection; output template uses a sanitised directory only | OK |

## Verified-OK (read + reasoned, not all runtime-tested)

- Redirect handling: credentials/cookies are only attached when the hop host equals the issuing host and the scheme was not downgraded (existing tests cover this).
- Names: Windows device names (`CON`, `NUL`…) and trailing dots/spaces handled.
- Extension permissions are minimal at install; `<all_urls>` is optional and user-granted. `webRequest` is used for observation only (no blocking permission).
- Privacy: no telemetry, update check or analytics code found in `src/` or `extension/` (grep for `HttpClient`/`fetch(` shows user-initiated downloads only). Matches `docs/PRIVACY.md`. Not verified with a network capture.

## Not covered
- Windows ACLs on the pipe and registry keys (`CurrentUserOnly` relies on Windows behaviour; untestable here).
- NuGet vulnerability scan: see FINAL_AUDIT_REPORT.md validation table.
- SSRF-style access to private addresses: by design WDM downloads any user-supplied http(s) URL; the browser handoff only forwards URLs the browser itself fetched. Documented, not restricted.
