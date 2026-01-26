using System.Collections.Generic;

namespace Vima.MediaSorter.Domain;

public class MediaFile(string filePath)
{
    public string FilePath { get; } = filePath;
    public List<string> RelatedFiles { get; } = [];
    public string? TargetSubFolder { get; private set; }
    public void SetTargetSubFolder(string subFolder) => TargetSubFolder = subFolder;
}