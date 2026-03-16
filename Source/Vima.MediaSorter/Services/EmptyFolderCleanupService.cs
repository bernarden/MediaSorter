using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Options;
using Vima.MediaSorter.Domain;
using Vima.MediaSorter.Infrastructure;

namespace Vima.MediaSorter.Services;

public interface IEmptyFolderCleanupService
{
    IReadOnlyList<string> GetFoldersForDeletion(IEnumerable<string> foldersToCheck);
    EmptyFolderCleanupResult DeleteFolders(
        IEnumerable<string> foldersToDelete,
        IProgress<double>? progress = null
    );
}

public class EmptyFolderCleanupService(IFileSystem fileSystem, IOptions<MediaSorterOptions> options)
    : IEmptyFolderCleanupService
{
    public IReadOnlyList<string> GetFoldersForDeletion(IEnumerable<string> foldersToCheck)
    {
        string rootDir =
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.Value.Directory))
            + Path.DirectorySeparatorChar;

        var processingQueue = new PriorityQueue<string, int>();

        foreach (var path in foldersToCheck)
        {
            string fullPath = Path.GetFullPath(path);
            if (
                fullPath.StartsWith(rootDir, StringComparison.OrdinalIgnoreCase)
                && fullPath.Length > rootDir.Length
            )
            {
                processingQueue.Enqueue(fullPath, -fullPath.Length);
            }
        }

        if (processingQueue.Count == 0)
            return new List<string>();

        var foldersToDelete = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (processingQueue.TryDequeue(out var current, out _))
        {
            if (!fileSystem.DirectoryExists(current))
                continue;

            bool hasFiles = fileSystem
                .EnumerateFiles(current, "*", SearchOption.TopDirectoryOnly)
                .Any();
            if (hasFiles)
                continue;

            bool hasRemainingSubDirs = fileSystem
                .EnumerateDirectories(current, "*", SearchOption.TopDirectoryOnly)
                .Any(sub => !foldersToDelete.Contains(sub));

            if (!hasRemainingSubDirs)
            {
                foldersToDelete.Add(current);

                string? parent = Path.GetDirectoryName(current);
                if (
                    parent != null
                    && parent.StartsWith(rootDir, StringComparison.OrdinalIgnoreCase)
                    && parent.Length >= rootDir.Length
                    && seen.Add(parent)
                )
                {
                    processingQueue.Enqueue(parent, -parent.Length);
                }
            }
        }
        return foldersToDelete.OrderByDescending(f => f.Length).ToList();
    }

    public EmptyFolderCleanupResult DeleteFolders(
        IEnumerable<string> foldersToDelete,
        IProgress<double>? progress = null
    )
    {
        var deletedFolders = new List<string>();
        var deletionErrors = new List<PathError>();
        var folderList = foldersToDelete.ToList();

        for (int i = 0; i < folderList.Count; i++)
        {
            var folder = folderList[i];
            try
            {
                if (fileSystem.DirectoryExists(folder))
                {
                    fileSystem.DeleteDirectory(folder);
                    deletedFolders.Add(folder);
                }
            }
            catch (Exception ex)
            {
                deletionErrors.Add(new(folder, ex));
            }
            progress?.Report((double)(i + 1) / folderList.Count);
        }

        return new EmptyFolderCleanupResult()
        {
            DeletedFolders = deletedFolders,
            ErroredFolders = deletionErrors,
        };
    }
}
