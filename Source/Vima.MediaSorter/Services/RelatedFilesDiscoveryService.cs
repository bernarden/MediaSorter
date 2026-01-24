using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Vima.MediaSorter.Domain;

namespace Vima.MediaSorter.Services;

public interface IRelatedFilesDiscoveryService
{
    AssociatedMedia AssociateRelatedFiles(IEnumerable<MediaFile> identifiedFiles, IEnumerable<string> ignoredFiles);
}

public partial class RelatedFilesDiscoveryService : IRelatedFilesDiscoveryService
{
    public AssociatedMedia AssociateRelatedFiles(IEnumerable<MediaFile> identifiedFiles, IEnumerable<string> ignoredFiles)
    {
        var associated = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ignoredIndex = ignoredFiles
                .GroupBy(f => Path.ChangeExtension(f, null), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        void ClaimFiles(string lookupPath, MediaFile target)
        {
            foreach (var file in ignoredIndex.GetValueOrDefault(lookupPath) ?? [])
                if (associated.Add(file))
                    target.RelatedFiles.Add(file);
        }

        foreach (var mediaFile in identifiedFiles)
        {
            // Same name but different extension
            ClaimFiles(Path.ChangeExtension(mediaFile.FilePath, null), mediaFile);

            // GoPro specific files
            var dir = Path.GetDirectoryName(mediaFile.FilePath) ?? "";
            var fileName = Path.GetFileNameWithoutExtension(mediaFile.FilePath);
            if (GetGoProRegex().IsMatch(fileName))
            {
                var lrvPath = Path.Combine(dir, fileName.Remove(1, 1).Insert(1, "L"));
                ClaimFiles(lrvPath, mediaFile);
            }
        }

        var associatedList = new List<string>();
        var remainingList = new List<string>();
        foreach (var f in ignoredFiles)
        {
            (associated.Contains(f) ? associatedList : remainingList).Add(f);
        }

        return new AssociatedMedia() { AssociatedFiles = associatedList, RemainingIgnoredFiles = remainingList };
    }

    [GeneratedRegex(@"G[HX](\d{2}|\w{2})\d{4}", RegexOptions.IgnoreCase)]
    private static partial Regex GetGoProRegex();
}