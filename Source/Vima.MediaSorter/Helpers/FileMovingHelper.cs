using System;
using System.IO;

namespace Vima.MediaSorter.Helpers
{
    public class FileMovingHelper
    {
        public enum MoveStatus
        {
            Success,
            Duplicate
        }

        public static MoveStatus MoveFile(string mainFolderPath, string filePath, DateTime createdDate)
        {
            string newFolderName = createdDate.ToString("yyyy_MM_dd -");
            string newFolderPath = Path.Combine(mainFolderPath, newFolderName);
            Directory.CreateDirectory(newFolderPath);

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
            var count = 1;
            do
            {
                var fileName = $"{Path.GetFileNameWithoutExtension(filePath)} ({count}){Path.GetExtension(filePath)}";
                filePathInNewFolder = Path.Combine(newFolderPath, fileName);
                count++;
            } while (File.Exists(filePathInNewFolder));

            return filePathInNewFolder;
        }
    }
}