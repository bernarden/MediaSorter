using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vima.MediaSorter.Domain;
using Vima.MediaSorter.Services.MediaFileHandlers;
using Vima.MediaSorter.UI;

namespace Vima.MediaSorter.Services;

public interface IMediaIdentificationService
{
    IdentifiedMedia Identify(IEnumerable<string> directoriesToScan);
}

public class MediaIdentificationService(IEnumerable<IMediaFileHandler> mediaFileHandlers) : IMediaIdentificationService
{
    public IdentifiedMedia Identify(IEnumerable<string> directoriesToScan)
    {
        Console.Write("Identifying your media... ");
        using var progress = new ProgressBar();
        int processedFileCounter = 0;
        ConcurrentBag<MediaFile> mediaFiles = new();
        ConcurrentBag<string> ignoredFiles = new();
        List<string> filePaths = [.. directoriesToScan.SelectMany(d => Directory.EnumerateFiles(d))];
        Parallel.ForEach(filePaths, new() { MaxDegreeOfParallelism = 25 }, filePath =>
        {
            ProcessFilePath(filePath, mediaFiles, ignoredFiles);
            Interlocked.Increment(ref processedFileCounter);
            progress.Report((double)processedFileCounter / filePaths.Count);
        });

        progress.Dispose();
        Console.WriteLine("Done.");

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