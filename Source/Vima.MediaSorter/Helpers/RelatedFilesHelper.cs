using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Vima.MediaSorter.Helpers;

public class RelatedFilesHelper
{
    public static IEnumerable<string> FindAll(string filePath)
    {
        HashSet<string> relatedFiles = [];
        string fileName = Path.GetFileNameWithoutExtension(filePath);
        string directoryPath = Path.GetDirectoryName(filePath) ?? @"\";

        // Find all files with the same name but different extensions.
        var relatedFilePaths = Directory
            .GetFiles(directoryPath, fileName + ".*")
            .Except(new List<string> { filePath })
            .ToHashSet();
        relatedFiles.UnionWith(relatedFilePaths);

        // Find LRV files for recent GoPro naming convention.
        Regex rgx = new(@"G[HX](\d{2}|\w{2})\d{4}", RegexOptions.IgnoreCase);
        Match mat = rgx.Match(filePath);
        if (mat.Success)
        {
            string lrvFileName = $"{fileName.Remove(1, 1).Insert(1, "L")}.LRV";
            string lrvFilePath = Path.Combine(directoryPath, lrvFileName);
            if (File.Exists(lrvFilePath))
            {
                relatedFiles.Add(lrvFilePath);
            }
        }

        return relatedFiles;
    }
}