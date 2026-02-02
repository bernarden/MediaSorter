using SkiaSharp;
using Vima.MediaSorter.Domain;

namespace Vima.MediaSorter.Infrastructure.Hashers;

public class AverageVisualFileHasher : VisualFileHasherBase
{
    public override VisualHasherType Type => VisualHasherType.Average;

    protected override (int width, int height) RequiredSize => (8, 8);

    protected override ulong GenerateHash(SKBitmap bitmap)
    {
        long sum = 0;
        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                sum += bitmap.GetPixel(x, y).Red;
            }
        }

        byte avg = (byte)(sum / 64);

        ulong hash = 0;
        for (int i = 0; i < 64; i++)
        {
            if (bitmap.GetPixel(i % 8, i / 8).Red > avg)
            {
                hash |= 1UL << i;
            }
        }
        return hash;
    }
}
