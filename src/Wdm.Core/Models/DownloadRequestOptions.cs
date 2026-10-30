using System.Text.Json.Serialization;

namespace Wdm.Core.Models;

/// <summary>
/// Per-download HTTP context captured from the browser (or CLI): referer, cookies, user agent
/// and optional basic-auth credentials. Sent with every request of the download.
/// </summary>
public class DownloadRequestOptions
{
    public string? Referer { get; set; }
    [JsonConverter(typeof(ProtectedStringConverter))]
    public string? Cookies { get; set; }

    /// <summary>Host the cookies were captured for. Defaults to the download's own host.</summary>
    public string? CookieHost { get; set; }
    public string? UserAgent { get; set; }
    [JsonConverter(typeof(ProtectedStringConverter))]
    public string? Username { get; set; }
    [JsonConverter(typeof(ProtectedStringConverter))]
    public string? Password { get; set; }

    /// <summary>Caller-chosen file name; overrides the name derived from the server response.</summary>
    public string? FileName { get; set; }

    /// <summary>Copy without cookies and credentials (what is kept once a download has finished).</summary>
    public DownloadRequestOptions WithoutSecrets() => new()
    {
        Referer = Referer, CookieHost = CookieHost, UserAgent = UserAgent, FileName = FileName
    };

    [JsonIgnore]
    public bool HasSecrets => !string.IsNullOrEmpty(Cookies) || !string.IsNullOrEmpty(Username) || !string.IsNullOrEmpty(Password);
}
