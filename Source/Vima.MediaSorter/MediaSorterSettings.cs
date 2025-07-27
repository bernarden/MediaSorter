namespace Vima.MediaSorter;

public class MediaSorterSettings
{
    /// <summary>
    /// This property will hold the path to the directory being processed.
    /// </summary>
    public string Directory { get; set; } = string.Empty;

    /// <summary>
    /// If true, the application should only simulate operations without actually moving/copying files.
    /// </summary>
    public bool SimulateMode { get; set; }
}
