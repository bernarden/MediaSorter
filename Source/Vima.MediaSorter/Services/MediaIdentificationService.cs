using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vima.MediaSorter.Domain;
using Vima.MediaSorter.Services.MediaFileHandlers;

namespace Vima.MediaSorter.Services;

public interface IMediaIdentificationService
{
    IdentifiedMedia Identify(IEnumerable<string> directoriesToScan, IProgress<double>? progress = null);
}

public class MediaIdentificationService(IEnumerable<IMediaFileHandler> mediaFileHandlers) : IMediaIdentificationService
{
    public IdentifiedMedia Identify(IEnumerable<string> directoriesToScan, IProgress<double>? progress = null)
    {
        List<string> filePaths = [.. directoriesToScan.SelectMany(Directory.EnumerateFiles)];
        if (filePaths.Count == 0)
        {
            progress?.Report(1.0);
            return new IdentifiedMedia();
        }

        int processedFileCounter = 0;
        ConcurrentBag<MediaFileWithDate> mediaFilesWithDates = new();
        ConcurrentBag<MediaFile> mediaFilesWithoutDates = new();
        ConcurrentBag<string> unsupportedFiles = new();
        Parallel.ForEach(filePaths, new() { MaxDegreeOfParallelism = 25 }, filePath =>
        {
            ProcessFilePath(filePath, mediaFilesWithDates, mediaFilesWithoutDates, unsupportedFiles);
            Interlocked.Increment(ref processedFileCounter);
            progress?.Report((double)processedFileCounter / filePaths.Count);
        });

        return new IdentifiedMedia
        {
            MediaFilesWithDates = [.. mediaFilesWithDates],
            MediaFilesWithoutDates = [.. mediaFilesWithoutDates],
            UnsupportedFiles = [.. unsupportedFiles]
        };
    }

    private void ProcessFilePath(
        string filePath,
        ConcurrentBag<MediaFileWithDate> mediaFilesWithDates,
        ConcurrentBag<MediaFile> mediaFilesWithoutDates,
        ConcurrentBag<string> unsupportedFiles)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        var handler = mediaFileHandlers.FirstOrDefault(h => h.CanHandle(ext));
        if (handler == null)
        {
            unsupportedFiles.Add(filePath);
            return;
        }

        var mediaFile = handler.Handle(filePath);
        if (mediaFile is MediaFileWithDate datedFile)
        {
            mediaFilesWithDates.Add(datedFile);
        }
        else
        {
            mediaFilesWithoutDates.Add(mediaFile);
        }
    }
}