using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vima.MediaSorter.Domain;
using Vima.MediaSorter.Helpers;

namespace Vima.MediaSorter.Services;

public class MediaIdentifyingService(MediaSorterSettings settings)
{
    public IdentifiedMedia Identify(IEnumerable<string> directoriesToScan)
    {
        Console.Write("Identifying your media... ");
        using ProgressBar progress = new();
        ConcurrentBag<string> stepLogs = new();

        List<string> filePaths = directoriesToScan
            .SelectMany(d => Directory.EnumerateFiles(d))
            .ToList();

        ConcurrentBag<MediaFile> mediaFiles = new();
        var ignoredFiles = new List<string>();
        int processedFileCounter = 0;
        Parallel.ForEach(filePaths, new() { MaxDegreeOfParallelism = 25 }, filePath =>
        {
            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            if (settings.ImageExtensions.Contains(ext))
            {
                MediaFile mediaFile = new(filePath, MediaFileType.Image);
                MediaMetadataHelper.SetCreatedDateTime(mediaFile);
                mediaFiles.Add(mediaFile);
            }
            else if (settings.VideoExtensions.Contains(ext))
            {
                MediaFile mediaFile = new(filePath, MediaFileType.Video);
                IEnumerable<string> relatedFiles = RelatedFilesHelper.FindAll(filePath);
                mediaFile.RelatedFiles.AddRange(relatedFiles);
                MediaMetadataHelper.SetCreatedDateTime(mediaFile);
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

        foreach (string stepLog in stepLogs)
            Console.WriteLine(stepLog);

        return new IdentifiedMedia
        {
            MediaFiles = [.. mediaFiles],
            IgnoredFiles = ignoredFiles
        };
    }
}
