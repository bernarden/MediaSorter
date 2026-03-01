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

        string targetPath = Path.Combine(targetDirectory, targetFileName);
        if (!fileSystem.FileExists(targetPath))
            return ExecuteMoveWithRetries(sourceFilePath, targetPath);

        if (duplicateDetector.AreIdentical(sourceFilePath, targetPath))
            return new DuplicateDetectedFileMove(sourceFilePath, targetPath);

        string uniqueTargetPath = GenerateUniqueTargetPath(targetDirectory, targetFileName);
        return ExecuteMoveWithRetries(sourceFilePath, uniqueTargetPath);
    }

    private FileMove ExecuteMoveWithRetries(string source, string dest)
    {
        const int maxAttempts = 3;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                fileSystem.Move(source, dest);
                return new SuccessfulFileMove(source, dest);
            }
            catch (Exception ex) when (IsTransient(ex) && attempt < maxAttempts)
            {
                Thread.Sleep(attempt * 150);
            }
        }

        fileSystem.Move(source, dest);
        return new SuccessfulFileMove(source, dest);
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
