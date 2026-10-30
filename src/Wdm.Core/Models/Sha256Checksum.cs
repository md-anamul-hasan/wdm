using System.Security.Cryptography;

namespace Wdm.Core.Models;

/// <summary>Raised when a finished download does not match the SHA-256 the user asked for.</summary>
public sealed class ChecksumMismatchException(string expected, string actual, string keptFile)
    : Exception($"SHA-256 mismatch: expected {expected}, got {actual}. The downloaded data was kept as '{Path.GetFileName(keptFile)}'; restart the download or correct the checksum.")
{
    public string Expected { get; } = expected;
    public string Actual { get; } = actual;
}

/// <summary>Parsing and computing SHA-256 checksums as users write them (hex, optional "sha256:" prefix, any case, spaces).</summary>
public static class Sha256Checksum
{
    /// <summary>Normalises a user-supplied checksum to 64 lowercase hex characters; false if it is not one.</summary>
    public static bool TryNormalize(string? input, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(input)) return false;

        var text = input.Trim();
        if (text.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) text = text[7..];
        text = new string(text.Where(c => !char.IsWhiteSpace(c)).ToArray());

        if (text.Length != 64 || !text.All(Uri.IsHexDigit)) return false;

        normalized = text.ToLowerInvariant();
        return true;
    }

    public static async Task<string> ComputeFileAsync(string path, CancellationToken ct = default)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).ToLowerInvariant();
    }
}
