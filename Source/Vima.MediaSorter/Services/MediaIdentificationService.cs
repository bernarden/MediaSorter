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
        ConcurrentBag<MediaFile> mediaFiles = new();
        ConcurrentBag<string> ignoredFiles = new();
        Parallel.ForEach(filePaths, new() { MaxDegreeOfParallelism = 25 }, filePath =>
        {
            ProcessFilePath(filePath, mediaFiles, ignoredFiles);
            Interlocked.Increment(ref processedFileCounter);
            progress?.Report((double)processedFileCounter / filePaths.Count);
        });

        return new IdentifiedMedia
        {
            MediaFiles = [.. mediaFiles],
            IgnoredFiles = [.. ignoredFiles]
        };
    }

    private void ProcessFilePath(string filePath, ConcurrentBag<MediaFile> mediaFiles, ConcurrentBag<string> ignoredFiles)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        var handler = mediaFileHandlers.FirstOrDefault(h => h.CanHandle(ext));
        if (handler != null)
        {
            var mediaFile = handler.Handle(filePath);
            mediaFiles.Add(mediaFile);
        }
        else
        {
            ignoredFiles.Add(filePath);
        }
    }
}