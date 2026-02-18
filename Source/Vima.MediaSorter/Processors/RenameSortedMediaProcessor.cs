using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Vima.MediaSorter.Domain;
using Vima.MediaSorter.Services;
using Vima.MediaSorter.Services.MediaFileHandlers;
using Vima.MediaSorter.UI;

namespace Vima.MediaSorter.Processors;

public class RenameSortedMediaProcessor(
    IDirectoryIdentificationService directoryIdentifingService,
    IMediaIdentificationService mediaIdentifyingService,
    ITimeZoneAdjustmentService timeZoneAdjusterService,
    IRelatedFilesDiscoveryService relatedFileDiscoveryService,
    IEnumerable<IMediaFileHandler> mediaFileHandlers,
    IAuditLogService auditLogService,
    IOptions<MediaSorterOptions> options
) : IProcessor
{
    public ProcessorOptions Option => ProcessorOptions.RenameSortedMedia;

    public void Process()
    {
        string logPath = auditLogService.Initialise();

        try
        {
            OutputConfiguration();

            Console.WriteLine("[Step 1/2] Identification & planning");
            Console.WriteLine(ConsoleHelper.TaskSeparator);

            var directoryStructure = ConsoleHelper.ExecuteWithProgress(
                "Identifying directories",
                directoryIdentifingService.Identify
            );
            if (directoryStructure.SortedFolders.Count == 0)
            {
                Console.WriteLine("No sorted folders found to process.");
                return;
            }

            var foldersToScan = directoryStructure.SortedFolders
                .Concat(directoryStructure.SortedSubFolders)
                .ToList();
            var identified = ConsoleHelper.ExecuteWithProgress(
                "Identifying media files",
                p => mediaIdentifyingService.Identify(foldersToScan, p)
            );

            var associated = relatedFileDiscoveryService.AssociateRelatedFiles(
                identified.MediaFilesWithDates,
                identified.UnsupportedFiles
            );

            var timeZoneAdjustedFilePaths = timeZoneAdjusterService.ApplyOffsetsIfNeeded(
                identified.MediaFilesWithDates
            );

            var renamePlan = GenerateRenamePlan(identified.MediaFilesWithDates);

            OutputAnalysis(
                identified,
                associated,
                renamePlan,
                directoryStructure.SortedFolders.Count,
                timeZoneAdjustedFilePaths.Count,
                logPath
            );

            Console.WriteLine("[Step 2/2] Renaming");
            Console.WriteLine(ConsoleHelper.TaskSeparator);

            if (renamePlan.Count == 0)
            {
                Console.WriteLine("All files already match the naming convention.");
                return;
            }

            if (ConsoleHelper.AskYesNoQuestion(
                    $"Action: Rename {renamePlan.Count} file(s)?",
                    ConsoleKey.N) != ConsoleKey.Y
            )
            {
                Console.WriteLine("Result: Operation aborted.");
                return;
            }

            ExecuteRenaming(renamePlan);

            Console.WriteLine();
            Console.WriteLine($"Processing complete. Audit log: {logPath}");
        }
        catch (Exception ex)
        {
            LogError("A critical error occurred during processing.", ex);
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n[FATAL ERROR] {ex.Message}");
            Console.WriteLine($"Details logged to: {logPath}");
            Console.ResetColor();
        }
    }

    private List<(string Source, string Destination)> GenerateRenamePlan(
        IReadOnlyCollection<MediaFileWithDate> files
    )
    {
        var plan = new List<(string Source, string Destination)>();

        var folderGroups = files
            .GroupBy(f => Path.GetDirectoryName(f.FilePath))
            .OrderBy(g => g.Key);

        foreach (var folderGroup in folderGroups)
        {
            var secondGroups = folderGroup
                .GroupBy(f => f.CreatedOn.Date.ToString("yyyyMMdd_HHmmss"))
                .OrderBy(g => g.Key);

            foreach (var secondGroup in secondGroups)
            {
                var items = secondGroup.ToList();
                bool needsPrecision = items.Count > 1;

                if (needsPrecision)
                    items.ForEach(TryEnhanceMetadata);

                var sorted = items
                    .OrderBy(f => f.CreatedOn.Date.Ticks)
                    .ThenBy(f => Path.GetFileName(f.FilePath))
                    .ToList();

                DateTime lastAssignedTime = DateTime.MinValue;

                foreach (var media in sorted)
                {
                    DateTime targetTime = media.CreatedOn.Date;
                    string format = needsPrecision ? "yyyyMMdd_HHmmss_fff" : "yyyyMMdd_HHmmss";

                    if (
                        needsPrecision
                        && (
                            targetTime.ToString(format) == lastAssignedTime.ToString(format)
                            || targetTime < lastAssignedTime
                        )
                    )
                    {
                        targetTime = lastAssignedTime.AddMilliseconds(1);
                    }

                    lastAssignedTime = targetTime;
                    string newBaseName = targetTime.ToString(format);

                    var allPaths = new List<string> { media.FilePath }.Concat(media.RelatedFiles);
                    foreach (var oldPath in allPaths)
                    {
                        string ext = Path.GetExtension(oldPath);
                        string newFileName = newBaseName + ext;
                        string newPath = Path.Combine(folderGroup.Key!, newFileName);

                        if (string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
                            continue;

                        plan.Add((oldPath, newPath));
                    }
                }
            }
        }

        return plan;
    }

    private void ExecuteRenaming(List<(string Source, string Destination)> plan)
    {
        var moved = new List<(string Source, string Destination)>();
        var errors = new List<(string Source, string Destination, Exception Exception)>();

        ConsoleHelper.ExecuteWithProgress(
            "Status: Renaming",
            p =>
            {
                for (int i = 0; i < plan.Count; i++)
                {
                    var op = plan[i];
                    try
                    {
                        File.Move(op.Source, op.Destination);
                        moved.Add(op);
                    }
                    catch (Exception ex)
                    {
                        errors.Add((op.Source, op.Destination, ex));
                    }
                    p.Report((double)(i + 1) / plan.Count);
                }
                return true;
            }
        );

        LogExecutionResults(moved, errors);

        Console.WriteLine($"Result: {moved.Count} files renamed.");
        if (errors.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"(!) {errors.Count} error(s) encountered (see log for details).");

            foreach (var error in errors.Take(5))
                Console.WriteLine($"    - {Path.GetFileName(error.Source)}: {error.Exception.Message}");

            if (errors.Count > 5)
                Console.WriteLine("    - ... (see logs for more)");

            Console.ResetColor();
        }
    }

    private void LogExecutionResults(
        List<(string Source, string Destination)> moved,
        List<(string Source, string Destination, Exception Exception)> errors)
    {
        auditLogService.LogHeader("Execution results");

        if (moved.Count > 0)
        {
            auditLogService.LogLine("\n[Successfully renamed]");
            auditLogService.LogBulletPoints(
                moved.Select(m => $"RENAME: {m.Source} -> {Path.GetFileName(m.Destination)}"));
        }

        if (errors.Count > 0)
        {
            auditLogService.LogLine("\n[Errors - Failed to rename]");
            auditLogService.LogBulletPoints(
                errors.Select(e => $"FAIL:   {e.Source} -> Reason: {e.Exception.Message}"));
        }

        auditLogService.LogLine($"\nExecution Summary: {moved.Count} success, {errors.Count} errors.");
        auditLogService.LogLine("\n" + ConsoleHelper.TaskSeparator + "\n");
        auditLogService.Flush();
    }

    private void TryEnhanceMetadata(MediaFileWithDate file)
    {
        if (file.CreatedOn.Source != CreatedOnSource.FileName) return;

        var ext = Path.GetExtension(file.FilePath).ToLowerInvariant();
        var handler = mediaFileHandlers.FirstOrDefault(h => h.CanHandle(ext));
        var highPrecisionDate = handler?.GetCreatedOnDateFromMetadata(file.FilePath);
        if (highPrecisionDate != null)
            file.SetCreatedOn(highPrecisionDate);
    }

    private void OutputConfiguration()
    {
        var allExtensions = mediaFileHandlers
            .SelectMany(h => h.SupportedExtensions)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(e => e);

        var config = new[]
        {
            "Configuration:",
            $"  Directory:      {options.Value.Directory}",
            $"  Extensions:     {string.Join(", ", allExtensions)}",
            $"  Default format: yyyyMMdd_HHmmss",
            $"  Burst format:   yyyyMMdd_HHmmss_fff",
            "",
        };

        foreach (var line in config)
        {
            Console.WriteLine(line);
            auditLogService.LogLine(line);
        }
        auditLogService.Flush();
    }

    private void OutputAnalysis(
        IdentifiedMedia identified,
        AssociatedMedia associated,
        List<(string, string)> plan,
        int folderCount,
        int timeZoneAdjusted,
        string logPath
    )
    {
        var summary = new List<string>
        {
            $"",
            $"Analysis Result:",
            $"  Sorted folders:      {folderCount}",
            $"  Media identified:    {identified.MediaFilesWithDates.Count}",
            $"  Sidecars linked:     {identified.MediaFilesWithDates.Sum(f => f.RelatedFiles.Count)}",
            $"  Ignored/Unknown:     {associated.RemainingIgnoredFiles.Count}",
        };

        if (timeZoneAdjusted > 0)
            summary.Add($"  Time zone adjusted:  {timeZoneAdjusted}");

        if (identified.ErroredFiles.Count > 0)
            summary.Add($"  Errors:              {identified.ErroredFiles.Count} (see log)");

        summary.Add($"  Files to rename:     {plan.Count}");
        summary.Add($"  Audit log:           {logPath}");
        summary.Add("");

        foreach (var line in summary)
        {
            Console.WriteLine(line);
            auditLogService.LogLine(line);
        }

        if (identified.ErroredFiles.Any())
        {
            auditLogService.LogLine("\n[Technical Errors - Identification Failed]");
            auditLogService.LogBulletPoints(
                identified.ErroredFiles.Select(err => $"ERROR: {err.FilePath} (Exception: {err.Exception.Message})"));
            auditLogService.LogLine($"\n{ConsoleHelper.TaskSeparator}");
        }

        if (plan.Count != 0)
        {
            LogRenamePlan(plan);
        }

        auditLogService.Flush();
    }

    private void LogRenamePlan(IReadOnlyList<(string Source, string Destination)> plan)
    {
        auditLogService.LogHeader("Proposed rename plan");

        var folderGroups = plan.GroupBy(p => Path.GetDirectoryName(p.Source)).OrderBy(g => g.Key);
        foreach (var folderGroup in folderGroups)
        {
            auditLogService.LogLine($"\nFolder: {folderGroup.Key}");
            auditLogService.LogLine(ConsoleHelper.TaskSeparator);

            foreach (var (Source, Destination) in folderGroup)
            {
                auditLogService.LogLine(
                    $"  {Path.GetFileName(Source)} -> {Path.GetFileName(Destination)}"
                );
            }
        }

        auditLogService.LogLine("\n" + ConsoleHelper.TaskSeparator + "\n");
    }

    private void LogError(string message, Exception ex)
    {
        auditLogService.LogError(message, ex);
        auditLogService.LogLine($"\n{ConsoleHelper.TaskSeparator}\n");
        auditLogService.Flush();
    }
}
