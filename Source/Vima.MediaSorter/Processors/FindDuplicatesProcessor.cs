using Microsoft.Extensions.Options;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vima.MediaSorter.Domain;
using Vima.MediaSorter.Infrastructure;
using Vima.MediaSorter.Services;
using Vima.MediaSorter.UI;

namespace Vima.MediaSorter.Processors;

public class FindDuplicatesProcessor(
    IDuplicateDetector duplicateDetector,
    IAuditLogService auditLogService,
    IOptions<MediaSorterOptions> options
) : IProcessor
{
    public ProcessorOptions Option => ProcessorOptions.FindDuplicates;

    public void Process()
    {
        string logPath = auditLogService.Initialise();

        try
        {
            Console.WriteLine("[Step 1/1] Duplicate Detection");
            Console.WriteLine(ConsoleHelper.TaskSeparator);

            ConcurrentBag<(string Path, Exception Ex)> errors = new();
            ConcurrentDictionary<string, string> hashes = new();

            var (duplicates, totalFilesChecked) = ConsoleHelper.ExecuteWithProgress(
                "Scanning for duplicates",
                p =>
                {
                    var allFiles = Directory
                        .EnumerateFiles(options.Value.Directory, "*", SearchOption.AllDirectories)
                        .ToList();

                    var sizeGroups = allFiles
                        .Select(f => new FileInfo(f))
                        .GroupBy(f => f.Length)
                        .Where(g => g.Count() > 1)
                        .ToList();

                    int processedSizeGroupCounter = 0;
                    ConcurrentBag<List<string>> result = new();
                    Parallel.ForEach(
                        sizeGroups,
                        new() { MaxDegreeOfParallelism = 25 },
                        sizeGroup =>
                        {
                            List<string> pathsInSizeGroup = sizeGroup
                                .Select(f => f.FullName)
                                .ToList();
                            var duplicateSets = pathsInSizeGroup
                                .GroupBy(path => GetHashSafely(path, hashes, errors))
                                .Where(g => g.Count() > 1);

                            foreach (var duplicateSet in duplicateSets)
                            {
                                result.Add([.. duplicateSet]);
                            }

                            Interlocked.Increment(ref processedSizeGroupCounter);
                            p.Report((double)processedSizeGroupCounter / sizeGroups.Count);
                        }
                    );

                    return (result.ToList(), allFiles.Count);
                }
            );

            LogDuplicates(duplicates, errors);

            Console.WriteLine();
            Console.WriteLine("Analysis Result:");
            int duplicateSetsCount = duplicates.Count;
            int totalFilesInSets = duplicates.Sum(d => d.Count);
            int redundantFileCount = totalFilesInSets - duplicateSetsCount;

            Console.WriteLine($"  Files scanned:   {totalFilesChecked}");
            Console.WriteLine($"  Duplicate sets:  {duplicateSetsCount}");
            Console.WriteLine($"  Redundant files: {redundantFileCount} (safe to delete)");

            if (!errors.IsEmpty)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  Errors:         {errors.Count} files (skipped)");
                Console.ResetColor();
            }

            if (!errors.IsEmpty)
            {
                Console.WriteLine();
                Console.WriteLine("(!) Discovery Alerts:");
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(
                    $"    - {errors.Count} file(s) could not be read due to permissions or locks:"
                );
                foreach (var error in errors.Take(5))
                    Console.WriteLine(
                        $"      - {Path.GetFileName(error.Path)}: {error.Ex.Message}"
                    );

                if (errors.Count > 5)
                    Console.WriteLine("      - ... (see logs for more)");
                Console.ResetColor();
            }

            Console.WriteLine();
            Console.WriteLine(ConsoleHelper.TaskSeparator);
            Console.WriteLine($"Processing complete. Audit Log: {logPath}");
        }
        catch (Exception ex)
        {
            LogError("A critical error occurred during duplicate scanning.", ex);
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n[FATAL ERROR] {ex.Message}");
            Console.WriteLine($"Details logged to: {logPath}");
            Console.ResetColor();
        }
    }

    private string GetHashSafely(
        string path,
        ConcurrentDictionary<string, string> cache,
        ConcurrentBag<(string Path, Exception Ex)> errors
    )
    {
        try
        {
            return cache.GetOrAdd(path, p => duplicateDetector.GetFileHash(p));
        }
        catch (Exception ex)
        {
            errors.Add((path, ex));
            return $"ERROR-{Guid.NewGuid()}";
        }
    }

    private void LogDuplicates(
        List<List<string>> duplicates,
        ConcurrentBag<(string Path, Exception Ex)> errors
    )
    {
        auditLogService.WriteHeader("DUPLICATE SCAN RESULTS");
        if (duplicates.Count == 0)
        {
            auditLogService.WriteLine("No duplicates found.");
        }
        else
        {
            foreach (var set in duplicates)
            {
                auditLogService.WriteLine($"\nSet ({set.Count} files):");
                auditLogService.WriteBulletPoints(set, indentation: 1);
            }
        }

        if (errors.Count != 0)
        {
            auditLogService.WriteHeader("TECHNICAL ERRORS DURING SCAN");
            foreach (var error in errors)
            {
                auditLogService.WriteLine($"ERROR: {error.Path}");
                auditLogService.WriteLine($"       Reason: {error.Ex.Message}");
            }
        }

        auditLogService.WriteLine("\n" + ConsoleHelper.TaskSeparator);
    }

    private void LogError(string message, Exception ex)
    {
        auditLogService.WriteError(message, ex);
        auditLogService.WriteLine("\n" + ConsoleHelper.TaskSeparator + "\n");
    }
}
