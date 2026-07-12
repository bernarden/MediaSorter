using SkiaSharp;
using Vima.MediaSorter.Domain;
using Vima.MediaSorter.Infrastructure;

namespace Vima.MediaSorter.Services.Hashers;

public class DifferenceVisualFileHasher(IFileSystem fileSystem) : VisualFileHasherBase(fileSystem)
{
    public override VisualHasherType Type => VisualHasherType.Difference;

    protected override (int width, int height) RequiredSize => (9, 8);

    protected override ulong GenerateHash(SKBitmap bitmap)
    {
        ulong hash = 0;
        var bitIndex = 0;

        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                var left = bitmap.GetPixel(x, y).Red;
                var right = bitmap.GetPixel(x + 1, y).Red;

                if (left > right)
                {
                    hash |= 1UL << bitIndex;
                }

                bitIndex++;
            }
        }
        return hash;
    }
}
