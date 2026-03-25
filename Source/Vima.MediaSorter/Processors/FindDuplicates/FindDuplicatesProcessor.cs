using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using SkiaSharp;
using Vima.MediaSorter.Domain;
using Vima.MediaSorter.Infrastructure;
using Vima.MediaSorter.Services;
using Vima.MediaSorter.Services.Hashers;

namespace Vima.MediaSorter.Processors.FindDuplicates;

public class FindDuplicatesProcessor(
    IFileHasher fileHasher,
    IEnumerable<IVisualFileHasher> visualHashers,
    IFindDuplicatesReporter findDuplicatesReporter,
    IFindDuplicatesCacheService findDuplicatesCacheService,
    IFileSystem fileSystem,
    IOutputService outputService,
    IOptions<MediaSorterOptions> options
) : IProcessor
{
    private readonly string cacheFilePath = Path.Combine(
        options.Value.Directory,
        "Vima.MediaSorter.Hashes.jsonl"
    );

    private readonly (
        IVisualFileHasher Avg,
        IVisualFileHasher Diff,
        IVisualFileHasher Perc
    ) hashers = (
        Avg: visualHashers.First(x => x.Type == VisualHasherType.Average),
        Diff: visualHashers.First(x => x.Type == VisualHasherType.Difference),
        Perc: visualHashers.First(x => x.Type == VisualHasherType.Perceptual)
    );

    private readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg",
        ".jpeg",
        ".png",
        ".webp",
        ".bmp",
    };

    public ProcessorOptions Option => ProcessorOptions.FindDuplicates;

    public async Task Process(CancellationToken token = default)
    {
        outputService.Start("Find duplicates");

        try
        {
            findDuplicatesReporter.ReportConfiguration();

            List<string> allFiles =
            [
                .. fileSystem.EnumerateFiles(
                    options.Value.Directory,
                    "*",
                    SearchOption.AllDirectories
                ),
            ];
            string? targetFolder = SelectTargetFolder(allFiles);

            var (visualHasher, threshold) = SelectVisualHasher();

            outputService.Section("[Step 1/1] Duplicate Detection", OutputLevel.Info);
            Stopwatch sw = Stopwatch.StartNew();

            var (hashes, errors) = await GenerateHashes(allFiles);

            var (exactDuplicates, visualDuplicates) = ClusterDuplicates(
                visualHasher,
                threshold,
                hashes,
                targetFolder
            );

            sw.Stop();

            var allDuplicates = exactDuplicates.Concat(visualDuplicates).ToList();
            findDuplicatesReporter.ReportAnalysisResults(
                allFiles,
                allDuplicates,
                exactDuplicates,
                visualDuplicates,
                hashes,
                errors,
                visualHasher,
                sw.Elapsed
            );

            if (allDuplicates.Count == 0)
            {
                outputService.Complete("No duplicates were detected.");
                return;
            }

            ReviewDuplicates(allDuplicates, hashes);
        }
        catch (Exception ex)
        {
            outputService.Fatal("A critical error occurred during processing.", ex);
        }
    }

    private async Task<(
        ConcurrentDictionary<string, FindDuplicatesFile> hashes,
        ConcurrentBag<(string Path, Exception Ex)> errors
    )> GenerateHashes(List<string> allFiles)
    {
        var cache = findDuplicatesCacheService.Load();
        var results = new ConcurrentDictionary<string, FindDuplicatesFile>();
        var errors = new ConcurrentBag<(string Path, Exception Ex)>();

        await findDuplicatesCacheService.StartWriterAsync();

        outputService.ExecuteWithProgress(
            "Generating hashes",
            p =>
            {
                int processed = 0;

                Parallel.ForEach(
                    allFiles,
                    new ParallelOptions { MaxDegreeOfParallelism = 25 },
                    path =>
                    {
                        try
                        {
                            var lastWrite = fileSystem.GetLastWriteTimeUtc(path);
                            long fileSize = fileSystem.GetFileSize(path);

                            if (
                                cache.TryGetValue(path, out var cachedEntry)
                                && cachedEntry.LastModified == lastWrite
                                && cachedEntry.Size == fileSize
                            )
                            {
                                results[path] = cachedEntry;
                                findDuplicatesCacheService.QueueToPersist(cachedEntry);
                            }
                            else
                            {
                                var file = CreateFindDuplicatesFile(path, fileSize, lastWrite);
                                findDuplicatesCacheService.QueueToPersist(file);
                                results[path] = file;
                            }
                        }
                        catch (Exception ex)
                        {
                            errors.Add((path, ex));
                        }

                        var current = Interlocked.Increment(ref processed);
                        if (current % 20 == 0)
                            p.Report((double)current / allFiles.Count);
                    }
                );
            }
        );

        await findDuplicatesCacheService.CommitAsync();

        return (results, errors);
    }

    private FindDuplicatesFile CreateFindDuplicatesFile(
        string path,
        long fileSize,
        DateTime lastWrite
    )
    {
        string bHash = fileHasher.GetHash(path);

        if (!ImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
        {
            return new FindDuplicatesFile(path, bHash, 0, 0, 0, fileSize, 0, 0, lastWrite);
        }

        using var stream = fileSystem.CreateFileStream(path, FileMode.Open, FileAccess.Read);
        using var bitmap = SKBitmap.Decode(stream);

        if (bitmap == null)
        {
            return new FindDuplicatesFile(path, bHash, 0, 0, 0, fileSize, 0, 0, lastWrite);
        }

        return new FindDuplicatesFile(
            path,
            bHash,
            hashers.Avg?.GetHash(bitmap) ?? 0,
            hashers.Diff?.GetHash(bitmap) ?? 0,
            hashers.Perc?.GetHash(bitmap) ?? 0,
            fileSize,
            bitmap.Width,
            bitmap.Height,
            lastWrite
        );
    }

    private (
        List<List<string>> binaryDuplicates,
        List<List<string>> visualDuplicates
    ) ClusterDuplicates(
        IVisualFileHasher? visualHasher,
        int threshold,
        ConcurrentDictionary<string, FindDuplicatesFile> hashes,
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

    private (IVisualFileHasher? visualHasher, int threshold) SelectVisualHasher()
    {
        outputService.WriteLine("Duplicate detection modes:", OutputLevel.Info);
        outputService.WriteLine(
            "  [0] Exact (default): Byte-for-byte match. Fastest; misses resized/edited images.",
            OutputLevel.Info
        );
        outputService.WriteLine(
            $"  [{(int)VisualHasherType.Average}] Average: High speed. Best for near-identical copies.",
            OutputLevel.Info
        );
        outputService.WriteLine(
            $"  [{(int)VisualHasherType.Difference}] Difference: Balanced. Great for finding resized/compressed versions.",
            OutputLevel.Info
        );
        outputService.WriteLine(
            $"  [{(int)VisualHasherType.Perceptual}] Perceptual: High precision. Finds matches even if filtered/edited.",
            OutputLevel.Info
        );
        outputService.WriteLine(
            $"Note: Visual modes only apply to: {string.Join(", ", ImageExtensions)}",
            OutputLevel.Info
        );
        outputService.WriteLine(string.Empty, OutputLevel.Info);
        var choice = (VisualHasherType)
            outputService.PromptForInt("Select mode", 0, 0, 3, OutputLevel.Info);
        if ((int)choice == 0)
        {
            outputService.WriteLine(string.Empty, OutputLevel.Info);
            return (null, 0);
        }

        var selectedHasher = visualHashers.FirstOrDefault(h => h.Type == choice);
        if (selectedHasher == null)
        {
            outputService.WriteLine(
                $"  Hasher {choice} not registered. Defaulting to Exact.",
                OutputLevel.Warn
            );
            outputService.WriteLine(string.Empty, OutputLevel.Warn);
            return (null, 0);
        }

        int recommended = choice == VisualHasherType.Perceptual ? 8 : 2;
        int threshold = outputService.PromptForInt(
            $"Threshold 0-64 (match:0, default:{recommended}, loose:15)",
            recommended,
            0,
            64,
            OutputLevel.Info
        );

        outputService.WriteLine(string.Empty, OutputLevel.Info);
        return (selectedHasher, Math.Clamp(threshold, 0, 64));
    }

    private void ReviewDuplicates(
        List<List<string>> groups,
        ConcurrentDictionary<string, FindDuplicatesFile> hashes
    )
    {
        if (
            !outputService.Confirm(
                "Action: Interactively review duplicate groups one by one?",
                OutputLevel.Info
            )
        )
        {
            outputService.Complete("  Operation aborted.");
            return;
        }

        string menuPrompt = "[V]iew files | [N]ext group | [Q]uit review: ";

        for (int i = 0; i < groups.Count; i++)
        {
            outputService.WriteLine(string.Empty, OutputLevel.Info);
            outputService.WriteLine(
                $"Group {i + 1}/{groups.Count} ({groups[i].Count} files)",
                OutputLevel.Info
            );

            var sortedGroup = groups[i]
                .OrderByDescending(path => hashes[path].Width * hashes[path].Height)
                .ThenByDescending(path => hashes[path].Size)
                .ToList();
            foreach (var path in sortedGroup)
            {
                string resolution =
                    hashes[path].Width > 0 ? $"{hashes[path].Width}x{hashes[path].Height}" : "N/A";
                string relativePath = fileSystem.GetRelativePath(path);
                outputService.WriteLine(
                    $"  - {relativePath} [{resolution} | {findDuplicatesReporter.FormatFileSize(hashes[path].Size)}]",
                    OutputLevel.Info
                );
            }

            bool moveToNext = false;
            outputService.Write(menuPrompt, OutputLevel.Info);
            while (!moveToNext)
            {
                var input = outputService.ReadKey(true);
                switch (input)
                {
                    case ConsoleKey.V:
                        OpenGroupFiles(sortedGroup, menuPrompt);
                        break;
                    case ConsoleKey.N:
                        outputService.WriteLine(input.ToString(), OutputLevel.Info);
                        moveToNext = true;
                        break;
                    case ConsoleKey.Q:
                        outputService.WriteLine(input.ToString(), OutputLevel.Info);
                        outputService.WriteLine(string.Empty, OutputLevel.Info);
                        outputService.Complete();
                        return;
                }
            }
        }

        outputService.WriteLine(string.Empty, OutputLevel.Info);
        outputService.Complete();
    }

    private string? SelectTargetFolder(List<string> allFiles)
    {
        outputService.WriteLine("Duplicate search scopes:", OutputLevel.Info);
        outputService.WriteLine(
            "  [0] Global (default): Compare every file against every other file.",
            OutputLevel.Info
        );
        outputService.WriteLine(
            "  [1] Targeted: Select one folder and find where its files are duplicated.",
            OutputLevel.Info
        );
        outputService.WriteLine(string.Empty, OutputLevel.Info);
        var mode = outputService.PromptForInt("Select search scope", 0, 0, 1, OutputLevel.Info);
        outputService.WriteLine(string.Empty, OutputLevel.Info);

        if (mode == 0)
            return null;

        var folders = allFiles
            .Select(Path.GetDirectoryName)
            .Where(d => d != null)
            .Distinct()
            .OrderBy(d => d)
            .ToList();
        int maxDigits = folders.Count.ToString().Length;
        var folderOptions = folders.Select(
            (path, i) =>
            {
                string prefix = $"[{i}]".PadLeft(maxDigits + 2);
                string relativePath = fileSystem.GetRelativePath(path);
                string displayPath = string.IsNullOrEmpty(relativePath) ? "." : relativePath;
                return $"{prefix} {displayPath}";
            }
        );
        outputService.List("Target folders:", folderOptions, OutputLevel.Info);

        int choice = outputService.PromptForInt(
            "Select folder index",
            0,
            0,
            folders.Count - 1,
            OutputLevel.Info
        );
        outputService.WriteLine(string.Empty, OutputLevel.Info);
        return folders[choice];
    }

    private void OpenGroupFiles(List<string> group, string prompt)
    {
        bool errorOccurred = false;

        foreach (var path in group)
        {
            try
            {
                System.Diagnostics.Process.Start(
                    new ProcessStartInfo(path) { UseShellExecute = true }
                );
            }
            catch (Exception ex)
            {
                if (!errorOccurred)
                    outputService.WriteLine(string.Empty, OutputLevel.Info);

                errorOccurred = true;
                string relative = fileSystem.GetRelativePath(path);
                outputService.WriteLine(
                    $"(!) Failed to open {relative}: {ex.Message}",
                    OutputLevel.Warn
                );
            }
        }

        if (errorOccurred)
        {
            outputService.Write(prompt, OutputLevel.Info);
        }
    }
}
