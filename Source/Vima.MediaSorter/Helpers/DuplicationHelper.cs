using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace Vima.MediaSorter.Helpers;

public class DuplicationHelper
{
    public static bool AreFilesIdentical(string path1, string path2)
    {
        long length1 = new FileInfo(path1).Length;
        long length2 = new FileInfo(path2).Length;
        if (length1 != length2)
        {
            return false;
        }

        IEnumerable<byte> hash1 = CalculateFileHash(path1);
        IEnumerable<byte> hash2 = CalculateFileHash(path2);
        return hash1.SequenceEqual(hash2);
    }

    private static byte[] CalculateFileHash(string filePath)
    {
        using FileStream stream = new(filePath, FileMode.Open, FileAccess.Read);
        using HashAlgorithm hashAlgorithm = SHA256.Create();
        byte[] hash = hashAlgorithm.ComputeHash(stream);
        return hash;
    }
}