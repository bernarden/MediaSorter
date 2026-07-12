using System;

namespace Vima.MediaSorter.Domain;

public class FindDuplicatesFile(
    string path,
    string binaryHash,
    ulong averageHash,
    ulong differenceHash,
    ulong perceptualHash,
    long size,
    int width,
    int height,
    DateTime lastModified
)
{
    public string Path { get; } = path;
    public string BinaryHash { get; } = binaryHash;
    public ulong AverageHash { get; } = averageHash;
    public ulong DifferenceHash { get; } = differenceHash;
    public ulong PerceptualHash { get; } = perceptualHash;
    public long Size { get; } = size;
    public int Width { get; } = width;
    public int Height { get; } = height;
    public DateTime LastModified { get; } = lastModified;
}
