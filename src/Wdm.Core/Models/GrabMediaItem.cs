namespace Wdm.Core.Models;

public class GrabMediaItem
{
    public string Url { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Category { get; set; } = "Other";
    public string Extension { get; set; } = string.Empty;
    public bool IsSelected { get; set; } = true;

    public GrabMediaItem() { }

    public GrabMediaItem(string url, string fileName, string category, string extension)
    {
        Url = url;
        FileName = fileName;
        Category = category;
        Extension = extension;
    }
}
