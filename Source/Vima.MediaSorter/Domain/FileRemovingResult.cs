using System.Collections.Generic;

namespace Vima.MediaSorter.Domain;

public class FileRemovingResult
{
    public IReadOnlyList<string> DeletedFiles { get; init; } = new List<string>();

    public IReadOnlyList<PathError> ErroredFiles { get; init; } = new List<PathError>();
}
