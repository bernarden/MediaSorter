using System.IO;

namespace Vima.MediaSorter.Infrastructure;

public interface IDuplicateDetector
{
    bool AreIdentical(string path1, string path2);
}

public class DuplicateDetector(IFileSystem fileSystem) : IDuplicateDetector
{
    public bool AreIdentical(string path1, string path2)
    {
        if (fileSystem.GetFileSize(path1) != fileSystem.GetFileSize(path2))
            return false;

        using var s1 = fileSystem.CreateFileStream(path1, FileMode.Open, FileAccess.Read);
        using var s2 = fileSystem.CreateFileStream(path2, FileMode.Open, FileAccess.Read);

        int b1;
        while ((b1 = s1.ReadByte()) != -1)
        {
            if (b1 != s2.ReadByte())
                return false;
        }
        return true;
    }
}
