using System.Text.Json.Serialization;
using Wdm.Core.Models;

namespace Wdm.Core.Ipc;

/// <summary>
/// A download handed to the running WDM instance by another process
/// (native messaging host, command line, shell integration).
/// </summary>
public class ExternalDownloadRequest
{
    /// <summary>One or more URLs. More than one opens the batch dialog unless the request is silent or queue-only.</summary>
    public List<string> Urls { get; set; } = new();

    public string? Referer { get; set; }
    public string? Cookies { get; set; }

    /// <summary>Host the cookies belong to; they are never sent to any other host.</summary>
    public string? CookieHost { get; set; }
    public string? UserAgent { get; set; }
    public string? FileName { get; set; }
    public string? DestinationDirectory { get; set; }

    /// <summary>Start immediately without showing the "add download" dialog.</summary>
    public bool Silent { get; set; }

    /// <summary>Add to the queue without starting (IDM's /a switch).</summary>
    public bool QueueOnly { get; set; }

    /// <summary>Only bring the running window to the foreground.</summary>
    public bool ActivateOnly { get; set; }

    [JsonIgnore]
    public bool IsEmpty => Urls.Count == 0;

    public DownloadRequestOptions ToOptions() => new()
    {
        Referer = Referer,
        Cookies = Cookies,
        CookieHost = CookieHost,
        UserAgent = UserAgent,
        FileName = Urls.Count == 1 ? FileName : null
    };

    public static bool IsHttpUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
