using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Threading;
using Vima.MediaSorter.Domain;

public class DirectoryIdentifingService(MediaSorterSettings settings)
{
    public DirectoryStructure IdentifyDirectoryStructure()
    {
        Console.Write("Identifying directory structure... ");
        using ProgressBar progress = new();
        ConcurrentBag<string> stepLogs = new();

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
                    stepLogs.Add(
                        $"\tWarning: Multiple directories mapped to date: '{date:d}'. Only '{new DirectoryInfo(existingDirectoryPath).Name}' is going to be used.");
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

        foreach (string stepLog in stepLogs)
            Console.WriteLine(stepLog);

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