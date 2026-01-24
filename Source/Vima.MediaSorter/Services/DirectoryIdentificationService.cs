using Microsoft.Extensions.Options;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using Vima.MediaSorter.Domain;
using Vima.MediaSorter.Infrastructure;
using Vima.MediaSorter.UI;

namespace Vima.MediaSorter.Services;

public interface IDirectoryIdentificationService
{
    DirectoryStructure IdentifyDirectoryStructure();
}

public class DirectoryIdentificationService(
    IDirectoryResolver directoryResolver,
    IOptions<MediaSorterOptions> options) : IDirectoryIdentificationService
{
    public DirectoryStructure IdentifyDirectoryStructure()
    {
        Console.Write("Identifying directory structure... ");
        using ProgressBar progress = new();
        int processedDirectoryCounter = 0;
        DirectoryStructure result = new();
        ConcurrentDictionary<DateTime, ConcurrentBag<string>> ignoredFolderForDate = new();
        string[] directoryPaths = Directory.GetDirectories(options.Value.Directory);
        foreach (string directoryPath in directoryPaths)
        {
            ProcessDirectoryPath(directoryPath, result, ignoredFolderForDate);
            Interlocked.Increment(ref processedDirectoryCounter);
            progress.Report((double)processedDirectoryCounter / directoryPaths.Length);
        }

        progress.Dispose();
        Console.WriteLine("Done.");

        if (!ignoredFolderForDate.IsEmpty)
        {
            Console.WriteLine("  Warning: Multiple directories mapped to the same dates (Only first discovery used):");
            foreach (var ignoredFolders in ignoredFolderForDate.OrderBy(x => x.Key))
            {
                string usedDirName = Path.GetFileName(result.DateToExistingDirectoryMapping[ignoredFolders.Key]);
                Console.WriteLine($"    [{ignoredFolders.Key:yyyy_MM_dd}] Target: '{usedDirName}'");
                foreach (var ignoredFolder in ignoredFolders.Value)
                {
                    Console.WriteLine($"                 Ignore: '{ignoredFolder}'");
                }
            }
            Console.WriteLine();
        }

        return result;
    }

    private void ProcessDirectoryPath(
        string directoryPath,
        DirectoryStructure result,
        ConcurrentDictionary<DateTime, ConcurrentBag<string>> ignoredFolderForDate)
    {
        DirectoryInfo directoryInfo = new(directoryPath);
        string directoryName = directoryInfo.Name;

        DateTime? directoryDate = directoryResolver.GetDateFromDirectoryName(directoryName);
        if (directoryDate == null)
        {
            result.UnsortedFolders.Add(directoryPath);
        }
        else
        {
            result.SortedFolders.Add(directoryPath);
            DateTime date = directoryDate.Value.Date;
            if (result.DateToExistingDirectoryMapping.TryGetValue(date, out string? existingDirectoryPath))
            {
                var ignoredFolders = ignoredFolderForDate.GetOrAdd(date, _ => new ConcurrentBag<string>());
                ignoredFolders.Add(directoryName);
            }
            else
            {
                result.DateToExistingDirectoryMapping.Add(date, directoryPath);
            }
        }
    }
}