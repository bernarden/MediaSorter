using System;
using System.Collections.Generic;

namespace Vima.MediaSorter.Processors.FindDuplicates;

public static class FindDuplicatesConstants
{
    public static readonly IReadOnlySet<string> ImageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".bmp",
    };
}
