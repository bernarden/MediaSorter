using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Vima.MediaSorter.Domain;
using Vima.MediaSorter.Infrastructure;
using Vima.MediaSorter.Services;

namespace Vima.MediaSorter.Processors.FindDuplicates;

public interface IFindDuplicatesReviewService
{
    void ReviewInteractively(
        IReadOnlyList<IReadOnlyList<string>> groups,
        IDictionary<string, FindDuplicatesFile> hashes
    );
}

public class FindDuplicatesReviewService(IFileSystem fileSystem, IOutputService outputService)
    : IFindDuplicatesReviewService
{
    private const string MenuPrompt = "[V]iew files | [N]ext group | [Q]uit review: ";

    public void ReviewInteractively(
        IReadOnlyList<IReadOnlyList<string>> groups,
        IDictionary<string, FindDuplicatesFile> hashes
    )
    {
        string question = "Action: Interactively review duplicate groups one by one?";
        if (!outputService.Confirm(question, OutputLevel.Info))
        {
            outputService.Complete("  Operation aborted.");
            return;
        }

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
                    $"  - {relativePath} [{resolution} | {hashes[path].Size.FormatFileSize()}]",
                    OutputLevel.Info
                );
            }

            bool moveToNext = false;
            outputService.Write(MenuPrompt, OutputLevel.Info);
            while (!moveToNext)
            {
                var input = outputService.ReadKey(true);
                switch (input)
                {
                    case ConsoleKey.V:
                        OpenGroupFiles(sortedGroup);
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

    private void OpenGroupFiles(List<string> group)
    {
        bool errorOccurred = false;

        foreach (var path in group)
        {
            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
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
            outputService.Write(MenuPrompt, OutputLevel.Info);
        }
    }
}
