using System;
using System.Collections.Generic;
using System.IO;

namespace Vima.MediaSorter.Helpers
{
    public class FileMovingHelper
    {
        public static readonly HashSet<string> PreviouslyCreatedFolders = new();

        public enum MoveStatus
        {
            Success,
            Duplicate
        }

        public static MoveStatus MoveFile(string mainFolderPath, string filePath, DateTime createdDate)
        {
            string newFolderName = createdDate.ToString("yyyy_MM_dd -");
            string newFolderPath = Path.Combine(mainFolderPath, newFolderName);
            return MoveFile(filePath, newFolderPath);
        }
        
        private static MoveStatus MoveFile(string filePath, string newFolderPath)
        {
            if (!PreviouslyCreatedFolders.Contains(newFolderPath))
            {
                Directory.CreateDirectory(newFolderPath);
                PreviouslyCreatedFolders.Add(newFolderPath);
            }

            string filePathInNewFolder = Path.Combine(newFolderPath, Path.GetFileName(filePath));
            if (File.Exists(filePathInNewFolder))
            {
                if (DuplicationHelper.AreFilesIdentical(filePath, filePathInNewFolder))
                {
                    return MoveStatus.Duplicate;
                }

                filePathInNewFolder = GenerateUniqueName(filePath, newFolderPath);
            }

            File.Move(filePath, filePathInNewFolder);
            return MoveStatus.Success;
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
}