namespace Wdm.Core.Ipc;

/// <summary>Browser extension identities the native messaging host is allowed to talk to.</summary>
public static class NativeHostManifest
{
    /// <summary>Sideloaded/unpacked copy: fixed by the "key" field in extension/manifest.json.</summary>
    public const string SideloadedChromeExtensionId = "kdjgcdbahccolkkagbckkchfpcchcfne";

    /// <summary>Chrome Web Store listing "WDM Download Manager Integration".</summary>
    public const string StoreChromeExtensionId = "flnbghmpkajibfdfhbcfhkcjkhlokfgi";

    public const string FirefoxExtensionId = "wdm@windowsdownloadmanager.com";

    /// <summary>Chromium extension IDs are 32 lowercase characters from a-p.</summary>
    public static bool IsChromeExtensionId(string? id) =>
        id is { Length: 32 } && id.All(c => c is >= 'a' and <= 'p');

    /// <summary>The <c>allowed_origins</c> list for the Chromium manifest: built-in IDs plus validated extras.</summary>
    public static string[] AllowedOrigins(IEnumerable<string> additionalIds) =>
        new[] { SideloadedChromeExtensionId, StoreChromeExtensionId }
            .Concat(additionalIds.Where(IsChromeExtensionId))
            .Distinct()
            .Select(id => $"chrome-extension://{id}/")
            .ToArray();
}

public enum FindingLevel { Ok, Warning, Failure }

public sealed record DiagnosticFinding(FindingLevel Level, string Message);

/// <summary>Checks a registered native-messaging manifest and explains, in plain words, why a browser would refuse it.</summary>
public static class NativeHostManifestInspector
{
    /// <param name="json">Contents of the Chromium manifest file.</param>
    /// <param name="fileExists">Lets the caller (and tests) decide whether the manifest's <c>path</c> exists.</param>
    public static List<DiagnosticFinding> CheckChromium(string json, Func<string, bool> fileExists)
    {
        var findings = new List<DiagnosticFinding>();
        System.Text.Json.Nodes.JsonNode? node;
        try { node = System.Text.Json.Nodes.JsonNode.Parse(json); }
        catch (System.Text.Json.JsonException ex)
        {
            findings.Add(new(FindingLevel.Failure, $"The manifest is not valid JSON ({ex.Message}). Re-run: Wdm.NativeHost.exe --register"));
            return findings;
        }

        if (node?["name"]?.ToString() != "com.wdm.download")
            findings.Add(new(FindingLevel.Failure, "Manifest name is not 'com.wdm.download'; browsers look the host up by this name."));

        if (node?["type"]?.ToString() != "stdio")
            findings.Add(new(FindingLevel.Failure, "Manifest type must be 'stdio'."));

        string? path = node?["path"]?.ToString();
        if (string.IsNullOrWhiteSpace(path))
            findings.Add(new(FindingLevel.Failure, "Manifest has no 'path' to Wdm.NativeHost.exe."));
        else if (!fileExists(path))
            findings.Add(new(FindingLevel.Failure, $"Wdm.NativeHost.exe was not found at '{path}' (WDM moved or uninstalled?). Re-run: Wdm.NativeHost.exe --register"));
        else
            findings.Add(new(FindingLevel.Ok, $"Host program found: {path}"));

        var origins = (node?["allowed_origins"] as System.Text.Json.Nodes.JsonArray)?.Select(o => o?.ToString()).ToHashSet() ?? new();
        foreach (var (label, id) in new[] { ("Chrome Web Store", NativeHostManifest.StoreChromeExtensionId), ("sideloaded", NativeHostManifest.SideloadedChromeExtensionId) })
        {
            if (origins.Contains($"chrome-extension://{id}/")) findings.Add(new(FindingLevel.Ok, $"Extension allowed: {label} ({id})"));
            else findings.Add(new(FindingLevel.Warning, $"The {label} extension ({id}) is NOT allowed. Update WDM to 1.0.2 or newer, or run: Wdm.NativeHost.exe --register --extension-id {id}"));
        }

        if (origins.Any(o => o != null && o.Contains('*')))
            findings.Add(new(FindingLevel.Warning, "Manifest uses a wildcard origin; WDM never writes one - something else edited this file."));

        return findings;
    }

    public static List<DiagnosticFinding> CheckFirefox(string json)
    {
        var findings = new List<DiagnosticFinding>();
        try
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(json);
            var ids = (node?["allowed_extensions"] as System.Text.Json.Nodes.JsonArray)?.Select(o => o?.ToString()).ToHashSet() ?? new();
            findings.Add(ids.Contains(NativeHostManifest.FirefoxExtensionId)
                ? new(FindingLevel.Ok, $"Extension allowed: Firefox ({NativeHostManifest.FirefoxExtensionId})")
                : new(FindingLevel.Failure, $"Firefox extension '{NativeHostManifest.FirefoxExtensionId}' is not allowed. Re-run: Wdm.NativeHost.exe --register"));
        }
        catch (System.Text.Json.JsonException ex)
        {
            findings.Add(new(FindingLevel.Failure, $"The Firefox manifest is not valid JSON ({ex.Message})."));
        }
        return findings;
    }
}
