using System.Collections.Generic;

namespace Vima.MediaSorter.Domain;

public class EmptyFolderCleanupResult
{
    public IReadOnlyList<string> DeletedFolders { get; init; } = new List<string>();

    public IReadOnlyList<PathError> ErroredFolders { get; init; } = new List<PathError>();
}
