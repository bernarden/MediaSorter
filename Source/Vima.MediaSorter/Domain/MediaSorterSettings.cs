using System.Collections.Generic;

namespace Vima.MediaSorter.Domain;

public class MediaSorterSettings
{
    /// <summary>
    /// This property will hold the path to the directory being processed.
    /// </summary>
    public string Directory { get; set; } = string.Empty;

    public string FolderNameFormat = "yyyy_MM_dd -";

    public IList<string> ImageExtensions = new List<string>() { ".jpg", ".jpeg" };

    public IList<string> VideoExtensions = new List<string>() { ".mp4" };
}
