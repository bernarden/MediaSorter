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

    public static (MoveStatus, string?) MoveFile(
        string filePath, string destinationFolderPath, string destinationFileName)
    {
        if (!PreviouslyCreatedFolders.Contains(destinationFolderPath))
        {
            Directory.CreateDirectory(destinationFolderPath);
            PreviouslyCreatedFolders.Add(destinationFolderPath);
        }

        string filePathInNewFolder = Path.Combine(destinationFolderPath, destinationFileName);
        if (File.Exists(filePathInNewFolder))
        {
            if (DuplicationHelper.AreFilesIdentical(filePath, filePathInNewFolder))
            {
                return (MoveStatus.Duplicate, filePathInNewFolder);
            }

            filePathInNewFolder = GenerateUniqueName(destinationFolderPath, destinationFileName);
        }

        File.Move(filePath, filePathInNewFolder);
        return (MoveStatus.Success, null);
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