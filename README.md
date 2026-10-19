# WDM (Windows Download Manager)

A modern, high-performance clean-room replication of **Internet Download Manager (IDM)** built on **.NET 10** for Windows, engineered using reverse-engineering analysis of IDM binaries with REA.

---

## 🚀 Key Architectural Features & IDM Comparison

| Feature | IDM (Internet Download Manager) | WDM (Windows Download Manager) |
|---|---|---|
| **Segmentation Engine** | Dynamic multipart chunk splitting | Dynamic multipart chunk splitting (`DynamicSegmenter`) |
| **File Storage** | Temp files in `%APPDATA%\DwnlData` + sequential merge penalty at 99% | **Zero-Merge Direct Storage**: Concurrent random-access offset writes via `SafeFileHandle` & pre-allocation |
| **State Persistence** | Windows Registry (`Software\DownloadManager\...`) | Robust `.wdm-state` JSON checkpoints + ETag/Last-Modified validation |
| **Network Engine** | Raw Winsock + OpenSSL | High-efficiency `SocketsHttpHandler` with connection pooling & HTTP/1.1-2 range streaming |
| **Bandwidth Limiter** | Speed Limiter | High-precision Token Bucket Rate Limiter (`SpeedLimiter`) |
| **Browser Integration** | `IDMMsgHost.exe` stdio Native Messaging | `Wdm.NativeHost` stdio Native Messaging + Chrome/Edge/Firefox MV3 Extension |
| **Desktop Interface** | Legacy Win32 GDI | Modern WPF Dark Interface with real-time multi-connection segment visualizer |

---

## 📁 Solution Structure

```
f:\04_Dev\wdm\
├── src\
│   ├── Wdm.Core\                      # Core multi-connection download engine
│   │   ├── Engine\
│   │   │   ├── DownloadEngine.cs      # Master download orchestrator
│   │   │   ├── DynamicSegmenter.cs    # IDM dynamic splitting algorithm
│   │   │   ├── HttpConnectionWorker.cs# Parallel range streaming worker
│   │   │   ├── NetworkProber.cs       # HEAD/Range probe & RFC 5987/6266 filename parser
│   │   │   ├── ResumeManager.cs       # Checkpoint state persistence & recovery
│   │   │   ├── SpeedLimiter.cs        # Token-bucket bandwidth throttling
│   │   │   └── StorageWriter.cs       # Zero-merge direct-offset file writer
│   │   └── Models\                    # Data models (Metadata, Progress, Segments, Status)
│   ├── Wdm.Cli\                       # Interactive terminal application with segment progress bars
│   ├── Wdm.NativeHost\                # Chrome / Edge / Firefox Native Messaging Host
│   └── Wdm.Ui\                        # Modern Windows desktop GUI with live segment visualizer
├── extension\                         # Chrome / Edge / Firefox Extension (Manifest V3)
│   ├── manifest.json
│   ├── background.js
│   ├── content.js                     # Video / Audio sniffer & floating download button
│   ├── popup.html
│   └── popup.js
├── tests\
│   └── Wdm.Tests\                     # Comprehensive xUnit test suite
└── Wdm.sln
```

---

## 🛠️ Usage

### 1. Running the CLI
```powershell
# Basic download
dotnet run --project src/Wdm.Cli/Wdm.Cli.csproj -- https://example.com/file.iso

# Specifying connections, output directory, and speed limit (in KB/s)
dotnet run --project src/Wdm.Cli/Wdm.Cli.csproj -- https://example.com/file.iso -c 16 -o D:\Downloads -l 5120
```

### 2. Launching the Windows GUI (WPF)
```powershell
dotnet run --project src/Wdm.Ui/Wdm.Ui.csproj
```

### 3. Registering Browser Native Messaging Host
```powershell
dotnet run --project src/Wdm.NativeHost/Wdm.NativeHost.csproj -- --register
```

### 4. Running the Tests
```powershell
dotnet test
```
