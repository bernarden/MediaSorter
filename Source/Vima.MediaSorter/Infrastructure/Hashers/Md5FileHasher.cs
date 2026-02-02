using System;
using System.IO;
using System.Security.Cryptography;

namespace Vima.MediaSorter.Infrastructure.Hashers;

public interface IFileHasher
{
    string GetHash(string path);
}

public class Md5FileHasher() : IFileHasher
{
    public string GetHash(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists)
            return string.Empty;

        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 65536
        );
        byte[] hashBytes = MD5.HashData(stream);
        return Convert.ToHexString(hashBytes);
    }
}
