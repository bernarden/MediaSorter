using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.IO;
using Vima.MediaSorter.Domain;
using Vima.MediaSorter.Infrastructure;

namespace Vima.MediaSorter.Services;

public interface IDirectoryIdentificationService
{
    DirectoryStructure Identify(IProgress<double>? progress = null);
}

public class DirectoryIdentificationService(
    IDirectoryResolver directoryResolver,
    IOptions<MediaSorterOptions> options
) : IDirectoryIdentificationService
{
    public DirectoryStructure Identify(IProgress<double>? progress = null)
    {
        DirectoryStructure result = new();
        var dateToExistingDirectoriesMapping = new Dictionary<DateTime, List<string>>();
        string[] directoryPaths = Directory.GetDirectories(options.Value.Directory);
        if (directoryPaths.Length == 0)
        {
            progress?.Report(1.0);
            return result;
        }

        for (int i = 0; i < directoryPaths.Length; i++)
        {
            ScanDirectoryRecursively(
                new DirectoryInfo(directoryPaths[i]),
                result,
                dateToExistingDirectoriesMapping
            );
            progress?.Report((double)(i + 1) / directoryPaths.Length);
        }

        foreach ((var date, var directories) in dateToExistingDirectoriesMapping)
        {
            if (directories.Count <= 0)
                continue;

            result.DateToExistingDirectoryMapping[date] = directories[0];

            if (directories.Count > 1)
            {
                var directoriesToIgnore = directories.GetRange(1, directories.Count - 1);
                result.DateToIgnoredDirectoriesMapping[date] = directoriesToIgnore;
            }
        }

        return result;
    }

    private void ScanDirectoryRecursively(
        DirectoryInfo directoryInfo,
        DirectoryStructure result,
        Dictionary<DateTime, List<string>> dateToDirMapping,
        bool isInsideSortedBranch = false
    )
    {
        DateTime? directoryDate = directoryResolver.GetDateFromDirectoryName(directoryInfo.Name);
        if (directoryDate.HasValue)
        {
            result.SortedFolders.Add(directoryInfo.FullName);
            DateTime date = directoryDate.Value.Date;
            if (dateToDirMapping.TryGetValue(date, out List<string>? existingDirectories))
                existingDirectories.Add(directoryInfo.FullName);
            else
                dateToDirMapping.Add(date, new List<string> { directoryInfo.FullName });
        }
        else
        {
            if (isInsideSortedBranch)
                result.SortedSubFolders.Add(directoryInfo.FullName);
            else
                result.UnsortedFolders.Add(directoryInfo.FullName);
        }

        foreach (var subDir in directoryInfo.GetDirectories())
        {
            ScanDirectoryRecursively(
                subDir,
                result,
                dateToDirMapping,
                isInsideSortedBranch || directoryDate.HasValue
            );
        }
    }
}