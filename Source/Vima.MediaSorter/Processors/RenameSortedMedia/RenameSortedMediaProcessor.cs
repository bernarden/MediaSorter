using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vima.MediaSorter.Domain;
using Vima.MediaSorter.Services;

namespace Vima.MediaSorter.Processors.RenameSortedMedia;

public class RenameSortedMediaProcessor(
    IDirectoryIdentificationService directoryIdentifingService,
    IMediaIdentificationService mediaIdentifyingService,
    ITimeZoneAdjustmentService timeZoneAdjusterService,
    IRelatedFilesDiscoveryService relatedFileDiscoveryService,
    IRenameSortedMediaPlanService renameSortedMediaPlanService,
    IRenameSortedMediaReporter renameSortedMediaReporter,
    IOutputService outputService
) : IProcessor
{
    public ProcessorOptions Option => ProcessorOptions.RenameSortedMedia;

    public async Task Process(CancellationToken token = default)
    {
        outputService.Start("Rename media in sorted folders");

        try
        {
            renameSortedMediaReporter.ReportConfiguration();

            outputService.Section("[Step 1/2] Identification", OutputLevel.Info);

            var directoryStructure = outputService.ExecuteWithProgress(
                "Identifying directories",
                directoryIdentifingService.Identify
            );
            if (directoryStructure.SortedFolders.Count == 0)
            {
                outputService.Complete("No sorted folders found to process.");
                return;
            }

            var foldersToScan = directoryStructure.SortedFolders
                .Concat(directoryStructure.SortedSubFolders)
                .ToList();
            var identified = outputService.ExecuteWithProgress(
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

            var renamePlan = renameSortedMediaPlanService.GenerateRenamePlan(identified.MediaFilesWithDates);

            renameSortedMediaReporter.ReportAnalysisResults(
                identified,
                associated,
                renamePlan,
                directoryStructure.SortedFolders.Count,
                timeZoneAdjustedFilePaths.Count
            );

            outputService.Section("[Step 2/2] Renaming", OutputLevel.Info);

            if (renamePlan.Count == 0)
            {
                outputService.Complete("All files already match the naming convention.");
                return;
            }

            if (!outputService.Confirm($"Action: Rename {renamePlan.Count} file(s)?", OutputLevel.Info))
            {
                outputService.Complete("  Operation aborted.");
                return;
            }

            renameSortedMediaPlanService.Rename(renamePlan);

            outputService.Complete();
        }
        catch (Exception ex)
        {
            outputService.Fatal("A critical error occurred during processing.", ex);
        }
    }
}