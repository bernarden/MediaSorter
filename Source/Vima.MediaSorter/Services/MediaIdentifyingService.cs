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

public interface IMediaIdentifyingService
{
    IdentifiedMedia Identify(IEnumerable<string> directoriesToScan);
}

public class MediaIdentifyingService(IEnumerable<IMediaFileHandler> mediaFileHandlers) : IMediaIdentifyingService
{
    public IdentifiedMedia Identify(IEnumerable<string> directoriesToScan)
    {
        Console.Write("Identifying your media... ");

        List<string> filePaths = directoriesToScan
            .SelectMany(d => Directory.EnumerateFiles(d))
            .ToList();

        ConcurrentBag<MediaFile> mediaFiles = new();
        ConcurrentBag<string> ignoredFiles = new();
        using var progress = new ProgressBar();
        int processedFileCounter = 0;

        Parallel.ForEach(filePaths, new() { MaxDegreeOfParallelism = 25 }, filePath =>
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
}