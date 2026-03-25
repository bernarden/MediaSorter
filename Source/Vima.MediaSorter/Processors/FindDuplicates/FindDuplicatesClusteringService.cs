using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Vima.MediaSorter.Domain;
using Vima.MediaSorter.Services;
using Vima.MediaSorter.Services.Hashers;

namespace Vima.MediaSorter.Processors.FindDuplicates;

public interface IFindDuplicatesClusteringService
{
    (
        IReadOnlyList<IReadOnlyList<string>> BinaryDuplicates,
        IReadOnlyList<IReadOnlyList<string>> VisualDuplicates
    ) Cluster(
        IVisualFileHasher? visualHasher,
        int threshold,
        IDictionary<string, FindDuplicatesFile> hashes,
        string? targetFolder = null
    );
}

public class FindDuplicatesClusteringService(IOutputService outputService)
    : IFindDuplicatesClusteringService
{
    public (
        IReadOnlyList<IReadOnlyList<string>> BinaryDuplicates,
        IReadOnlyList<IReadOnlyList<string>> VisualDuplicates
    ) Cluster(
        IVisualFileHasher? visualHasher,
        int threshold,
        IDictionary<string, FindDuplicatesFile> hashes,
        string? targetFolder = null
    )
    {
        return outputService.ExecuteWithProgress(
            "Clustering duplicates",
            p =>
            {
                List<List<string>> binaryDuplicates = hashes
                    .Values.GroupBy(entry => entry.BinaryHash)
                    .Select(group => group.Select(entry => entry.Path).ToList())
                    .Where(paths =>
                        paths.Count > 1
                        && (targetFolder == null || paths.Any(p => p.StartsWith(targetFolder)))
                    )
                    .ToList();

                List<List<string>> visualDuplicates = new();
                var processed = new HashSet<string>();
                var visualHashes = hashes
                    .Select(kvp => new
                    {
                        kvp.Key,
                        Hash = visualHasher?.Type switch
                        {
                            VisualHasherType.Average => kvp.Value.AverageHash,
                            VisualHasherType.Difference => kvp.Value.DifferenceHash,
                            VisualHasherType.Perceptual => kvp.Value.PerceptualHash,
                            _ => 0UL,
                        },
                    })
                    .Where(x => x.Hash != 0)
                    .ToDictionary(x => x.Key, x => x.Hash);
                var paths = visualHashes.Keys.ToList();
                var targetPaths =
                    targetFolder == null
                        ? paths
                        : paths.Where(path => path.StartsWith(targetFolder)).ToList();

                for (int i = 0; i < targetPaths.Count; i++)
                {
                    string targetPath = targetPaths[i];
                    if (!processed.Contains(targetPath))
                    {
                        var currentSet = new List<string> { targetPath };
                        ulong h1 = visualHashes[targetPath];

                        for (int j = i + 1; j < paths.Count; j++)
                        {
                            string anotherPath = paths[j];
                            if (processed.Contains(anotherPath) || targetPath == anotherPath)
                                continue;

                            var h2 = visualHashes[anotherPath];
                            if (BitOperations.PopCount(h1 ^ h2) <= threshold)
                            {
                                currentSet.Add(anotherPath);
                                processed.Add(anotherPath);
                            }
                        }

                        if (currentSet.Count > 1)
                            visualDuplicates.Add(currentSet);

                        processed.Add(targetPath);
                    }

                    p.Report((double)i / targetPaths.Count);
                }

                p.Report(1.0);
                return (binaryDuplicates, visualDuplicates);
            }
        );
    }
}
