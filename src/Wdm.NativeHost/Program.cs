using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Win32;
using Wdm.Core.Ipc;
using Wdm.Core.Models;

namespace Wdm.NativeHost;

/// <summary>
/// WDM Native Messaging Host for Chrome, Edge, Brave and Firefox.
/// Talks to the WDM browser extension over stdio (4-byte length prefix + JSON) and forwards
/// downloads to the running WDM window through its named pipe, starting WDM first if needed.
/// This is the same architecture as IDM's IDMMsgHost.exe, which relays to IDMan.exe.
/// </summary>
public class Program
{
    private const string HostName = "com.wdm.download";

    // Fixed by the "key" field in extension/manifest.json
    private const string ChromeExtensionId = "kdjgcdbahccolkkagbckkchfpcchcfne";
    private const string FirefoxExtensionId = "wdm@windowsdownloadmanager.com";

    private static readonly string Version = typeof(Program).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "1.0.0";
    private const int MaxIncomingMessageBytes = 10 * 1024 * 1024;

    private static readonly string[] ChromiumRegistryRoots =
    {
        @"Software\Google\Chrome\NativeMessagingHosts",
        @"Software\Microsoft\Edge\NativeMessagingHosts",
        @"Software\BraveSoftware\Brave-Browser\NativeMessagingHosts",
        @"Software\Chromium\NativeMessagingHosts",
    };

    private const string FirefoxRegistryRoot = @"Software\Mozilla\NativeMessagingHosts";

    public static async Task<int> Main(string[] args)
    {
        if (args.Length > 0)
        {
            switch (args[0].ToLowerInvariant())
            {
                case "--register" or "-r":
                    return Register();
                case "--unregister" or "-u":
                    return Unregister();
            }
        }

        await RunHostLoopAsync();
        return 0;
    }

    private static async Task RunHostLoopAsync()
    {
        using var stdin = Console.OpenStandardInput();
        using var stdout = Console.OpenStandardOutput();

        byte[] lengthBuffer = new byte[4];

        while (true)
        {
            if (await ReadExactAsync(stdin, lengthBuffer, 4) < 4) break; // browser closed the port

            int messageLength = BitConverter.ToInt32(lengthBuffer, 0);
            if (messageLength <= 0 || messageLength > MaxIncomingMessageBytes) break;

            byte[] messageBuffer = new byte[messageLength];
            if (await ReadExactAsync(stdin, messageBuffer, messageLength) < messageLength) break;

            string responseJson = await HandleMessageAsync(Encoding.UTF8.GetString(messageBuffer));

            byte[] responseBytes = Encoding.UTF8.GetBytes(responseJson);
            await stdout.WriteAsync(BitConverter.GetBytes(responseBytes.Length));
            await stdout.WriteAsync(responseBytes);
            await stdout.FlushAsync();
        }
    }

    private static async Task<string> HandleMessageAsync(string jsonString)
    {
        try
        {
            var node = JsonNode.Parse(jsonString);
            string action = node?["action"]?.ToString() ?? "";

            switch (action.ToLowerInvariant())
            {
                case "ping":
                    return Reply(new { status = "ok", message = "pong", version = Version });

                case "download":
                {
                    var request = BuildRequest(node, node?["url"]?.ToString());
                    if (request.IsEmpty)
                    {
                        return Reply(new { status = "error", message = "Missing or invalid http(s) URL" });
                    }
                    return await DeliverAsync(request);
                }

                case "batch":
                {
                    var urls = node?["urls"]?.AsArray().Select(u => u?.ToString()).ToList() ?? new();
                    var request = BuildRequest(node, null);
                    request.Urls = urls.Where(ExternalDownloadRequest.IsHttpUrl).Select(u => u!).Distinct().ToList();
                    if (request.IsEmpty)
                    {
                        return Reply(new { status = "error", message = "No valid http(s) URLs" });
                    }
                    request.CookieHost = request.Cookies == null ? null : new Uri(request.Urls[0]).Host;
                    return await DeliverAsync(request);
                }

                default:
                    return Reply(new { status = "unknown_action", action });
            }
        }
        catch (Exception ex)
        {
            return Reply(new { status = "error", message = ex.Message });
        }
    }

