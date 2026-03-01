using System;
using System.IO;
using System.Security.Cryptography;

namespace Vima.MediaSorter.Infrastructure.Hashers;

public interface IFileHasher
{
    string GetHash(string path);
}

public class Md5FileHasher(IFileSystem fileSystem) : IFileHasher
{
    public string GetHash(string path)
    {
        if (!fileSystem.FileExists(path))
            return string.Empty;

        using var stream = fileSystem.CreateFileStream(path, FileMode.Open, FileAccess.Read);
        byte[] hashBytes = MD5.HashData(stream);
        return Convert.ToHexString(hashBytes);
    }
}
