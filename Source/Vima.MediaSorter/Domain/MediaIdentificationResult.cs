using System.Collections.Generic;

namespace Vima.MediaSorter.Domain;

public class MediaIdentificationResult
{
    public List<MediaFile> MediaFiles { get; init; } = new();
    public IReadOnlyList<string> IgnoredFiles { get; init; } = new List<string>();
}