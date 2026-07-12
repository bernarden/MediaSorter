using System.Collections.Generic;

namespace Vima.MediaSorter.Domain;

public class FileRemovingResult
{
    public IReadOnlyList<string> DeletedFiles { get; init; } = [];

    public IReadOnlyList<PathError> ErroredFiles { get; init; } = [];
}
