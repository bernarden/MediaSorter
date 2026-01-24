using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using Vima.MediaSorter.Domain;

namespace Vima.MediaSorter.Infrastructure;

public interface IFileMover
{
    (MoveStatus Status, string? DestinationPath) Move(
        string sourceFilePath, string targetDirectory, string targetFileName);
}

public class FileMover(IDuplicateDetector duplicateDetector) : IFileMover
{
    private readonly ConcurrentDictionary<string, bool> _previouslyCreatedFolders = new();

    public (MoveStatus Status, string? DestinationPath) Move(
        string sourceFilePath, string targetDirectory, string targetFileName)
    {
        _previouslyCreatedFolders.GetOrAdd(targetDirectory, path =>
        {
            Directory.CreateDirectory(path);
            return true;
        });

        string targetPath = Path.Combine(targetDirectory, targetFileName);
        if (!File.Exists(targetPath))
            return ExecuteMoveWithRetries(sourceFilePath, targetPath);

        if (duplicateDetector.AreIdentical(sourceFilePath, targetPath))
            return (MoveStatus.Duplicate, targetPath);

        string uniqueTargetPath = GenerateUniqueTargetPath(targetDirectory, targetFileName);
        return ExecuteMoveWithRetries(sourceFilePath, uniqueTargetPath);
    }

    private (MoveStatus, string?) ExecuteMoveWithRetries(string source, string dest)
    {
        const int maxAttempts = 3;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                File.Move(source, dest);
                return (MoveStatus.Success, null);
            }
            catch (Exception ex) when (IsTransient(ex) && attempt < maxAttempts)
            {
                Thread.Sleep(attempt * 150);
            }
        }

        File.Move(source, dest);
        return (MoveStatus.Success, null);
    }

    private bool IsTransient(Exception ex) => ex switch
    {
        IOException => true,
        UnauthorizedAccessException => true,
        _ => false
    };

    private static string GenerateUniqueTargetPath(string targetDirectory, string targetFileName)
    {
        string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(targetFileName);
        string extension = Path.GetExtension(targetFileName).ToLowerInvariant();

        int count = 1;
        string uniqueTargetPath;
        do
        {
            uniqueTargetPath = Path.Combine(targetDirectory, $"{fileNameWithoutExtension} ({count}){extension}");
            count++;
        } while (File.Exists(uniqueTargetPath));

        return uniqueTargetPath;
    }
}