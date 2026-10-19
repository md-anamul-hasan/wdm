using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Win32;

namespace Wdm.NativeHost;

/// <summary>
/// Native Messaging Host replicating IDMMsgHost.exe for Chrome, Edge, and Firefox.
/// Communicates with web extensions via standard I/O (4-byte length prefix + JSON).
/// </summary>
public class Program
{
    private const string HostName = "com.wdm.download";

    public static async Task Main(string[] args)
    {
        if (args.Length > 0 && (args[0] == "--register" || args[0] == "-r"))
        {
            RegisterHost();
            return;
        }

        // Run stdio native messaging loop
        await RunHostLoopAsync();
    }

    private static async Task RunHostLoopAsync()
    {
        using var stdin = Console.OpenStandardInput();
        using var stdout = Console.OpenStandardOutput();

        byte[] lengthBuffer = new byte[4];

        while (true)
        {
            // Read 4-byte length
            int read = await ReadExactAsync(stdin, lengthBuffer, 0, 4);
            if (read < 4) break; // Extension disconnected or stdin closed

            int messageLength = BitConverter.ToInt32(lengthBuffer, 0);
            if (messageLength <= 0 || messageLength > 10 * 1024 * 1024) break; // Sanity check

            byte[] messageBuffer = new byte[messageLength];
            read = await ReadExactAsync(stdin, messageBuffer, 0, messageLength);
            if (read < messageLength) break;

            string jsonString = Encoding.UTF8.GetString(messageBuffer);
            var responseJson = HandleMessage(jsonString);

            // Write response
            byte[] responseBytes = Encoding.UTF8.GetBytes(responseJson);
            byte[] responseLengthBytes = BitConverter.GetBytes(responseBytes.Length);

            await stdout.WriteAsync(responseLengthBytes);
            await stdout.WriteAsync(responseBytes);
            await stdout.FlushAsync();
        }
    }

    private static string HandleMessage(string jsonString)
    {
        try
        {
            var node = JsonNode.Parse(jsonString);
            string action = node?["action"]?.ToString() ?? "";

            switch (action.ToLowerInvariant())
            {
                case "ping":
                    return JsonSerializer.Serialize(new { status = "ok", message = "pong", version = "1.0.0" });

                case "download":
                    string url = node?["url"]?.ToString() ?? "";
                    string? referer = node?["referer"]?.ToString();
                    string? cookies = node?["cookies"]?.ToString();

                    if (!string.IsNullOrWhiteSpace(url))
                    {
                        LaunchWdmDownload(url, referer, cookies);
                        return JsonSerializer.Serialize(new { status = "ok", message = "Download queued in WDM", url });
                    }
                    return JsonSerializer.Serialize(new { status = "error", message = "Missing URL" });

                case "sniff":
                    // Audio / Video stream sniffer callback
                    string mediaUrl = node?["mediaUrl"]?.ToString() ?? "";
                    string title = node?["title"]?.ToString() ?? "Media";
                    return JsonSerializer.Serialize(new { status = "ok", message = "Media URL captured", mediaUrl, title });

                default:
                    return JsonSerializer.Serialize(new { status = "unknown_action", action });
            }
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { status = "error", message = ex.Message });
        }
    }

    private static void LaunchWdmDownload(string url, string? referer, string? cookies)
    {
        try
        {
            // Find Wdm.Cli or Wdm.Ui executable
            string currentExeDir = AppContext.BaseDirectory;
            string cliPath = Path.Combine(currentExeDir, "Wdm.Cli.exe");
            
            if (File.Exists(cliPath))
            {
                var psi = new ProcessStartInfo
                {
                    FileName = cliPath,
                    Arguments = $"\"{url}\"",
                    UseShellExecute = true
                };
                Process.Start(psi);
            }
        }
        catch { }
    }

    private static void RegisterHost()
    {
        Console.WriteLine("[WDM] Registering Native Messaging Host for Chrome, Edge, and Firefox...");

        string exePath = Process.GetCurrentProcess().MainModule?.FileName 
            ?? Path.Combine(AppContext.BaseDirectory, "Wdm.NativeHost.exe");
        string manifestDir = AppContext.BaseDirectory;
        string chromeManifestPath = Path.Combine(manifestDir, $"{HostName}.json");
        string firefoxManifestPath = Path.Combine(manifestDir, $"{HostName}.mozilla.json");

        // Chrome & Edge Manifest
        var chromeManifest = new
        {
            name = HostName,
            description = "Windows Download Manager Extension Helper",
            path = exePath,
            type = "stdio",
            allowed_origins = new[]
            {
                "chrome-extension://*",
                "chrome-extension://wdm-extension-id/"
            }
        };

        // Firefox Manifest
        var firefoxManifest = new
        {
            name = HostName,
            description = "Windows Download Manager Extension Helper",
            path = exePath,
            type = "stdio",
            allowed_extensions = new[]
            {
                "wdm@windowsdownloadmanager.com"
            }
        };

        File.WriteAllText(chromeManifestPath, JsonSerializer.Serialize(chromeManifest, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(firefoxManifestPath, JsonSerializer.Serialize(firefoxManifest, new JsonSerializerOptions { WriteIndented = true }));

        try
        {
            // Registry keys
            using (var key = Registry.CurrentUser.CreateSubKey($@"Software\Google\Chrome\NativeMessagingHosts\{HostName}"))
            {
                key?.SetValue("", chromeManifestPath);
            }

            using (var key = Registry.CurrentUser.CreateSubKey($@"Software\Microsoft\Edge\NativeMessagingHosts\{HostName}"))
            {
                key?.SetValue("", chromeManifestPath);
            }

            using (var key = Registry.CurrentUser.CreateSubKey($@"Software\Mozilla\NativeMessagingHosts\{HostName}"))
            {
                key?.SetValue("", firefoxManifestPath);
            }

            Console.WriteLine($"[WDM] Successfully registered {HostName} in Windows Registry!");
            Console.WriteLine($"Chrome/Edge manifest: {chromeManifestPath}");
            Console.WriteLine($"Firefox manifest: {firefoxManifestPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WDM Warning] Registry write error: {ex.Message}");
        }
    }

    private static async Task<int> ReadExactAsync(Stream stream, byte[] buffer, int offset, int count)
    {
        int totalRead = 0;
        while (totalRead < count)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(offset + totalRead, count - totalRead));
            if (read == 0) break;
            totalRead += read;
        }
        return totalRead;
    }
}