    private static ExternalDownloadRequest BuildRequest(JsonNode? node, string? url)
    {
        var request = new ExternalDownloadRequest
        {
            Referer = NullIfEmpty(node?["referer"]?.ToString()),
            Cookies = NullIfEmpty(node?["cookies"]?.ToString()),
            UserAgent = NullIfEmpty(node?["userAgent"]?.ToString()),
            FileName = NullIfEmpty(node?["fileName"]?.ToString()),
        };

        if (ExternalDownloadRequest.IsHttpUrl(url))
        {
            request.Urls.Add(url!);
            if (request.Cookies != null) request.CookieHost = new Uri(url!).Host;
        }

        return request;
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static async Task<string> DeliverAsync(ExternalDownloadRequest request)
    {
        if (await WdmPipe.TrySendAsync(request))
        {
            return Reply(new { status = "ok", message = "Download sent to WDM" });
        }

        // WDM isn't running: start it, then wait for its pipe to come up
        if (!LaunchWdm())
        {
            return Reply(new { status = "error", message = "WDM application (Wdm.Ui.exe) was not found next to the native host" });
        }

        for (int attempt = 0; attempt < 40; attempt++)
        {
            await Task.Delay(250);
            if (await WdmPipe.TrySendAsync(request, 500))
            {
                return Reply(new { status = "ok", message = "WDM started and download sent" });
            }
        }

        return Reply(new { status = "error", message = "WDM did not respond after being started" });
    }

    private static bool LaunchWdm()
    {
        try
        {
            string? uiPath = FindWdmUi();
            if (uiPath == null) return false;

            Process.Start(new ProcessStartInfo { FileName = uiPath, UseShellExecute = true });
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    private static string? FindWdmUi()
    {
        string baseDir = AppContext.BaseDirectory;
        string[] candidates =
        {
            Path.Combine(baseDir, "Wdm.Ui.exe"),
            // Local development layout: src/Wdm.NativeHost/bin/<cfg>/<tfm>/ -> src/Wdm.Ui/bin/<cfg>/<tfm>/
            Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\..\Wdm.Ui\bin\Debug\net10.0-windows\Wdm.Ui.exe")),
            Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\..\Wdm.Ui\bin\Release\net10.0-windows\Wdm.Ui.exe")),
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static string ManifestDirectory => Path.Combine(AppSettings.DataDirectory, "NativeMessaging");

    private static int Register()
    {
        Console.WriteLine("[WDM] Registering native messaging host for Chrome, Edge, Brave and Firefox...");

        string exePath = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Wdm.NativeHost.exe");
        Directory.CreateDirectory(ManifestDirectory);

        string chromiumManifest = Path.Combine(ManifestDirectory, $"{HostName}.json");
        string firefoxManifest = Path.Combine(ManifestDirectory, $"{HostName}.mozilla.json");
        var indented = new JsonSerializerOptions { WriteIndented = true };

        File.WriteAllText(chromiumManifest, JsonSerializer.Serialize(new
        {
            name = HostName,
            description = "Windows Download Manager browser integration",
            path = exePath,
            type = "stdio",
            allowed_origins = new[] { $"chrome-extension://{ChromeExtensionId}/" }
        }, indented));

        File.WriteAllText(firefoxManifest, JsonSerializer.Serialize(new
        {
            name = HostName,
            description = "Windows Download Manager browser integration",
            path = exePath,
            type = "stdio",
            allowed_extensions = new[] { FirefoxExtensionId }
        }, indented));

        try
        {
            foreach (var root in ChromiumRegistryRoots)
            {
                using var key = Registry.CurrentUser.CreateSubKey($@"{root}\{HostName}");
                key.SetValue("", chromiumManifest);
            }

            using (var key = Registry.CurrentUser.CreateSubKey($@"{FirefoxRegistryRoot}\{HostName}"))
            {
                key.SetValue("", firefoxManifest);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            Console.Error.WriteLine($"[WDM] Registry write failed: {ex.Message}");
            return 1;
        }

        Console.WriteLine($"[WDM] Registered '{HostName}'.");
        Console.WriteLine($"      Host:    {exePath}");
        Console.WriteLine($"      Chrome:  {chromiumManifest}");
        Console.WriteLine($"      Firefox: {firefoxManifest}");
        return 0;
    }

    private static int Unregister()
    {
        foreach (var root in ChromiumRegistryRoots.Append(FirefoxRegistryRoot))
        {
            try { Registry.CurrentUser.DeleteSubKeyTree($@"{root}\{HostName}", throwOnMissingSubKey: false); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { }
        }

        try { Directory.Delete(ManifestDirectory, recursive: true); } catch (DirectoryNotFoundException) { }
        Console.WriteLine($"[WDM] Unregistered '{HostName}'.");
        return 0;
    }

    private static string Reply(object payload) => JsonSerializer.Serialize(payload);

    private static async Task<int> ReadExactAsync(Stream stream, byte[] buffer, int count)
    {
        int totalRead = 0;
        while (totalRead < count)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(totalRead, count - totalRead));
            if (read == 0) break;
            totalRead += read;
        }
        return totalRead;
    }
}
