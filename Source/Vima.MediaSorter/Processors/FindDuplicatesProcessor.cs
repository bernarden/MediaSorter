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

            var (visualHasher, threshold) = SelectVisualHasher();

            outputService.Section("[Step 1/1] Duplicate Detection");
            Stopwatch sw = Stopwatch.StartNew();

            List<string> allFiles =
            [
                .. fileSystem.EnumerateFiles(
                    options.Value.Directory,
                    "*",
                    SearchOption.AllDirectories
                ),
            ];

            var (visualHashes, binaryHashes, metadata, errors) = GenerateHashes(
                visualHasher,
                allFiles
            );

            var (exactDuplicates, visualDuplicates) = ClusterDuplicates(
                threshold,
                visualHashes,
                binaryHashes
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
                threshold,
                sw.Elapsed
            );

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
        int threshold,
        TimeSpan duration
    )
    {
        int duplicateSets = allDuplicates.Count;
        int redundantFiles = allDuplicates.Sum(d => d.Count) - duplicateSets;

        outputService.Table(
            "Analysis result:",
            [
                new("Total scanned:", allFiles.Count.ToString()),
                new("Detection time:", duration.ToString(@"hh\:mm\:ss\.ff")),
                new("Duplicate sets:", duplicateSets.ToString()),
                new("Redundant files:", redundantFiles.ToString()),
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
        ConcurrentDictionary<string, string> binaryHashes
    )
    {
        return outputService.ExecuteWithProgress(
            "Clustering duplicates",
            p =>
            {
                List<List<string>> binaryDuplicates = new();
                List<List<string>> visualDuplicates = new();

                var binaryGroups = binaryHashes.GroupBy(x => x.Value).Where(g => g.Count() > 1);
                foreach (var group in binaryGroups)
                {
                    binaryDuplicates.Add([.. group.Select(x => x.Key)]);
                }

                if (!visualHashes.IsEmpty)
                {
                    var paths = visualHashes.Keys.ToList();
                    var processed = new HashSet<string>();

                    for (int i = 0; i < paths.Count; i++)
                    {
                        if (!processed.Contains(paths[i]))
                        {
                            var currentSet = new List<string> { paths[i] };
                            ulong h1 = visualHashes[paths[i]];

                            for (int j = i + 1; j < paths.Count; j++)
                            {
                                if (processed.Contains(paths[j]))
                                    continue;

                                var h2 = visualHashes[paths[j]];
                                if (BitOperations.PopCount(h1 ^ h2) <= threshold)
                                {
                                    currentSet.Add(paths[j]);
                                    processed.Add(paths[j]);
                                }
                            }

                            if (currentSet.Count > 1)
                                visualDuplicates.Add(currentSet);
                            processed.Add(paths[i]);
                        }

                        p.Report((double)i / paths.Count);
                    }
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
        if (groups.Count == 0)
            return;

        if (!outputService.Confirm("Action: Interactively review duplicate groups one by one?"))
        {
            outputService.Complete("  Operation aborted.");
            return;
        }

        string menuPrompt = "[V] View Files | [N] Next Group | [Q] Quit Review: ";

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
