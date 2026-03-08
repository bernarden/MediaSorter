using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Options;
using Vima.MediaSorter.Domain;
using Vima.MediaSorter.Infrastructure;
using Vima.MediaSorter.Services;

namespace Vima.MediaSorter.Processors;

public class CleanupRawMediaProcessor(
    IDirectoryIdentificationService directoryIdentificationService,
    IFileSystem fileSystem,
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

            outputService.Section("[Step 1/2] Identification");

            var structure = outputService.ExecuteWithProgress(
                "Scanning directories",
                directoryIdentificationService.Identify
            );

            var deletionPlan = GenerateDeletionPlan(structure.SortedFolders);

            ReportAnalysisResults(structure, deletionPlan);

            outputService.Section("[Step 2/2] Deletion");

            int totalOrphanedCount = deletionPlan.Sum(p => p.Value.Count);
            if (totalOrphanedCount == 0)
            {
                outputService.Complete("Everything is in sync. No RAW files need to be deleted.");
                return;
            }

            if (
                !outputService.Confirm($"Action: Delete {totalOrphanedCount} orphaned RAW file(s)?")
            )
            {
                outputService.Complete("  Operation aborted.");
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
            outputService.Subsection("Proposed deletion plan", OutputLevel.Debug);
            foreach (var entry in plan.OrderBy(e => e.Key))
            {
                var files = entry
                    .Value.OrderByPath(x => x)
                    .Select(x => fileSystem.GetRelativePath(x, entry.Key));
                outputService.List(
                    $"Folder {fileSystem.GetRelativePath(entry.Key)}:",
                    files,
                    OutputLevel.Debug
                );
                outputService.WriteLine(string.Empty, OutputLevel.Debug);
            }
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
                            fileSystem.DeleteFile(filePath);
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

        if (deleteDetails.Count > 0)
        {
            outputService.Subsection("Deleted", OutputLevel.Debug);
            outputService.List(
                string.Empty,
                deleteDetails.OrderByPath(x => x).Select(x => fileSystem.GetRelativePath(x)),
                OutputLevel.Debug
            );
            outputService.WriteLine(string.Empty, OutputLevel.Debug);
        }

        if (deletionErrors.Count > 0)
        {
            var errorsOutput = deletionErrors
                .OrderByPath(x => x.FilePath)
                .Select(error =>
                    $"{fileSystem.GetRelativePath(error.FilePath)}: {error.ExceptionMessage}"
                );
            outputService.List("Deletion failures:", errorsOutput, OutputLevel.Error, 5);
            outputService.WriteLine(string.Empty, OutputLevel.Error);
        }
    }
}
