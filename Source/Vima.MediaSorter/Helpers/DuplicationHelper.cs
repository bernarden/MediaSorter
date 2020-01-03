using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace Vima.MediaSorter.Helpers
{
    public class DuplicationHelper
    {
        public static bool AreFilesIdentical(string path1, string path2)
        {
            var length1 = new FileInfo(path1).Length;
            var length2 = new FileInfo(path2).Length;
            if (length1 != length2)
            {
                return false;
            }

            var hash1 = CalculateFileHash(path1);
            var hash2 = CalculateFileHash(path2);
            return hash1.SequenceEqual(hash2);
        }

        private static IEnumerable<byte> CalculateFileHash(string filePath)
        {
            using FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            using HashAlgorithm hashAlgorithm = SHA256.Create();
            var hash = hashAlgorithm.ComputeHash(stream);
            return hash;
        }
    }
}