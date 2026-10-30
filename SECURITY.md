# Security policy

## Reporting a vulnerability
Please **do not** open a public issue. Use GitHub's private reporting: *Security → Report a vulnerability* on https://github.com/md-anamul-hasan/wdm/security/advisories/new.
Include the WDM version, Windows version, and steps to reproduce. Expect an acknowledgement within a few days; this is a volunteer project without an SLA.

## Supported versions
Only the latest release receives fixes.

## Security model (summary)
- Local-only: no telemetry, no update checks, no account (see `docs/PRIVACY.md`).
- Browser → native host: Chrome/Edge/Brave enforce `allowed_origins` for the extension IDs in `NativeHostManifest`; Firefox uses `allowed_extensions`.
- Native host → app: per-user named pipe (`CurrentUserOnly`), 1 MB limit, 5 s read timeout, http(s) URLs only.
- Credentials/cookies are only sent to the host they were issued for and never over an HTTPS→HTTP redirect.
- Known limitation: cookies and passwords of a download are stored unencrypted in `%APPDATA%\WDM\downloads.json` and the `.wdm-state` file (tracked as AUD-013).
- Full details: `docs/audit/SECURITY_AUDIT.md`.
