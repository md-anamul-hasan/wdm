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
