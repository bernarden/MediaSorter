using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Vima.MediaSorter.Domain;
using Vima.MediaSorter.Infrastructure;
using Vima.MediaSorter.Services;

namespace Vima.MediaSorter.Processors;

public class CleanupRawMediaProcessor(
    IDirectoryIdentificationService directoryIdentificationService,
    IOutputService outputService,
    IOptions<MediaSorterOptions> options
) : IProcessor
{
    public ProcessorOptions Option => ProcessorOptions.CleanupRawMedia;

    public HashSet<string> rawExtensions = new(StringComparer.OrdinalIgnoreCase) { ".cr3" };

    public void Process()
    {
        outputService.Start("Cleanup orphaned RAW files");

        try
        {
            OutputConfiguration();

            outputService.Header("[Step 1/2] Identification");

            var structure = outputService.ExecuteWithProgress(
                "Scanning directories",
                directoryIdentificationService.Identify
            );

            var deletionPlan = GenerateDeletionPlan(structure.SortedFolders);

            ReportAnalysisResults(structure, deletionPlan);

            outputService.Header("[Step 2/2] Deletion");

            int totalOrphanedCount = deletionPlan.Sum(p => p.Value.Count);
            if (totalOrphanedCount == 0)
            {
                outputService.WriteLine(
                    "  Everything is in sync. No RAW files need to be deleted."
                );
                outputService.WriteLine();
                outputService.WriteLine(MediaSorterConstants.Separator);
                outputService.WriteLine();
                return;
            }

            if (
                !outputService.Confirm($"Action: Delete {totalOrphanedCount} orphaned RAW file(s)?")
            )
            {
                outputService.WriteLine("  Operation aborted.");
                outputService.WriteLine();
                outputService.WriteLine(MediaSorterConstants.Separator);
                outputService.WriteLine();
                return;
            }

            ExecuteDeletion(deletionPlan, totalOrphanedCount);

            outputService.Complete();
        }
        catch (Exception ex)
        {
            outputService.Fatal("Critical failure in RAW cleanup", ex);
        }
    }

    private void OutputConfiguration()
    {
        outputService.Table(
            "Configuration:",
            [
                new("Directory:", options.Value.Directory),
                new("Log file:", outputService.LogFileName),
                new("Raw folder:", MediaSorterConstants.RawFolderName),
                new("Raw extensions:", string.Join(", ", rawExtensions)),
            ]
        );
    }

    private void ReportAnalysisResults(
        DirectoryStructure structure,
        Dictionary<string, List<string>> plan
    )
    {
        int orphanedCount = plan.Sum(p => p.Value.Count);
        outputService.Table(
            "Analysis result:",
            [
                new("Folders checked:", structure.SortedFolders.Count.ToString()),
                new("Orphaned RAWs:", orphanedCount.ToString()),
            ]
        );

        if (plan.Count != 0)
        {
            outputService.Header("Proposed deletion plan", OutputLevel.Debug);
            foreach (var entry in plan.OrderBy(e => e.Key))
            {
                outputService.WriteLine($"Folder: {GetRelativePath(entry.Key)}", OutputLevel.Debug);
                foreach (var file in entry.Value.OrderByPath(x => x))
                {
                    outputService.WriteLine(
                        $"  {Path.GetRelativePath(entry.Key, file)}",
                        OutputLevel.Debug
                    );
                }
                outputService.WriteLine("", OutputLevel.Debug);
            }
        }
    }

    private Dictionary<string, List<string>> GenerateDeletionPlan(IList<string> sortedFolders)
    {
        var plan = new Dictionary<string, List<string>>();

        foreach (var mainFolder in sortedFolders)
        {
            var rawFolderPath = Path.Combine(mainFolder, MediaSorterConstants.RawFolderName);

            if (!Directory.Exists(rawFolderPath))
                continue;

            var curatedBaseNames = Directory
                .GetFiles(mainFolder)
                .Select(Path.GetFileNameWithoutExtension)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var orphanedRaws = Directory
                .GetFiles(rawFolderPath)
                .Where(f => rawExtensions.Contains(Path.GetExtension(f)))
                .Where(f => !curatedBaseNames.Contains(Path.GetFileNameWithoutExtension(f)))
                .ToList();

            if (orphanedRaws.Count != 0)
            {
                plan.Add(rawFolderPath, orphanedRaws);
            }
        }

        return plan;
    }

    private void ExecuteDeletion(Dictionary<string, List<string>> plan, int totalFiles)
    {
        int deleted = 0;
        int errors = 0;
        List<(string FilePath, string ExceptionMessage)> deletionErrors = new();
        List<string> deleteDetails = new();

        outputService.ExecuteWithProgress(
            "  Deleting orphaned files",
            p =>
            {
                foreach (var folderEntry in plan)
                {
                    foreach (var filePath in folderEntry.Value)
                    {
                        try
                        {
                            File.Delete(filePath);
                            deleteDetails.Add(filePath);
                            deleted++;
                        }
                        catch (Exception ex)
                        {
                            deletionErrors.Add((filePath, ex.Message));
                            errors++;
                        }
                        p.Report((double)deleted / totalFiles);
                    }
                }
                return true;
            }
        );

        outputService.WriteLine($"  Result: Deleted {deleted} file(s).");
        outputService.WriteLine();

        if (errors > 0)
        {
            outputService.List(
                "Deleted",
                deleteDetails.OrderByPath(x => x).Select(x => GetRelativePath(x)),
                OutputLevel.Debug
            );
            outputService.WriteLine("", OutputLevel.Debug);
        }

        if (errors > 0)
        {
            outputService.List(
                "Deletion failures:",
                deletionErrors
                    .OrderByPath(x => x.FilePath)
                    .Select(error =>
                        $"{GetRelativePath(error.FilePath)}: {error.ExceptionMessage}"
                    ),
                OutputLevel.Error
            );
            outputService.WriteLine("", OutputLevel.Error);
        }
    }

    private string GetRelativePath(string? path)
    {
        if (path == null)
            return string.Empty;

        return Path.GetRelativePath(options.Value.Directory, path);
    }
}
