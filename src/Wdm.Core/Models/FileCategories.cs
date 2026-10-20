namespace Wdm.Core.Models;

public enum FileCategory
{
    General,
    Video,
    Music,
    Compressed,
    Documents,
    Programs,
    Images
}

/// <summary>Single source of truth for mapping file extensions to download categories.</summary>
public static class FileCategories
{
    private static readonly Dictionary<string, FileCategory> Map = Build();

    private static Dictionary<string, FileCategory> Build()
    {
        var map = new Dictionary<string, FileCategory>(StringComparer.OrdinalIgnoreCase);

        void Add(FileCategory category, string extensions)
        {
            foreach (var ext in extensions.Split(' ', StringSplitOptions.RemoveEmptyEntries)) map["." + ext] = category;
        }

        Add(FileCategory.Video, "mp4 mkv avi webm mov m4v wmv flv mpg mpeg 3gp ogv m3u8 ts");
        Add(FileCategory.Music, "mp3 wav flac aac ogg m4a wma");
        Add(FileCategory.Compressed, "zip rar 7z tar gz bz2 xz iso");
        Add(FileCategory.Documents, "pdf doc docx txt rtf csv xls xlsx ppt pptx epub");
        Add(FileCategory.Programs, "exe msi apk bat cmd pkg");
        Add(FileCategory.Images, "png jpg jpeg webp gif svg");
        return map;
    }

    /// <param name="extension">With or without the leading dot.</param>
    public static FileCategory FromExtension(string? extension)
    {
        if (string.IsNullOrEmpty(extension)) return FileCategory.General;
        return Map.GetValueOrDefault(extension[0] == '.' ? extension : "." + extension, FileCategory.General);
    }

    public static FileCategory FromFileName(string? fileName) =>
        FromExtension(string.IsNullOrEmpty(fileName) ? null : Path.GetExtension(fileName));

    public static bool IsKnownExtension(string? extension) => FromExtension(extension) != FileCategory.General;

    /// <summary>Label shown in the UI and used for sidebar filtering.</summary>
    public static string DisplayName(FileCategory category) => category switch
    {
        FileCategory.General => "Other",
        _ => category.ToString()
    };
}
