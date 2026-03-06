using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using Vima.MediaSorter.Domain;

namespace Vima.MediaSorter.Infrastructure;

public interface IFileMover
{
    FileMove Move(string sourceFilePath, string targetDirectory, string targetFileName);
}

public class FileMover(IFileSystem fileSystem, IDuplicateDetector duplicateDetector) : IFileMover
{
    private readonly ConcurrentDictionary<string, bool> _previouslyCreatedFolders = new();

    public FileMove Move(string sourceFilePath, string targetDirectory, string targetFileName)
    {
        _previouslyCreatedFolders.GetOrAdd(
            targetDirectory,
            path =>
            {
                if (!fileSystem.DirectoryExists(path))
                {
                    fileSystem.CreateDirectory(path);
                }
                return true;
            }
        );

        const int maxRetries = 5;
        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            string targetPath = Path.Combine(targetDirectory, targetFileName);

            if (fileSystem.FileExists(targetPath))
            {
                if (duplicateDetector.AreIdentical(sourceFilePath, targetPath))
                    return new DuplicateDetectedFileMove(sourceFilePath, targetPath);

                targetPath = GenerateUniqueTargetPath(targetDirectory, targetFileName);
            }

            try
            {
                fileSystem.Move(sourceFilePath, targetPath);
                return new SuccessfulFileMove(sourceFilePath, targetPath);
            }
            catch (IOException ex) when (IsTransient(ex) && attempt < maxRetries - 1)
            {
                Thread.Sleep(Random.Shared.Next(10, 50));
            }
        }

        throw new IOException(
            $"Could not move {sourceFilePath} after {maxRetries} attempts due to name collisions."
        );
    }

    private static bool IsTransient(Exception ex) =>
        ex switch
        {
            IOException => true,
            UnauthorizedAccessException => true,
            _ => false,
        };

    private string GenerateUniqueTargetPath(string targetDirectory, string targetFileName)
    {
        string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(targetFileName);
        string extension = Path.GetExtension(targetFileName).ToLowerInvariant();

        int count = 1;
        string uniqueTargetPath;
        do
        {
            uniqueTargetPath = Path.Combine(
                targetDirectory,
                $"{fileNameWithoutExtension} ({count}){extension}"
            );
            count++;
        } while (fileSystem.FileExists(uniqueTargetPath));

        return uniqueTargetPath;
    }
}
