using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Vima.MediaSorter.Domain;
using Vima.MediaSorter.UI;

namespace Vima.MediaSorter.Services;

public interface IDirectoryIdentifingService
{
    DirectoryStructure IdentifyDirectoryStructure();
}

public class DirectoryIdentifingService(MediaSorterSettings settings) : IDirectoryIdentifingService
{
    public DirectoryStructure IdentifyDirectoryStructure()
    {
        Console.Write("Identifying directory structure... ");
        using ProgressBar progress = new();
        ConcurrentDictionary<DateTime, ConcurrentBag<string>> ignoredFolderForDate = new();

        int counter = 0;
        DirectoryStructure result = new();
        string[] directoryPaths = Directory.GetDirectories(settings.Directory);
        foreach (string directoryPath in directoryPaths)
        {
            DirectoryInfo directoryInfo = new(directoryPath);
            string directoryName = directoryInfo.Name;

            DateTime? directoryDate = GetDirectoryDateFromPath(directoryName);
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

            Interlocked.Increment(ref counter);
            progress.Report((double)counter / directoryPaths.Length);
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

    private DateTime? GetDirectoryDateFromPath(string directoryName)
    {
        if (directoryName.Length < settings.FolderNameFormat.Length) return null;

        string directoryNameBeginning = directoryName[..settings.FolderNameFormat.Length];
        return DateTime.TryParseExact(directoryNameBeginning, settings.FolderNameFormat,
            CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime result)
            ? result
            : null;
    }
}