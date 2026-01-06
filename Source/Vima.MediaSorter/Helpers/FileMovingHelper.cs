using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;

namespace Vima.MediaSorter.Helpers;

public class FileMovingHelper
{
    private static readonly ConcurrentDictionary<string, byte> PreviouslyCreatedFolders = new();
    private static readonly object[] Locks = [.. Enumerable.Range(0, 32).Select(_ => new object())];

    public enum MoveStatus
    {
        Success,
        Duplicate
    }

    public static (MoveStatus, string?) MoveFile(
        string filePath, string destinationFolderPath, string destinationFileName)
    {
        EnsureFolderExists(destinationFolderPath);

        string filePathInNewFolder = Path.Combine(destinationFolderPath, destinationFileName);
        if (File.Exists(filePathInNewFolder))
        {
            if (DuplicationHelper.AreFilesIdentical(filePath, filePathInNewFolder))
            {
                return (MoveStatus.Duplicate, filePathInNewFolder);
            }

            filePathInNewFolder = GenerateUniqueName(destinationFolderPath, destinationFileName);
        }

        const int maxAttempts = 3;
        for (int attempt = 1; attempt <= maxAttempts; ++attempt)
        {
            try
            {
                File.Move(filePath, filePathInNewFolder);
                break;
            }
            catch (Exception ex) when (
                (ex is IOException ||
                ex is UnauthorizedAccessException ||
                ex is DirectoryNotFoundException) &&
                attempt < maxAttempts)
            {
                Thread.Sleep(150 * attempt);
            }
        }

        return (MoveStatus.Success, null);
    }


    private static void EnsureFolderExists(string path)
    {
        if (PreviouslyCreatedFolders.ContainsKey(path)) return;

        int lockIndex = (path.GetHashCode() & 0x7FFFFFFF) % Locks.Length;
        lock (Locks[lockIndex])
        {
            if (PreviouslyCreatedFolders.ContainsKey(path)) return;

            Directory.CreateDirectory(path);
            PreviouslyCreatedFolders.TryAdd(path, 0);
        }
    }
    private static string GenerateUniqueName(string newFolderPath, string destinationFileName)
    {
        string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(destinationFileName);
        string extension = Path.GetExtension(destinationFileName).ToLowerInvariant();

        string filePathInNewFolder;
        int count = 1;
        do
        {
            string fileName = $"{fileNameWithoutExtension} ({count}){extension}";
            filePathInNewFolder = Path.Combine(newFolderPath, fileName);
            count++;
        } while (File.Exists(filePathInNewFolder));

        return filePathInNewFolder;
    }
}