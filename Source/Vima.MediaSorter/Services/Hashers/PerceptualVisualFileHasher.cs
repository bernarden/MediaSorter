using System;
using SkiaSharp;
using Vima.MediaSorter.Domain;
using Vima.MediaSorter.Infrastructure;

namespace Vima.MediaSorter.Services.Hashers;

public class PerceptualVisualFileHasher(IFileSystem fileSystem) : VisualFileHasherBase(fileSystem)
{
    public override VisualHasherType Type => VisualHasherType.Perceptual;

    protected override (int width, int height) RequiredSize => (32, 32);

    protected override ulong GenerateHash(SKBitmap bitmap)
    {
        var size = 32;
        var pixelIntensities = new double[size, size];

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                pixelIntensities[x, y] = bitmap.GetPixel(x, y).Red;
            }
        }

        var dct = ApplyDCT(pixelIntensities, size);

        double total = 0;
        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                if (x == 0 && y == 0)
                {
                    continue;
                }

                total += dct[x, y];
            }
        }
        var avg = total / 63;

        ulong hash = 0;
        var bitIndex = 0;
        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                if (dct[x, y] > avg)
                {
                    hash |= 1UL << bitIndex;
                }

                bitIndex++;
            }
        }

        return hash;
    }

    private static double[,] ApplyDCT(double[,] pixelIntensities, int size)
    {
        var dctCoefficients = new double[size, size];
        var pi = Math.PI;

        for (var u = 0; u < 8; u++)
        {
            for (var v = 0; v < 8; v++)
            {
                double sum = 0;
                for (var x = 0; x < size; x++)
                {
                    for (var y = 0; y < size; y++)
                    {
                        sum +=
                            pixelIntensities[x, y]
                            * Math.Cos((2 * x + 1) * u * pi / (2 * size))
                            * Math.Cos((2 * y + 1) * v * pi / (2 * size));
                    }
                }

                var alphaU = (u == 0) ? Math.Sqrt(1.0 / size) : Math.Sqrt(2.0 / size);
                var alphaV = (v == 0) ? Math.Sqrt(1.0 / size) : Math.Sqrt(2.0 / size);

                dctCoefficients[u, v] = alphaU * alphaV * sum;
            }
        }
        return dctCoefficients;
    }
}
