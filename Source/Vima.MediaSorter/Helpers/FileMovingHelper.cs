using System.Collections.Generic;
using System.IO;

namespace Vima.MediaSorter.Helpers;

public class FileMovingHelper
{
    public static readonly HashSet<string> PreviouslyCreatedFolders = [];

    public enum MoveStatus
    {
        Success,
        Duplicate
    }

    public static (MoveStatus, string?) MoveFile(string filePath, string destinationFolderPath)
    {
        if (!PreviouslyCreatedFolders.Contains(destinationFolderPath))
        {
            Directory.CreateDirectory(destinationFolderPath);
            PreviouslyCreatedFolders.Add(destinationFolderPath);
        }

        string filePathInNewFolder = Path.Combine(destinationFolderPath, Path.GetFileName(filePath));
        if (File.Exists(filePathInNewFolder))
        {
            if (DuplicationHelper.AreFilesIdentical(filePath, filePathInNewFolder))
            {
                return (MoveStatus.Duplicate, filePathInNewFolder);
            }

            filePathInNewFolder = GenerateUniqueName(filePath, destinationFolderPath);
        }

        File.Move(filePath, filePathInNewFolder);
        return (MoveStatus.Success, null);
    }

    private static string GenerateUniqueName(string filePath, string newFolderPath)
    {
        string filePathInNewFolder;
        int count = 1;
        do
        {
            string fileName =
                $"{Path.GetFileNameWithoutExtension(filePath)} ({count}){Path.GetExtension(filePath)}";
            filePathInNewFolder = Path.Combine(newFolderPath, fileName);
            count++;
        } while (File.Exists(filePathInNewFolder));

        return filePathInNewFolder;
    }
}