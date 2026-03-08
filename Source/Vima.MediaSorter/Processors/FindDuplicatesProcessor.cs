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
using Vima.MediaSorter.Infrastructure.Hashers;
using Vima.MediaSorter.Services;

namespace Vima.MediaSorter.Processors;

public class FindDuplicatesProcessor(
    IFileHasher fileHasher,
    IEnumerable<IVisualFileHasher> visualHashers,
    IFileSystem fileSystem,
    IOutputService outputService,
    IOptions<MediaSorterOptions> options
) : IProcessor
{
    private static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".webp", ".bmp"];

    public ProcessorOptions Option => ProcessorOptions.FindDuplicates;

    public void Process()
    {
        outputService.Start("Find duplicates");

        try
        {
            OutputConfiguration();

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

            outputService.Section("[Step 1/1] Duplicate Detection");
            Stopwatch sw = Stopwatch.StartNew();

            var (visualHashes, binaryHashes, metadata, errors) = GenerateHashes(
                visualHasher,
                allFiles
            );

            var (exactDuplicates, visualDuplicates) = ClusterDuplicates(
                threshold,
                visualHashes,
                binaryHashes,
                targetFolder
            );

            sw.Stop();

            var allDuplicates = exactDuplicates.Concat(visualDuplicates).ToList();
            ReportAnalysisResults(
                allFiles,
                allDuplicates,
                exactDuplicates,
                visualDuplicates,
                metadata,
                errors,
                visualHasher,
                sw.Elapsed
            );

            if (allDuplicates.Count == 0)
            {
                outputService.Complete("No duplicates were detected.");
                return;
            }

            ReviewDuplicates(allDuplicates, metadata);
        }
        catch (Exception ex)
        {
            outputService.Fatal("A critical error occurred.", ex);
        }
    }

    private void OutputConfiguration()
    {
        outputService.Table(
            "Configuration:",
            [
                new("Directory:", options.Value.Directory),
                new("Log file:", outputService.LogFileName),
                new("Binary hasher:", fileHasher.GetType().Name),
            ]
        );
    }

    private void ReportAnalysisResults(
        List<string> allFiles,
        List<List<string>> allDuplicates,
        List<List<string>> exactDuplicates,
        List<List<string>> visualDuplicates,
        ConcurrentDictionary<string, (long size, int width, int height)> metadata,
        ConcurrentBag<(string Path, Exception Ex)> errors,
        IVisualFileHasher? visualHasher,
        TimeSpan duration
    )
    {
        int duplicateSets = allDuplicates.Count;
        int exactRedundant = exactDuplicates.Sum(d => d.Count - 1);
        int visualRedundant = visualDuplicates.Sum(d => d.Count - 1);

        outputService.Table(
            "Analysis result:",
            [
                new("Total scanned:", allFiles.Count.ToString()),
                new("Detection time:", duration.ToString(@"hh\:mm\:ss\.ff")),
                new("Duplicate sets:", duplicateSets.ToString()),
                new("Duplicate files:", (exactRedundant + visualRedundant).ToString()),
                new("  Exact:", exactRedundant.ToString()),
                new("  Visual:", visualRedundant.ToString()),
                new("Errors:", errors.Count.ToString(), !errors.IsEmpty),
            ]
        );

        LogDuplicateGroups("Exact duplicates (byte-for-byte)", exactDuplicates, metadata);

        if (visualHasher != null)
        {
            LogDuplicateGroups(
                $"Visual duplicates ({visualHasher.Type})",
                visualDuplicates,
                metadata
            );
        }

        if (!errors.IsEmpty)
        {
            var errorDetails = errors
                .OrderByPath(e => e.Path)
                .Select(e => $"{fileSystem.GetRelativePath(e.Path)}: {e.Ex.Message}");
            outputService.List("Errors:", errorDetails, OutputLevel.Error, 5);
            outputService.WriteLine(string.Empty, OutputLevel.Error);
        }
    }

    private void LogDuplicateGroups(
        string title,
        List<List<string>> groups,
        ConcurrentDictionary<string, (long size, int width, int height)> metadata
    )
    {
        if (groups.Count == 0)
            return;

        outputService.Subsection(title, OutputLevel.Debug);
        foreach (var group in groups)
        {
            var fileDetails = group
                .OrderByDescending(path => metadata[path].width * metadata[path].height)
                .ThenByDescending(path => metadata[path].size)
                .Select(path =>
                {
                    var (size, w, h) = metadata[path];
                    string resolution = w > 0 ? $"{w}x{h}" : "N/A";
                    return $"{fileSystem.GetRelativePath(path)} [{resolution} | {FormatFileSize(size)}]";
                });

            outputService.List($"Group set ({group.Count} files):", fileDetails, OutputLevel.Debug);
            outputService.WriteLine(string.Empty, OutputLevel.Debug);
        }
    }

    private (
        ConcurrentDictionary<string, ulong> visualHashes,
        ConcurrentDictionary<string, string> binaryHashes,
        ConcurrentDictionary<string, (long size, int width, int height)> metadata,
        ConcurrentBag<(string Path, Exception Ex)> errors
    ) GenerateHashes(IVisualFileHasher? visualHasher, List<string> allFiles)
    {
        return outputService.ExecuteWithProgress(
            "Generating hashes",
            p =>
            {
                int processed = 0;
                ConcurrentDictionary<string, ulong> visualHashes = new();
                ConcurrentDictionary<string, string> binaryHashes = new();
                ConcurrentDictionary<string, (long size, int width, int height)> metadata = new();
                ConcurrentBag<(string Path, Exception Ex)> errors = new();
                Parallel.ForEach(
                    allFiles,
                    new ParallelOptions { MaxDegreeOfParallelism = 25 },
                    path =>
                    {
                        try
                        {
                            var ext = Path.GetExtension(path).ToLower();
                            long fileSize = fileSystem.GetFileSize(path);
                            if (visualHasher != null && ImageExtensions.Contains(ext))
                            {
                                using var stream = fileSystem.CreateFileStream(
                                    path,
                                    FileMode.Open,
                                    FileAccess.Read
                                );
                                using var bitmap =
                                    SKBitmap.Decode(stream)
                                    ?? throw new Exception("Failed to decode the image.");
                                metadata[path] = (fileSize, bitmap.Width, bitmap.Height);
                                visualHashes[path] = visualHasher.GetHash(bitmap);
                            }
                            else
                            {
                                binaryHashes[path] = fileHasher.GetHash(path);
                                metadata[path] = (fileSize, 0, 0);
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

                return (visualHashes, binaryHashes, metadata, errors);
            }
        );
    }

    private (
        List<List<string>> binaryDuplicates,
        List<List<string>> visualDuplicates
    ) ClusterDuplicates(
        int threshold,
        ConcurrentDictionary<string, ulong> visualHashes,
        ConcurrentDictionary<string, string> binaryHashes,
        string? targetFolder = null
    )
    {
        return outputService.ExecuteWithProgress(
            "Clustering duplicates",
            p =>
            {
                List<List<string>> binaryDuplicates = binaryHashes
                    .GroupBy(x => x.Value)
                    .Select(g => g.Select(x => x.Key).ToList())
                    .Where(paths =>
                        paths.Count > 1
                        && (targetFolder == null || paths.Any(p => p.StartsWith(targetFolder)))
                    )
                    .ToList();

                List<List<string>> visualDuplicates = new();
                var processed = new HashSet<string>();
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
        outputService.WriteLine("Duplicate detection modes:");
        outputService.WriteLine(
            "  [0] Exact (default): Byte-for-byte match. Fastest; misses resized/edited images."
        );
        outputService.WriteLine(
            $"  [{(int)VisualHasherType.Average}] Average: High speed. Best for near-identical copies."
        );
        outputService.WriteLine(
            $"  [{(int)VisualHasherType.Difference}] Difference: Balanced. Great for finding resized/compressed versions."
        );
        outputService.WriteLine(
            $"  [{(int)VisualHasherType.Perceptual}] Perceptual: High precision. Finds matches even if filtered/edited."
        );
        outputService.WriteLine(
            $"Note: Visual modes only apply to: {string.Join(", ", ImageExtensions)}"
        );
        outputService.WriteLine();
        var choice = outputService.PromptForEnum<VisualHasherType>("Select mode", 0);
        if ((int)choice == 0)
        {
            outputService.WriteLine();
            return (null, 0);
        }

        var selectedHasher = visualHashers.FirstOrDefault(h => h.Type == choice);
        if (selectedHasher == null)
        {
            outputService.WriteLine(
                $"  Hasher {choice} not registered. Defaulting to Exact.",
                OutputLevel.Warn
            );
            outputService.WriteLine();
            return (null, 0);
        }

        int recommended = choice == VisualHasherType.Perceptual ? 8 : 2;
        int threshold = outputService.PromptForInt(
            $"Threshold 0-64 (match:0, default:{recommended}, loose:15)",
            recommended,
            0,
            64
        );

        outputService.WriteLine();
        return (selectedHasher, Math.Clamp(threshold, 0, 64));
    }

    private void ReviewDuplicates(
        List<List<string>> groups,
        ConcurrentDictionary<string, (long size, int width, int height)> metadata
    )
    {
        if (!outputService.Confirm("Action: Interactively review duplicate groups one by one?"))
        {
            outputService.Complete("  Operation aborted.");
            return;
        }

        string menuPrompt = "[V]iew files | [N]ext group | [Q]uit review: ";

        for (int i = 0; i < groups.Count; i++)
        {
            outputService.WriteLine();
            outputService.WriteLine($"Group {i + 1}/{groups.Count} ({groups[i].Count} files)");

            var sortedGroup = groups[i]
                .OrderByDescending(path => metadata[path].width * metadata[path].height)
                .ThenByDescending(path => metadata[path].size)
                .ToList();
            foreach (var path in sortedGroup)
            {
                var (size, w, h) = metadata[path];
                string resolution = w > 0 ? $"{w}x{h}" : "N/A";
                string relativePath = fileSystem.GetRelativePath(path);
                outputService.WriteLine(
                    $"  - {relativePath} [{resolution} | {FormatFileSize(size)}]"
                );
            }

            bool moveToNext = false;
            outputService.Write(menuPrompt);
            while (!moveToNext)
            {
                var input = outputService.ReadKey(true);
                switch (input)
                {
                    case ConsoleKey.V:
                        OpenGroupFiles(sortedGroup, menuPrompt);
                        break;
                    case ConsoleKey.N:
                        outputService.WriteLine(input.ToString());
                        moveToNext = true;
                        break;
                    case ConsoleKey.Q:
                        outputService.WriteLine(input.ToString());
                        outputService.WriteLine();
                        outputService.Complete();
                        return;
                }
            }
        }

        outputService.WriteLine();
        outputService.Complete();
    }

    private string? SelectTargetFolder(List<string> allFiles)
    {
        outputService.WriteLine("Duplicate search scopes:");
        outputService.WriteLine(
            "  [0] Global (default): Compare every file against every other file."
        );
        outputService.WriteLine(
            "  [1] Targeted: Select one folder and find where its files are duplicated."
        );
        outputService.WriteLine();
        var mode = outputService.PromptForInt("Select search scope", 0, 0, 1);
        outputService.WriteLine();

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
        outputService.List("Target folders:", folderOptions);

        int choice = outputService.PromptForInt("Select folder index", 0, 0, folders.Count - 1);
        outputService.WriteLine();
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
                    outputService.WriteLine();

                errorOccurred = true;
                string relative = fileSystem.GetRelativePath(path);
                outputService.WriteLine($"(!) Failed to open {relative}: {ex.Message}");
            }
        }

        if (errorOccurred)
        {
            outputService.Write(prompt);
        }
    }

    private static string FormatFileSize(long bytes)
    {
        string[] suffix = ["B", "KB", "MB", "GB", "TB"];
        int i;
        double dblSByte = bytes;
        for (i = 0; i < suffix.Length && bytes >= 1024; i++, bytes /= 1024)
            dblSByte = bytes / 1024.0;

        return $"{dblSByte:0.##} {suffix[i]}";
    }
}
