using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vima.MediaSorter.Domain;
using Vima.MediaSorter.Infrastructure;
using Vima.MediaSorter.Services;

namespace Vima.MediaSorter.Processors.CleanupRawMedia;

public class CleanupRawMediaProcessor(
    IDirectoryIdentificationService directoryIdentificationService,
    IFileRemovingService fileRemovingService,
    ICleanupRawMediaReporter cleanupRawMediaReporter,
    IFileSystem fileSystem,
    IOutputService outputService
) : IProcessor
{
    public ProcessorOptions Option => ProcessorOptions.CleanupRawMedia;

    public HashSet<string> rawExtensions = new(StringComparer.OrdinalIgnoreCase) { ".cr3" };

    public async Task Process(CancellationToken token = default)
    {
        outputService.Start("Cleanup orphaned RAW files");

        try
        {
            cleanupRawMediaReporter.ReportConfiguration(rawExtensions);

            outputService.Section("[Step 1/2] Identification", OutputLevel.Info);

            var structure = outputService.ExecuteWithProgress(
                "Scanning directories",
                directoryIdentificationService.Identify
            );

            var deletionPlan = GenerateDeletionPlan(structure.SortedFolders);

            cleanupRawMediaReporter.ReportAnalysisResults(structure, deletionPlan);

            outputService.Section("[Step 2/2] Deletion", OutputLevel.Info);

            int totalOrphanedCount = deletionPlan.Sum(p => p.Value.Count);
            if (totalOrphanedCount == 0)
            {
                outputService.Complete("Everything is in sync. No RAW files need to be deleted.");
                return;
            }

            if (
                !outputService.Confirm(
                    $"Action: Delete {totalOrphanedCount} orphaned RAW file(s)?",
                    OutputLevel.Info
                )
            )
            {
                outputService.Complete("  Operation aborted.");
                return;
            }

            var result = outputService.ExecuteWithProgress(
                "  Deleting orphaned files",
                p => fileRemovingService.DeleteFiles(deletionPlan.SelectMany(f => f.Value), p)
            );
            cleanupRawMediaReporter.ReportDeletionResults(result.DeletedFiles, result.ErroredFiles);

            outputService.Complete();
        }
        catch (Exception ex)
        {
            outputService.Fatal("Critical failure in RAW cleanup", ex);
        }
    }

    private Dictionary<string, List<string>> GenerateDeletionPlan(IList<string> sortedFolders)
    {
        var plan = new Dictionary<string, List<string>>();

        foreach (var mainFolder in sortedFolders)
        {
            var rawFolderPath = Path.Combine(mainFolder, MediaSorterConstants.RawFolderName);

            if (!fileSystem.DirectoryExists(rawFolderPath))
                continue;

            var curatedBaseNames = fileSystem
                .EnumerateFiles(mainFolder)
                .Select(Path.GetFileNameWithoutExtension)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var orphanedRaws = fileSystem
                .EnumerateFiles(rawFolderPath)
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
}
