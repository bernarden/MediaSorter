using SkiaSharp;
using Vima.MediaSorter.Domain;

namespace Vima.MediaSorter.Infrastructure.Hashers;

public class DifferenceVisualFileHasher : VisualFileHasherBase
{
    public override VisualHasherType Type => VisualHasherType.Difference;

    protected override (int width, int height) RequiredSize => (9, 8);

    protected override ulong GenerateHash(SKBitmap bitmap)
    {
        ulong hash = 0;
        int bitIndex = 0;

        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                byte left = bitmap.GetPixel(x, y).Red;
                byte right = bitmap.GetPixel(x + 1, y).Red;

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
