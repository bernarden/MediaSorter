using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Vima.MediaSorter.Infrastructure;

public static class PathExtensions
{
    public static IOrderedEnumerable<T> OrderByPath<T>(
        this IEnumerable<T> source,
        Func<T, string> pathSelector
    )
    {
        return source
            .OrderBy(item => Path.GetDirectoryName(pathSelector(item)))
            .ThenBy(item => Path.GetFileName(pathSelector(item)));
    }
}
