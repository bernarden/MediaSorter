using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Options;
using Vima.MediaSorter.Domain;

namespace Vima.MediaSorter.Infrastructure;

public interface IFileSystem
{
    IEnumerable<string> EnumerateFiles(
        string path,
        string searchPattern = "*",
        SearchOption searchOption = SearchOption.TopDirectoryOnly
    );

    IEnumerable<string> EnumerateDirectories(
        string path,
        string searchPattern = "*",
        SearchOption searchOption = SearchOption.TopDirectoryOnly
    );

    bool FileExists(string path);
    bool DirectoryExists(string path);
    void CreateDirectory(string path);
    void DeleteFile(string path);
    void DeleteDirectory(string path, bool recursive = false);
    void Move(string source, string destination);
    FileStream CreateFileStream(
        string path,
        FileMode mode,
        FileAccess access,
        FileShare share = FileShare.ReadWrite,
        int bufferSize = 4096,
        bool useAsync = false
    );
    long GetFileSize(string path);
    string GetRelativePath(string? path, string? relativeTo = null);
    DateTime GetLastWriteTimeUtc(string path);
}

public class FileSystem(IOptions<MediaSorterOptions> options) : IFileSystem
{
    private readonly MediaSorterOptions _options = options.Value;

    public IEnumerable<string> EnumerateFiles(
        string path,
        string searchPattern = "*",
        SearchOption searchOption = SearchOption.TopDirectoryOnly
    )
    {
        return Directory.EnumerateFiles(path, searchPattern, searchOption);
    }

    public IEnumerable<string> EnumerateDirectories(
        string path,
        string searchPattern = "*",
        SearchOption searchOption = SearchOption.TopDirectoryOnly
    )
    {
        return Directory.EnumerateDirectories(path, searchPattern, searchOption);
    }

    public bool FileExists(string path)
    {
        return File.Exists(path);
    }

    public bool DirectoryExists(string path)
    {
        return Directory.Exists(path);
    }

    public void CreateDirectory(string path)
    {
        Directory.CreateDirectory(path);
    }

    public void DeleteFile(string path)
    {
        File.Delete(path);
    }

    public void DeleteDirectory(string path, bool recursive = false)
    {
        Directory.Delete(path, recursive);
    }

    public void Move(string source, string destination)
    {
        File.Move(source, destination);
    }

    public FileStream CreateFileStream(
        string path,
        FileMode mode,
        FileAccess access,
        FileShare share = FileShare.ReadWrite,
        int bufferSize = 4096,
        bool useAsync = false
    )
    {
        return new FileStream(path, mode, access, share, bufferSize, useAsync);
    }

    public long GetFileSize(string path)
    {
        return new FileInfo(path).Length;
    }

    public string GetRelativePath(string? path, string? relativeTo = null)
    {
        if (string.IsNullOrEmpty(path))
            return string.Empty;

        return Path.GetRelativePath(relativeTo ?? _options.Directory, path);
    }

    public DateTime GetLastWriteTimeUtc(string path)
    {
        return new FileInfo(path).LastWriteTimeUtc;
    }
}
