using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Runtime.Versioning;
using System.Text.Json.Serialization;

namespace Wdm.Core.Models;

/// <summary>Reversible encryption for secrets stored on disk. Replaceable so tests can run on any OS.</summary>
public interface ISecretCipher
{
    byte[] Protect(byte[] plain);
    byte[] Unprotect(byte[] protectedBytes);
}

/// <summary>Windows DPAPI, scope CurrentUser: only the same Windows account can decrypt.</summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiCipher : ISecretCipher
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("WDM.secret.v1");

    public byte[] Protect(byte[] plain) => ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
    public byte[] Unprotect(byte[] protectedBytes) => ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
}

/// <summary>
/// Cookies and passwords of a download are written to <c>downloads.json</c> and <c>*.wdm-state</c>. They are stored as
/// <c>dpapi1:&lt;base64&gt;</c>. Values without the prefix are legacy plaintext and are still read (and rewritten protected
/// on the next save). If encryption fails the secret is dropped rather than written in clear.
/// </summary>
public static class SecretProtector
{
    public const string Prefix = "dpapi1:";

    /// <summary>DPAPI on Windows; null elsewhere (WDM is Windows-only, other platforms keep plaintext for development/tests).</summary>
    public static ISecretCipher? Cipher { get; set; } = OperatingSystem.IsWindows() ? new DpapiCipher() : null;

    public static string? Protect(string? plain)
    {
        if (string.IsNullOrEmpty(plain)) return plain;
        if (Cipher == null) return plain;

        try
        {
            return Prefix + Convert.ToBase64String(Cipher.Protect(Encoding.UTF8.GetBytes(plain)));
        }
        catch (Exception ex) when (ex is CryptographicException or PlatformNotSupportedException or InvalidOperationException)
        {
            return null;
        }
    }

    public static string? Unprotect(string? stored)
    {
        if (string.IsNullOrEmpty(stored) || !stored.StartsWith(Prefix, StringComparison.Ordinal)) return stored; // legacy plaintext

        try
        {
            if (Cipher == null) return null;
            return Encoding.UTF8.GetString(Cipher.Unprotect(Convert.FromBase64String(stored[Prefix.Length..])));
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or PlatformNotSupportedException)
        {
            return null; // other user/machine, or damaged: treat as "no secret"
        }
    }

    /// <summary>True if <paramref name="stored"/> is a non-empty secret that is not in protected form.</summary>
    public static bool IsLegacyPlaintext(string? stored) =>
        !string.IsNullOrEmpty(stored) && !stored.StartsWith(Prefix, StringComparison.Ordinal);
}

/// <summary>System.Text.Json converter that protects a string property on write and unprotects it on read.</summary>
public sealed class ProtectedStringConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        SecretProtector.Unprotect(reader.GetString());

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        var protectedValue = SecretProtector.Protect(value);
        if (protectedValue == null) writer.WriteNullValue();
        else writer.WriteStringValue(protectedValue);
    }
}
