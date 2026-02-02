using SkiaSharp;
using System;
using Vima.MediaSorter.Domain;

namespace Vima.MediaSorter.Infrastructure.Hashers;

public class PerceptualVisualFileHasher : VisualFileHasherBase
{
    public override VisualHasherType Type => VisualHasherType.Perceptual;

    protected override (int width, int height) RequiredSize => (32, 32);

    protected override ulong GenerateHash(SKBitmap bitmap)
    {
        int size = 32;
        double[,] vals = new double[size, size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                vals[x, y] = bitmap.GetPixel(x, y).Red;
            }
        }

        double[,] dct = ApplyDCT(vals, size);

        double total = 0;
        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                if (x == 0 && y == 0)
                    continue;

                total += dct[x, y];
            }
        }
        double avg = total / 63;

        ulong hash = 0;
        int bitIndex = 0;
        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                if (dct[x, y] > avg)
                    hash |= 1UL << bitIndex;
                bitIndex++;
            }
        }

        return hash;
    }

    private static double[,] ApplyDCT(double[,] f, int size)
    {
        double[,] F = new double[size, size];
        double PI = Math.PI;

        for (int u = 0; u < 8; u++)
        {
            for (int v = 0; v < 8; v++)
            {
                double sum = 0;
                for (int x = 0; x < size; x++)
                {
                    for (int y = 0; y < size; y++)
                    {
                        sum +=
                            f[x, y]
                            * Math.Cos((2 * x + 1) * u * PI / (2 * size))
                            * Math.Cos((2 * y + 1) * v * PI / (2 * size));
                    }
                }

                double alphaU = (u == 0) ? Math.Sqrt(1.0 / size) : Math.Sqrt(2.0 / size);
                double alphaV = (v == 0) ? Math.Sqrt(1.0 / size) : Math.Sqrt(2.0 / size);
                F[u, v] = alphaU * alphaV * sum;
            }
        }
        return F;
    }
}
