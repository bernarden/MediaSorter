using System;

namespace Vima.MediaSorter.Domain;

public class FindDuplicatesFile(
    string Path,
    string BinaryHash,
    ulong AverageHash,
    ulong DifferenceHash,
    ulong PerceptualHash,
    long Size,
    int Width,
    int Height,
    DateTime LastModified
)
{
    public string Path { get; } = Path;
    public string BinaryHash { get; } = BinaryHash;
    public ulong AverageHash { get; } = AverageHash;
    public ulong DifferenceHash { get; } = DifferenceHash;
    public ulong PerceptualHash { get; } = PerceptualHash;
    public long Size { get; } = Size;
    public int Width { get; } = Width;
    public int Height { get; } = Height;
    public DateTime LastModified { get; } = LastModified;
}
