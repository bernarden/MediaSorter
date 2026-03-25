using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Options;
using Vima.MediaSorter.Domain;
using Vima.MediaSorter.Infrastructure;
using Vima.MediaSorter.Services;
using Vima.MediaSorter.Services.Hashers;

namespace Vima.MediaSorter.Processors.FindDuplicates;

public interface IFindDuplicatesReporter
{
    void ReportConfiguration();
    void ReportSummary(
        int totalScanned,
        TimeSpan duration,
        int totalSets,
        int totalFiles,
        int errors
    );
    void ReportAnalysisResults(
        IEnumerable<string> allFiles,
        IEnumerable<IEnumerable<string>> allDuplicates,
        IEnumerable<IEnumerable<string>> exactDuplicates,
        IEnumerable<IEnumerable<string>> visualDuplicates,
        IDictionary<string, FindDuplicatesFile> hashes,
        IEnumerable<(string Path, Exception Ex)> errors,
        IVisualFileHasher? visualHasher,
        TimeSpan duration
    );
}

public class FindDuplicatesReporter(
    IOutputService outputService,
    IFileHasher fileHasher,
    IFileSystem fileSystem,
    IOptions<MediaSorterOptions> options
) : IFindDuplicatesReporter
{
    public void ReportConfiguration()
    {
        outputService.Table(
            "Configuration:",
            [
                new("Directory:", options.Value.Directory),
                new("Log file:", outputService.LogFileName),
                new("Binary hasher:", fileHasher.GetType().Name),
            ],
            OutputLevel.Info
        );
    }

    public void ReportSummary(
        int totalScanned,
        TimeSpan duration,
        int totalSets,
        int totalFiles,
        int errors
    )
    {
        outputService.Table(
            "Analysis result:",
            [
                new("Total scanned:", totalScanned.ToString()),
                new("Detection time:", duration.ToString(@"hh\:mm\:ss\.ff")),
                new("Duplicate sets:", totalSets.ToString()),
                new("Duplicate files:", totalFiles.ToString()),
                new("Errors:", errors.ToString(), errors > 0),
            ],
            OutputLevel.Info
        );
    }

    public void ReportAnalysisResults(
        IEnumerable<string> allFiles,
        IEnumerable<IEnumerable<string>> allDuplicates,
        IEnumerable<IEnumerable<string>> exactDuplicates,
        IEnumerable<IEnumerable<string>> visualDuplicates,
        IDictionary<string, FindDuplicatesFile> hashes,
        IEnumerable<(string Path, Exception Ex)> errors,
        IVisualFileHasher? visualHasher,
        TimeSpan duration
    )
    {
        int duplicateSets = allDuplicates.Count();
        int exactRedundant = exactDuplicates.Sum(d => d.Count() - 1);
        int visualRedundant = visualDuplicates.Sum(d => d.Count() - 1);

        outputService.Table(
            "Analysis result:",
            [
                new("Total scanned:", allFiles.Count().ToString()),
                new("Detection time:", duration.ToString(@"hh\:mm\:ss\.ff")),
                new("Duplicate sets:", duplicateSets.ToString()),
                new("Duplicate files:", (exactRedundant + visualRedundant).ToString()),
                new("  Exact:", exactRedundant.ToString()),
                new("  Visual:", visualRedundant.ToString()),
                new("Errors:", errors.Count().ToString(), errors.Any()),
            ],
            OutputLevel.Info
        );

        LogDuplicateGroups("Exact duplicates (byte-for-byte)", exactDuplicates, hashes);

        if (visualHasher != null)
        {
            LogDuplicateGroups(
                $"Visual duplicates ({visualHasher.Type})",
                visualDuplicates,
                hashes
            );
        }

        if (errors.Any())
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
        IEnumerable<IEnumerable<string>> groups,
        IDictionary<string, FindDuplicatesFile> hashes
    )
    {
        if (!groups.Any())
            return;

        outputService.Subsection(title, OutputLevel.Debug);
        foreach (var group in groups)
        {
            var fileDetails = group
                .OrderByDescending(path => hashes[path].Width * hashes[path].Height)
                .ThenByDescending(path => hashes[path].Size)
                .Select(path =>
                {
                    string resolution =
                        hashes[path].Width > 0
                            ? $"{hashes[path].Width}x{hashes[path].Height}"
                            : "N/A";
                    return $"{fileSystem.GetRelativePath(path)} [{resolution} | {hashes[path].Size.FormatFileSize()}]";
                });

            outputService.List(
                $"Group set ({group.Count()} files):",
                fileDetails,
                OutputLevel.Debug
            );
            outputService.WriteLine(string.Empty, OutputLevel.Debug);
        }
    }
}
