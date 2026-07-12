using System.Collections.Generic;

namespace Vima.MediaSorter.Domain;

public class EmptyFolderCleanupResult
{
    public IReadOnlyList<string> DeletedFolders { get; init; } = [];

    public IReadOnlyList<PathError> ErroredFolders { get; init; } = [];
}
