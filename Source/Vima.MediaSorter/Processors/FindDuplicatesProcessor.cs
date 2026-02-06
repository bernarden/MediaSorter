using Microsoft.Extensions.Options;
using SkiaSharp;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Vima.MediaSorter.Domain;
using Vima.MediaSorter.Infrastructure.Hashers;
using Vima.MediaSorter.Services;
using Vima.MediaSorter.UI;

namespace Vima.MediaSorter.Processors;

public class FindDuplicatesProcessor(
    IFileHasher fileHasher,
    IEnumerable<IVisualFileHasher> visualHashers,
    IAuditLogService auditLogService,
    IOptions<MediaSorterOptions> options
) : IProcessor
{
    private static readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".png", ".webp", ".bmp" };

    public ProcessorOptions Option => ProcessorOptions.FindDuplicates;

    public void Process()
    {
        string logPath = auditLogService.Initialise();

        OutputConfiguration();

        var (visualHasher, threshold) = SelectVisualHasher();

        try
        {
            Console.WriteLine("[Step 1/1] Duplicate Detection");
            Console.WriteLine(ConsoleHelper.TaskSeparator);
            Stopwatch sw = Stopwatch.StartNew();

            List<string> allFiles = Directory
                .EnumerateFiles(options.Value.Directory, "*", SearchOption.AllDirectories)
                .ToList();

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
            LogDuplicates(
                exactDuplicates,
                visualDuplicates,
                metadata,
                errors,
                visualHasher,
                threshold,
                allFiles.Count,
                sw.Elapsed
            );

            var allDuplicates = exactDuplicates.Concat(visualDuplicates).ToList();
            DisplayFinalSummary(allFiles, allDuplicates, errors, logPath, sw.Elapsed);
        }
        catch (Exception ex)
        {
            LogError("A critical error occurred.", ex);
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n[FATAL ERROR] {ex.Message}");
            Console.WriteLine($"Details logged to: {logPath}");
            Console.ResetColor();
        }
    }

    private void OutputConfiguration()
    {
        Console.WriteLine($"Configuration:");
        Console.WriteLine($"  Directory: {options.Value.Directory}");
        Console.WriteLine();
    }

    private (
        ConcurrentDictionary<string, ulong> visualHashes,
        ConcurrentDictionary<string, string> binaryHashes,
        ConcurrentDictionary<string, (long size, string resolution)> metadata,
        ConcurrentBag<(string Path, Exception Ex)> errors
    ) GenerateHashes(IVisualFileHasher? visualHasher, List<string> allFiles)
    {
        return ConsoleHelper.ExecuteWithProgress(
            "Generating hashes",
            p =>
            {
                int processed = 0;
                ConcurrentDictionary<string, ulong> visualHashes = new();
                ConcurrentDictionary<string, string> binaryHashes = new();
                ConcurrentDictionary<string, (long size, string resolution)> metadata = new();
                ConcurrentBag<(string Path, Exception Ex)> errors = new();
                Parallel.ForEach(
                    allFiles,
                    new ParallelOptions { MaxDegreeOfParallelism = 25 },
                    path =>
                    {
                        try
                        {
                            var info = new FileInfo(path);
                            var ext = info.Extension.ToLower();
                            if (visualHasher != null && ImageExtensions.Contains(ext))
                            {
                                using var stream = File.OpenRead(path);
                                using var bitmap = SKBitmap.Decode(stream);
                                metadata[path] = (info.Length, $"{bitmap.Width}x{bitmap.Height}");
                                visualHashes[path] = visualHasher.GetHash(bitmap);
                            }
                            else
                            {
                                binaryHashes[path] = fileHasher.GetHash(path);
                                metadata[path] = (info.Length, "N/A");
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

    private static (
        List<List<string>> binaryDuplicates,
        List<List<string>> visualDuplicates
    ) ClusterDuplicates(
        int threshold,
        ConcurrentDictionary<string, ulong> visualHashes,
        ConcurrentDictionary<string, string> binaryHashes
    )
    {
        return ConsoleHelper.ExecuteWithProgress(
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
        Console.WriteLine("Duplicate Detection Modes:");
        Console.WriteLine(
            "  [0] Exact (default): Byte-for-byte match. Fastest; misses resized/edited images."
        );
        Console.WriteLine(
            $"  [{(int)VisualHasherType.Average}] Average: High speed. Best for near-identical copies."
        );
        Console.WriteLine(
            $"  [{(int)VisualHasherType.Difference}] Difference: Balanced. Great for finding resized/compressed versions."
        );
        Console.WriteLine(
            $"  [{(int)VisualHasherType.Perceptual}] Perceptual: High precision. Finds matches even if filtered/edited."
        );
        Console.WriteLine(
            $"Note: Visual modes only apply to: {string.Join(", ", ImageExtensions)}"
        );
        Console.WriteLine();
        var choice = ConsoleHelper.PromptForEnum<VisualHasherType>("Select mode", 0);
        if ((int)choice == 0)
        {
            Console.WriteLine();
            return (null, 0);
        }

        var selectedHasher = visualHashers.FirstOrDefault(h => h.Type == choice);
        if (selectedHasher == null)
        {
            Console.WriteLine($"(!) Hasher {choice} not registered. Defaulting to Exact.");
            Console.WriteLine();
            return (null, 0);
        }

        int recommended = choice == VisualHasherType.Perceptual ? 8 : 2;
        Console.Write($"Threshold 0-64 (match:0, default:{recommended}, loose:15): ");
        if (!int.TryParse(Console.ReadLine(), out int threshold))
        {
            threshold = recommended;
        }

        Console.WriteLine();
        return (selectedHasher, Math.Clamp(threshold, 0, 64));
    }

    private static void DisplayFinalSummary(
        List<string> allFiles,
        List<List<string>> duplicates,
        ConcurrentBag<(string, Exception)> errors,
        string log,
        TimeSpan duration
    )
    {
        int numberOfSetsWithDuplicaates = duplicates.Count;
        int redundant = duplicates.Sum(d => d.Count) - numberOfSetsWithDuplicaates;

        Console.WriteLine();
        Console.WriteLine("Analysis Result:");
        Console.WriteLine($"  Total scanned:   {allFiles.Count}");
        Console.WriteLine($"  Detection time:  {duration:hh\\:mm\\:ss\\.ff}");
        Console.WriteLine($"  Duplicate sets:  {numberOfSetsWithDuplicaates}");
        Console.WriteLine($"  Redundant files: {redundant}");
        if (!errors.IsEmpty)
            Console.WriteLine($"  Errors:          {errors.Count} (see log)");
        Console.WriteLine();
        Console.WriteLine($"Processing complete. Audit log: {log}");
    }

    private void LogDuplicates(
        List<List<string>> exactDuplicates,
        List<List<string>> visualDuplicates,
        ConcurrentDictionary<string, (long size, string resolution)> metadata,
        ConcurrentBag<(string Path, Exception Ex)> errors,
        IVisualFileHasher? visualHasher,
        int threshold,
        int totalScanned,
        TimeSpan duration
    )
    {
        int duplicateSets = exactDuplicates.Count + visualDuplicates.Count;
        int redundantFiles =
            exactDuplicates.Sum(d => d.Count) + visualDuplicates.Sum(d => d.Count) - duplicateSets;

        auditLogService.LogHeader("DUPLICATE DETECTION SUMMARY");
        auditLogService.LogLine($"Directory:      {options.Value.Directory}");
        auditLogService.LogLine($"Total Scanned:  {totalScanned}");
        auditLogService.LogLine($"Execution Time: {duration:hh\\:mm\\:ss\\.ff}");
        auditLogService.LogLine($"Duplicate Sets: {duplicateSets}");
        auditLogService.LogLine($"Redundant:      {redundantFiles} files can be removed");
        auditLogService.LogLine($"Errors:         {errors.Count}");

        auditLogService.LogHeader("SCAN CONFIGURATION");
        auditLogService.LogLine($"Binary Hasher:  {fileHasher.GetType().Name}");

        if (visualHasher != null)
        {
            auditLogService.LogLine($"Visual Hasher:  {visualHasher.Type}");
            auditLogService.LogLine($"Threshold:      {threshold} (Range 0-64)");
        }

        auditLogService.LogHeader("EXACT DUPLICATES (BYTE-FOR-BYTE)");
        LogDuplicateGroups(exactDuplicates, metadata);

        if (visualHasher != null)
        {
            auditLogService.LogHeader($"VISUAL DUPLICATES ({visualHasher.Type})");
            LogDuplicateGroups(visualDuplicates, metadata);
        }

        if (!errors.IsEmpty)
        {
            auditLogService.LogHeader("TECHNICAL ERRORS DURING SCAN");
            foreach (var error in errors)
            {
                auditLogService.LogLine($"FAIL: {error.Path} -> {error.Ex.Message}");
            }
        }

        auditLogService.LogLine("\n" + ConsoleHelper.TaskSeparator);
        auditLogService.Flush();
    }

    private void LogDuplicateGroups(
        List<List<string>> groups,
        ConcurrentDictionary<string, (long size, string resolution)> metadata
    )
    {
        if (groups.Count == 0)
        {
            auditLogService.LogLine("No duplicates identified.");
            return;
        }

        foreach (var group in groups)
        {
            auditLogService.LogLine($"\nGroup Set ({group.Count} files):");
            var fileDetails = group.Select(path =>
            {
                var (size, resolution) = metadata[path];
                return $"{path} [{FormatFileSize(size)} | {resolution}]";
            });

            auditLogService.LogBulletPoints(fileDetails, 1);
        }
    }

    private static string FormatFileSize(long bytes)
    {
        string[] sufix = { "B", "KB", "MB", "GB", "TB" };
        int i;
        double dblSByte = bytes;
        for (i = 0; i < sufix.Length && bytes >= 1024; i++, bytes /= 1024)
            dblSByte = bytes / 1024.0;

        return $"{dblSByte:0.##} {sufix[i]}";
    }

    private void LogError(string message, Exception ex)
    {
        auditLogService.LogError(message, ex);
        auditLogService.LogLine("\n" + ConsoleHelper.TaskSeparator + "\n");
        auditLogService.Flush();
    }
}
