using System.Collections.Generic;

namespace Vima.MediaSorter.Domain;

public class AssociatedMedia
{
    public IReadOnlyList<string> AssociatedFiles { get; init; } = new List<string>();
    public IReadOnlyList<string> RemainingIgnoredFiles { get; init; } = new List<string>();
}
