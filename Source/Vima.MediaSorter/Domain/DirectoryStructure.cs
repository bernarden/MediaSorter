using System;
using System.Collections.Generic;

namespace Vima.MediaSorter.Domain;

public class DirectoryStructure
{
    public IDictionary<DateTime, string> DateToExistingDirectoryMapping { get; } = new Dictionary<DateTime, string>();

    public IList<string> SortedFolders { get; } = new List<string>();

    public IList<string> UnsortedFolders { get; } = new List<string>();
}