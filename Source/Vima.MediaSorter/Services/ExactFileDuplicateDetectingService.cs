using System.IO;
using Vima.MediaSorter.Infrastructure;

namespace Vima.MediaSorter.Services;

public interface IExactFileDuplicateDetectingService
{
    bool AreIdentical(string path1, string path2);
}

public class ExactFileDuplicateDetectingService(IFileSystem fileSystem)
    : IExactFileDuplicateDetectingService
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
