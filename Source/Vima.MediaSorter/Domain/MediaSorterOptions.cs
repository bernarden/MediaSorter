namespace Vima.MediaSorter.Domain;

public class MediaSorterOptions
{
    public string Directory { get; set; } = string.Empty;

    public string FolderNameFormat = "yyyy_MM_dd -";
}
