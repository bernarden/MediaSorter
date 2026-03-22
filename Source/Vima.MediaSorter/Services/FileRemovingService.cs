using System;
using System.Collections.Generic;
using System.Linq;
using Vima.MediaSorter.Domain;
using Vima.MediaSorter.Infrastructure;

namespace Vima.MediaSorter.Services;

public interface IFileRemovingService
{
    FileRemovingResult DeleteFiles(IEnumerable<string> filePaths, IProgress<double> progress);
}

public class FileRemovingService(IFileSystem fileSystem) : IFileRemovingService
{
    public FileRemovingResult DeleteFiles(
        IEnumerable<string> filePaths,
        IProgress<double>? progress
    )
    {
        var paths = filePaths.ToList();
        var deletedFiles = new List<string>();
        var erroredFiles = new List<PathError>();

        for (int i = 0; i < paths.Count; i++)
        {
            var path = paths[i];
            try
            {
                if (fileSystem.FileExists(path))
                {
                    fileSystem.DeleteFile(path);
                    deletedFiles.Add(path);
                }
            }
            catch (Exception ex)
            {
                erroredFiles.Add(new PathError(path, ex));
            }

            progress?.Report((double)(i + 1) / paths.Count);
        }

        return new FileRemovingResult()
        {
            DeletedFiles = deletedFiles,
            ErroredFiles = erroredFiles,
        };
    }
}
