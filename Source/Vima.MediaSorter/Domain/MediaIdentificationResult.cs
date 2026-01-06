using System;
using System.Collections.Generic;

namespace Vima.MediaSorter.Domain;

public class MediaIdentificationResult
{
    public List<MediaFile> MediaFiles { get; init; } = new();
    public IReadOnlyDictionary<DateTime, string> ExistingDirectoryMapping { get; init; } = new Dictionary<DateTime, string>();
    public IReadOnlyList<string> IgnoredFiles { get; init; } = new List<string>();
}