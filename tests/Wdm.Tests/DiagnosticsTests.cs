using System.Text.Json;
using Wdm.Core.Ipc;

namespace Wdm.Tests;

/// <summary>2.0: native host diagnostics (the logic behind <c>Wdm.NativeHost.exe --diagnose</c>).</summary>
[Collection("NamedPipe")]
public class DiagnosticsTests
{
    private static string Manifest(string? path = @"C:\Program Files\WDM\Wdm.NativeHost.exe", string[]? origins = null, string name = "com.wdm.download", string type = "stdio") =>
        JsonSerializer.Serialize(new
        {
            name, description = "x", path, type,
            allowed_origins = origins ?? NativeHostManifest.AllowedOrigins(Array.Empty<string>())
        });

    private static bool Has(IEnumerable<DiagnosticFinding> f, FindingLevel level, string text) =>
        f.Any(x => x.Level == level && x.Message.Contains(text, StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void HealthyManifest_HasNoProblems()
    {
        var f = NativeHostManifestInspector.CheckChromium(Manifest(), _ => true);
        Assert.DoesNotContain(f, x => x.Level != FindingLevel.Ok);
        Assert.True(Has(f, FindingLevel.Ok, NativeHostManifest.StoreChromeExtensionId));
    }

    [Fact]
    public void ManifestWithoutTheStoreExtension_ExplainsTheFix()
    {
        // This is exactly what installs of WDM 1.0.1 and earlier look like
        var old = Manifest(origins: new[] { $"chrome-extension://{NativeHostManifest.SideloadedChromeExtensionId}/" });
        var f = NativeHostManifestInspector.CheckChromium(old, _ => true);

        Assert.True(Has(f, FindingLevel.Warning, "Chrome Web Store"));
        Assert.True(Has(f, FindingLevel.Warning, "--extension-id " + NativeHostManifest.StoreChromeExtensionId));
    }

    [Fact]
    public void MissingHostProgram_IsAFailureWithTheFix()
    {
        var f = NativeHostManifestInspector.CheckChromium(Manifest(), _ => false);
        Assert.True(Has(f, FindingLevel.Failure, "was not found"));
        Assert.True(Has(f, FindingLevel.Failure, "--register"));
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("")]
    public void BrokenJson_IsAFailure(string json)
        => Assert.True(Has(NativeHostManifestInspector.CheckChromium(json, _ => true), FindingLevel.Failure, "not valid JSON"));

    [Fact]
    public void WrongNameOrType_AreFailures()
    {
        Assert.True(Has(NativeHostManifestInspector.CheckChromium(Manifest(name: "other"), _ => true), FindingLevel.Failure, "name"));
        Assert.True(Has(NativeHostManifestInspector.CheckChromium(Manifest(type: "http"), _ => true), FindingLevel.Failure, "stdio"));
        Assert.True(Has(NativeHostManifestInspector.CheckChromium(Manifest(path: null), _ => true), FindingLevel.Failure, "no 'path'"));
    }

    [Fact]
    public void WildcardOrigin_IsFlagged()
        => Assert.True(Has(NativeHostManifestInspector.CheckChromium(Manifest(origins: new[] { "chrome-extension://*/" }), _ => true), FindingLevel.Warning, "wildcard"));

    [Fact]
    public void Firefox_ChecksTheExtensionId()
    {
        string ok = JsonSerializer.Serialize(new { allowed_extensions = new[] { NativeHostManifest.FirefoxExtensionId } });
        string bad = JsonSerializer.Serialize(new { allowed_extensions = new[] { "someone-else@example.com" } });

        Assert.DoesNotContain(NativeHostManifestInspector.CheckFirefox(ok), x => x.Level != FindingLevel.Ok);
        Assert.True(Has(NativeHostManifestInspector.CheckFirefox(bad), FindingLevel.Failure, "not allowed"));
        Assert.True(Has(NativeHostManifestInspector.CheckFirefox("garbage"), FindingLevel.Failure, "not valid JSON"));
    }

    [Fact]
    public async Task PipeProbe_ReflectsWhetherWdmIsRunning_AndDoesNotDisturbIt()
    {
        Assert.False(await WdmPipe.IsListeningAsync(300));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        int delivered = 0;
        var listener = WdmPipe.ListenAsync(_ => Interlocked.Increment(ref delivered), cts.Token);

        bool up = false;
        for (int i = 0; i < 30 && !up; i++) up = await WdmPipe.IsListeningAsync(300);
        Assert.True(up);
        Assert.True(await WdmPipe.IsListeningAsync(1000)); // probing repeatedly keeps working
        Assert.Equal(0, delivered);                        // a probe never counts as a download

        cts.Cancel();
        try { await listener; } catch (OperationCanceledException) { }
    }
}
