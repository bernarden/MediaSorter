using SkiaSharp;
using System.IO;
using System.Numerics;
using Vima.MediaSorter.Domain;

namespace Vima.MediaSorter.Infrastructure.Hashers;

public interface IVisualFileHasher
{
    VisualHasherType Type { get; }
    ulong GetHash(string path);
    ulong GetHash(SKBitmap bitmap);
    bool IsMatch(ulong hash1, ulong hash2, int threshold);
}

public abstract class VisualFileHasherBase : IVisualFileHasher
{
    public abstract VisualHasherType Type { get; }

    protected abstract (int width, int height) RequiredSize { get; }

    protected abstract ulong GenerateHash(SKBitmap bitmap);

    public ulong GetHash(string path)
    {
        using var stream = File.OpenRead(path);
        using var original = SKBitmap.Decode(stream);
        return GetHash(original);
    }

    public ulong GetHash(SKBitmap bitmap)
    {
        var (w, h) = RequiredSize;
        var info = new SKImageInfo(w, h, SKColorType.Gray8, SKAlphaType.Opaque);
        using var resized = new SKBitmap(info);
        bitmap.ScalePixels(resized, SKSamplingOptions.Default);
        return GenerateHash(resized);
    }

    public virtual bool IsMatch(ulong hash1, ulong hash2, int threshold)
    {
        if (hash1 == 0 || hash2 == 0)
            return false;
        int distance = BitOperations.PopCount(hash1 ^ hash2);
        return distance <= threshold;
    }
}
