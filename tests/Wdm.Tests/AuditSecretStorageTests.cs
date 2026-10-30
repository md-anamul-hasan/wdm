using System.Text;
using System.Text.Json;
using Wdm.Core.Engine;
using Wdm.Core.Models;

namespace Wdm.Tests;

[CollectionDefinition("SecretCipher", DisableParallelization = true)]
public class SecretCipherCollection { }

/// <summary>Reversible stand-in for DPAPI so the storage wiring is testable on any OS (the DPAPI call itself is Windows-only).</summary>
internal sealed class FakeCipher : ISecretCipher
{
    public bool FailOnProtect { get; set; }
    public byte[] Protect(byte[] plain)
    {
        if (FailOnProtect) throw new System.Security.Cryptography.CryptographicException("no key");
        return plain.Select(b => (byte)(b ^ 0x5A)).Reverse().ToArray();
    }
    public byte[] Unprotect(byte[] protectedBytes) => protectedBytes.Reverse().Select(b => (byte)(b ^ 0x5A)).ToArray();
}

/// <summary>AUD-013: cookies and passwords are never written in clear, and not kept for finished downloads.</summary>
[Collection("SecretCipher")]
public class AuditSecretStorageTests : IDisposable
{
    private const string Cookie = "session=SUPER-SECRET-COOKIE";
    private const string Pass = "hunter2-PASSWORD";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"wdm_secret_{Guid.NewGuid():N}");
    private readonly ISecretCipher? _previous = SecretProtector.Cipher;
    private readonly FakeCipher _cipher = new();

    public AuditSecretStorageTests()
    {
        Directory.CreateDirectory(_dir);
        SecretProtector.Cipher = _cipher;
    }

    public void Dispose()
    {
        SecretProtector.Cipher = _previous;
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private static DownloadRecord Record(string status) => new()
    {
        Url = "https://example.com/f.bin", FileName = "f.bin", Status = status,
        Options = new DownloadRequestOptions { Cookies = Cookie, Username = "alice", Password = Pass, Referer = "https://example.com/" }
    };

    [Fact]
    public void DownloadList_NeverContainsSecretsInClear_AndRoundTrips()
    {
        string path = Path.Combine(_dir, "downloads.json");
        var store = new DownloadStore(path);
        store.Save(new[] { Record("Paused") });

        string text = File.ReadAllText(path);
        Assert.DoesNotContain("SUPER-SECRET", text);
        Assert.DoesNotContain(Pass, text);
        Assert.DoesNotContain("alice", text);
        Assert.Contains(SecretProtector.Prefix, text);

        var loaded = store.Load().Single().Options!;
        Assert.Equal(Cookie, loaded.Cookies);
        Assert.Equal(Pass, loaded.Password);
        Assert.Equal("alice", loaded.Username);
    }

    [Fact]
    public void CompletedDownloads_DropTheirSecrets_ButKeepNonSecretContext()
    {
        string path = Path.Combine(_dir, "downloads.json");
        var done = Record("Completed");
        new DownloadStore(path).Save(new[] { done });

        var loaded = new DownloadStore(path).Load().Single().Options!;
        Assert.Null(loaded.Cookies);
        Assert.Null(loaded.Password);
        Assert.Null(loaded.Username);
        Assert.Equal("https://example.com/", loaded.Referer);
        Assert.Equal(Cookie, done.Options!.Cookies); // the in-memory record is untouched
    }

    [Fact]
    public void LegacyPlaintextList_IsReadAndMigratedOnLoad()
    {
        string path = Path.Combine(_dir, "downloads.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new[]
        {
            new { Url = "https://example.com/a", Status = "Paused", Options = new { Cookies = Cookie, Password = Pass, Username = "alice" } }
        }));

        var loaded = new DownloadStore(path).Load().Single().Options!;
        Assert.Equal(Cookie, loaded.Cookies); // still usable

        string rewritten = File.ReadAllText(path);
        Assert.DoesNotContain("SUPER-SECRET", rewritten); // and no longer in clear on disk
        Assert.Equal(Cookie, new DownloadStore(path).Load().Single().Options!.Cookies);
    }

    [Fact]
    public async Task Checkpoint_NeverContainsSecretsInClear()
    {
        var meta = new DownloadMetadata
        {
            Url = "https://example.com/f", TargetFilePath = Path.Combine(_dir, "f.bin"),
            Options = new DownloadRequestOptions { Cookies = Cookie, Password = Pass }
        };
        await ResumeManager.SaveCheckpointAsync(meta);

        string text = await File.ReadAllTextAsync(ResumeManager.GetCheckpointPath(meta.TargetFilePath));
        Assert.DoesNotContain("SUPER-SECRET", text);
        Assert.DoesNotContain(Pass, text);

        var back = await ResumeManager.LoadCheckpointAsync(meta.TargetFilePath);
        Assert.Equal(Cookie, back!.Options!.Cookies);
    }

    [Fact]
    public void WhenEncryptionFails_TheSecretIsDroppedNotWrittenInClear()
    {
        _cipher.FailOnProtect = true;
        string path = Path.Combine(_dir, "downloads.json");
        new DownloadStore(path).Save(new[] { Record("Paused") });

        Assert.DoesNotContain("SUPER-SECRET", File.ReadAllText(path));
        Assert.Null(new DownloadStore(path).Load().Single().Options!.Cookies);
    }

    [Fact]
    public void UndecryptableValue_LoadsAsNoSecret()
    {
        string path = Path.Combine(_dir, "downloads.json");
        File.WriteAllText(path, "[{\"Url\":\"https://e/x\",\"Status\":\"Paused\",\"Options\":{\"Cookies\":\"dpapi1:!!!not-base64!!!\"}}]");
        Assert.Null(new DownloadStore(path).Load().Single().Options!.Cookies);
    }

    [Fact]
    public void ProtectedValues_AreNotDeterministicallyPlain()
    {
        string p = SecretProtector.Protect("abc")!;
        Assert.StartsWith(SecretProtector.Prefix, p);
        Assert.Equal("abc", SecretProtector.Unprotect(p));
        Assert.Equal("legacy", SecretProtector.Unprotect("legacy"));
        Assert.Null(SecretProtector.Protect(null));
        Assert.Equal("", SecretProtector.Protect(""));
    }
}
