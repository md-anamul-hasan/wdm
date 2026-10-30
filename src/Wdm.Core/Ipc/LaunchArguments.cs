namespace Wdm.Core.Ipc;

/// <summary>
/// Parses WDM's command line. Mirrors IDM's switches: <c>/d url</c>, <c>/p path</c>, <c>/f name</c>,
/// <c>/n</c> (silent), <c>/a</c> (queue only). A bare http(s) URL is accepted too.
/// </summary>
public static class LaunchArguments
{
    public static ExternalDownloadRequest Parse(IEnumerable<string> args)
    {
        var request = new ExternalDownloadRequest();
        var list = args.ToList();

        for (int i = 0; i < list.Count; i++)
        {
            var arg = list[i].Trim('"', ' ');
            var key = arg.ToLowerInvariant();
            bool hasValue = i + 1 < list.Count;

            if (key is "/d" or "-d" or "--url" && hasValue)
            {
                AddUrl(request, list[++i]);
            }
            else if (key is "/p" or "-p" or "--path" && hasValue)
            {
                request.DestinationDirectory = ResolveDirectory(list[++i].Trim('"'));
            }
            else if (key is "/f" or "-f" or "--file" && hasValue)
            {
                request.FileName = list[++i].Trim('"');
            }
            else if (key is "/sha256" or "-sha256" or "--sha256" && hasValue)
            {
                request.Sha256 = list[++i].Trim('"');
            }
            else if (key is "/n" or "-n" or "--silent")
            {
                request.Silent = true;
            }
            else if (key is "/a" or "-a" or "--queue")
            {
                request.QueueOnly = true;
            }
            else
            {
                AddUrl(request, arg);
            }
        }

        return request;
    }

    private static string ResolveDirectory(string path)
    {
        try { return Path.GetFullPath(path); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return path; }
    }

    private static void AddUrl(ExternalDownloadRequest request, string value)
    {
        var url = value.Trim('"', ' ');
        if (ExternalDownloadRequest.IsHttpUrl(url) && !request.Urls.Contains(url))
        {
            request.Urls.Add(url);
        }
    }
}
