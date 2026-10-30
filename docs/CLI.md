# WDM command line

```
wdm <url> [-o <directory>] [-c <connections>] [-l <KB/s>] [--sha256 <hex>]
```

| Option | Meaning |
|---|---|
| `-o`, `--output` | Folder to save into (default: your Downloads folder) |
| `-c`, `--connections` | 1–32 parallel connections (default 8) |
| `-l`, `--limit` | Speed limit in KB/s (default: unlimited) |
| `--sha256` | Expected SHA-256 of the file: 64 hex characters, any case; spaces and a `sha256:` prefix are accepted |

While downloading, the data is written to `<name>.wdm-part` next to a `<name>.wdm-state` resume file. When the file is complete (and, with `--sha256`, matches) it is renamed to `<name>`.

## Exit codes
| Code | Meaning |
|---|---|
| 0 | Completed |
| 1 | Error: not an http(s) URL, malformed `--sha256`, HTTP or I/O error |
| 2 | Completed but the SHA-256 does not match. Nothing is published; the data stays in `<name>.wdm-part` |
| 130 | Paused with Ctrl+C. Run the same command again to resume |

## Examples
```
wdm https://example.com/big.iso -c 16 -o D:\Downloads
wdm https://example.com/big.iso --sha256 e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855
```

## Launching the desktop app
`Wdm.Ui.exe /d <url> [/p <folder>] [/f <name>] [/sha256 <hex>] [/n] [/a]` hands a download to the running WDM window (`/n` start without asking, `/a` add to the queue without starting).
