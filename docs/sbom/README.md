# SBOM

CycloneDX 1.5, regenerate with `scripts/make-sbom.ps1` (needs `cargo install cargo-cyclonedx` and Node).

| File | Covers |
|---|---|
| `wdm-core/cli/nativehost/desktop.cdx.json` | Rust crates per workspace member. Resolved from `Cargo.lock` for all targets, so Linux-only crates appear although the Windows build does not ship them. |
| `wdm-ui.cdx.json` | UI production npm dependencies (bundled into the window's JavaScript). |
| `wdm-bundled-tools.cdx.json` | yt-dlp and ffmpeg with pinned URLs and SHA-256 from `scripts/tools.lock.json`. |

Limits: it lists components and declared licences. It is not a legal opinion, and the WebView2 Runtime (installed separately by the user) is not included.
