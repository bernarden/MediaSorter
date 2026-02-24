using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Vima.MediaSorter.Domain;
using Vima.MediaSorter.Services;
using Vima.MediaSorter.UI;

namespace Vima.MediaSorter.Processors;

public class CleanupRawMediaProcessor(
    IDirectoryIdentificationService directoryIdentificationService,
    IAuditLogService auditLogService
) : IProcessor
{
    public ProcessorOptions Option => ProcessorOptions.CleanupRawMedia;

    public void Process()
    {
        string logPath = auditLogService.Initialise();

        try
        {
            Console.WriteLine("[Step 1/2] Analyzing RAW vs. JPEG synchronisation");
            Console.WriteLine(ConsoleHelper.TaskSeparator);

            var structure = ConsoleHelper.ExecuteWithProgress(
                "Scanning directories",
                directoryIdentificationService.Identify
            );

            var deletionPlan = GenerateDeletionPlan(structure.SortedFolders);
            int totalOrphanedCount = deletionPlan.Sum(p => p.Value.Count);

            OutputAnalysis(structure.SortedFolders.Count, totalOrphanedCount, deletionPlan, logPath);

            Console.WriteLine();
            Console.WriteLine(ConsoleHelper.TaskSeparator);
            Console.WriteLine("[Step 2/2] Deleting orphaned files");
            Console.WriteLine(ConsoleHelper.TaskSeparator);

            if (totalOrphanedCount == 0)
            {
                Console.WriteLine("Everything is in sync. No RAW files need to be deleted.");
                return;
            }

            if (ConsoleHelper.AskYesNoQuestion(
                $"Action: Delete {totalOrphanedCount} orphaned RAW file(s)?",
                ConsoleKey.N) != ConsoleKey.Y)
            {
                Console.WriteLine("Result: Operation aborted.");
                return;
            }

            ExecuteDeletion(deletionPlan, totalOrphanedCount);

            Console.WriteLine($"\nProcessing complete. Audit log: {logPath}");
        }
        catch (Exception ex)
        {
            auditLogService.LogError("Critical failure in RAW cleanup", ex);
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n[FATAL ERROR] {ex.Message}");
            Console.ResetColor();
        }
    }

    private static Dictionary<string, List<string>> GenerateDeletionPlan(IList<string> sortedFolders)
    {
        var plan = new Dictionary<string, List<string>>();
        var rawExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".cr3" };

        foreach (var mainFolder in sortedFolders)
        {
            var rawFolderPath = Path.Combine(mainFolder, MediaSorterConstants.RawFolderName);

            if (!Directory.Exists(rawFolderPath))
                continue;

            var curatedBaseNames = Directory.GetFiles(mainFolder)
                .Select(Path.GetFileNameWithoutExtension)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var orphanedRaws = Directory.GetFiles(rawFolderPath)
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

        ConsoleHelper.ExecuteWithProgress("Status: Deleting", p =>
        {
            foreach (var folderEntry in plan)
            {
                foreach (var filePath in folderEntry.Value)
                {
                    try
                    {
                        File.Delete(filePath);
                        auditLogService.LogLine($"DELETE: {filePath}");
                        deleted++;
                    }
                    catch (Exception ex)
                    {
                        auditLogService.LogLine($"ERROR: Could not delete {filePath} - {ex.Message}");
                        errors++;
                    }
                    p.Report((double)deleted / totalFiles);
                }
            }
            return true;
        });

        auditLogService.LogLine($"\nSummary: {deleted} deleted successfully, {errors} errors.");
        auditLogService.Flush();
        Console.WriteLine($"Result: {deleted} files removed.");
    }

    private void OutputAnalysis(int sortedCount, int orphanedCount, Dictionary<string, List<string>> plan, string logPath)
    {
        Console.WriteLine("\nAnalysis Result:");
        Console.WriteLine($"  Sorted folders checked:  {sortedCount}");
        Console.WriteLine($"  Orphaned RAWs found:     {orphanedCount}");
        Console.WriteLine($"  Audit log:               {Path.GetFileName(logPath)}");
        Console.WriteLine();

        if (plan.Count != 0)
        {
            auditLogService.LogHeader("Proposed Deletion Plan (Grouped by Folder)");

            foreach (var entry in plan.OrderBy(e => e.Key))
            {
                auditLogService.LogLine($"\nFolder: {entry.Key}");
                auditLogService.LogLine(new string('-', 50));
                auditLogService.LogBulletPoints(entry.Value.Select(Path.GetFileName)!);
            }

            auditLogService.LogLine($"\nTotal files to be removed: {orphanedCount}");
            auditLogService.LogLine($"\n{new string('=', 50)}\n");
            auditLogService.Flush();
        }
    }
}