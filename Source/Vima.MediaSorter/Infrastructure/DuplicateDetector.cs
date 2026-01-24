using System.IO;

namespace Vima.MediaSorter.Infrastructure;

public interface IDuplicateDetector
{
    bool AreIdentical(string path1, string path2);
}

public class DuplicateDetector : IDuplicateDetector
{
    public bool AreIdentical(string path1, string path2)
    {
        var f1 = new FileInfo(path1);
        var f2 = new FileInfo(path2);
        if (f1.Length != f2.Length) return false;

        using var s1 = new FileStream(path1, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var s2 = new FileStream(path2, FileMode.Open, FileAccess.Read, FileShare.Read);

        int b1;
        while ((b1 = s1.ReadByte()) != -1)
        {
            if (b1 != s2.ReadByte()) return false;
        }
        return true;
    }
}