namespace Wdm.Core.Models;

/// <summary>
/// Per-download HTTP context captured from the browser (or CLI): referer, cookies, user agent
/// and optional basic-auth credentials. Sent with every request of the download.
/// </summary>
public class DownloadRequestOptions
{
    public string? Referer { get; set; }
    public string? Cookies { get; set; }

    /// <summary>Host the cookies were captured for. Defaults to the download's own host.</summary>
    public string? CookieHost { get; set; }
    public string? UserAgent { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }

    /// <summary>Caller-chosen file name; overrides the name derived from the server response.</summary>
    public string? FileName { get; set; }
}
