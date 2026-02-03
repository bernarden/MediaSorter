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
                directoryPaths[i],
                result,
                dateToExistingDirectoriesMapping
            );
            progress?.Report((double)(i + 1) / directoryPaths.Length);
        }

        foreach ((var date, var existingDirectories) in dateToExistingDirectoriesMapping)
        {
            if (existingDirectories.Count <= 0) continue;

            result.DateToExistingDirectoryMapping[date] = existingDirectories[0];

            if (existingDirectories.Count > 1)
                result.DateToIgnoredDirectoriesMapping[date] =
                    existingDirectories.GetRange(1, existingDirectories.Count - 1);
        }
        return result;
    }

    private void ScanDirectoryRecursively(
        string directoryPath,
        DirectoryStructure result,
        Dictionary<DateTime, List<string>> dateToExistingDirectoriesMapping
    )
    {
        DirectoryInfo directoryInfo = new(directoryPath);
        DateTime? directoryDate = directoryResolver.GetDateFromDirectoryName(directoryInfo.Name);
        if (directoryDate == null)
        {
            result.UnsortedFolders.Add(directoryPath);

            foreach (var subDir in Directory.GetDirectories(directoryPath))
            {
                ScanDirectoryRecursively(subDir, result, dateToExistingDirectoriesMapping);
            }
        }
        else
        {
            result.SortedFolders.Add(directoryPath);
            DateTime date = directoryDate.Value.Date;
            if (dateToExistingDirectoriesMapping.TryGetValue(date, out List<string>? existingDirectories))
            {
                existingDirectories.Add(directoryPath);
            }
            else
            {
                dateToExistingDirectoriesMapping.Add(date, new List<string> { directoryPath });
            }
        }
    }
}