using System.Collections.Generic;

namespace Vima.MediaSorter.Domain;

public class MediaFile
{
    public enum Type
    {
        Image,
        Video
    }

    public Type MediaType { get; }
    public string FilePath { get; }
    public List<string> RelatedFiles { get; }

    public MediaFile(string filePath, Type mediaType)
    {
        FilePath = filePath;
        MediaType = mediaType;
        RelatedFiles = [];
    }
}