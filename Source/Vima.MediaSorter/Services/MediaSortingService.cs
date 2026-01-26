using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Vima.MediaSorter.Domain;
using Vima.MediaSorter.Infrastructure;

namespace Vima.MediaSorter.Services;

public interface IMediaSortingService
{
    SortedMedia Sort(
        IReadOnlyCollection<MediaFileWithDate> files,
        IDictionary<DateTime, string> dateToExistingDirectoryMapping,
        IProgress<double>? progress = null
    );
}

public class MediaSortingService(
    IFileMover fileMover,
    IDirectoryResolver directoryResolver) : IMediaSortingService
{
    public SortedMedia Sort(
        IReadOnlyCollection<MediaFileWithDate> files,
        IDictionary<DateTime, string> dateToExistingDirectoryMapping,
        IProgress<double>? progress = null)
    {
        var successBag = new ConcurrentBag<SuccessfulFileMove>();
        var duplicateBag = new ConcurrentBag<DuplicateDetectedFileMove>();
        var errorBag = new ConcurrentBag<ErroredFileMove>();

        int processedCounter = 0;

        Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = 25 }, file =>
        {
            var targetFolder = directoryResolver.GetTargetFolderPath(
                file.CreatedOn.Date, file.TargetSubFolder, dateToExistingDirectoryMapping);

            foreach (var path in (string[])[file.FilePath, .. file.RelatedFiles])
            {
                try
                {
                    FileMove moveResult = fileMover.Move(path, targetFolder, Path.GetFileName(path));
                    switch (moveResult)
                    {
                        case SuccessfulFileMove success:
                            successBag.Add(success);
                            break;
                        case DuplicateDetectedFileMove duplicate:
                            duplicateBag.Add(duplicate);
                            break;
                        case ErroredFileMove error:
                            errorBag.Add(error);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    errorBag.Add(new ErroredFileMove(path, targetFolder, ex));
                }
            }

            Interlocked.Increment(ref processedCounter);
            progress?.Report((double)processedCounter / files.Count);
        });

        return new SortedMedia
        {
            Moved = [.. successBag],
            Duplicates = [.. duplicateBag],
            Errors = [.. errorBag],
        };
    }
}