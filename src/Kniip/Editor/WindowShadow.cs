using System;

namespace Kniip.Editor;

/// <summary>Zachte schaduw rondom een venster op een transparante rand, zoals Windows hem zelf tekent.</summary>
internal static class WindowShadow
{
    public const int Margin = 24;
    private const int BlurRadius = 6;
    private const int OffsetY = 4;
    private const double Opacity = 0.32;

    /// <param name="pixels">Bgra32 zonder voorvermenigvuldiging.</param>
    /// <param name="cornerRadius">Afgeronde hoeken transparant maken; 0 = rechte hoeken laten staan.</param>
    public static (byte[] Pixels, int Width, int Height) Apply(byte[] pixels, int width, int height, int cornerRadius)
    {
        byte[] window = (byte[])pixels.Clone();
        if (cornerRadius > 0)
        {
            RoundCorners(window, width, height, Math.Min(cornerRadius, Math.Min(width, height) / 2));
        }

        int w = width + 2 * Margin;
        int h = height + 2 * Margin;
        var alpha = new byte[w * h];
        for (int y = 0; y < height; y++)
        {
            int ty = y + Margin + OffsetY;
            if (ty >= h)
            {
                break;
            }

            for (int x = 0; x < width; x++)
            {
                alpha[ty * w + x + Margin] = window[(y * width + x) * 4 + 3];
            }
        }

        var temp = new byte[alpha.Length];
        for (int pass = 0; pass < 3; pass++)
        {
            BlurHorizontal(alpha, temp, w, h, BlurRadius);
            BlurVertical(temp, alpha, w, h, BlurRadius);
        }

        var result = new byte[w * h * 4];
        for (int i = 0; i < alpha.Length; i++)
        {
            result[i * 4 + 3] = (byte)Math.Round(alpha[i] * Opacity);
        }

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int s = (y * width + x) * 4;
                int d = ((y + Margin) * w + x + Margin) * 4;
                double sa = window[s + 3] / 255.0;
                double da = result[d + 3] / 255.0;
                double outA = sa + da * (1 - sa);
                if (outA <= 0)
                {
                    continue;
                }

                // De schaduw is zwart, dus alleen de vensterkleur telt mee in de kleur.
                double scale = sa / outA;
                result[d] = (byte)Math.Round(window[s] * scale);
                result[d + 1] = (byte)Math.Round(window[s + 1] * scale);
                result[d + 2] = (byte)Math.Round(window[s + 2] * scale);
                result[d + 3] = (byte)Math.Round(outA * 255);
            }
        }

        return (result, w, h);
    }

    private static void RoundCorners(byte[] pixels, int width, int height, int radius)
    {
        for (int y = 0; y < radius; y++)
        {
            for (int x = 0; x < radius; x++)
            {
                double dx = radius - (x + 0.5);
                double dy = radius - (y + 0.5);
                double coverage = Math.Clamp(radius - Math.Sqrt(dx * dx + dy * dy) + 0.5, 0, 1);
                if (coverage >= 1)
                {
                    continue;
                }

                Fade(pixels, width, x, y, coverage);
                Fade(pixels, width, width - 1 - x, y, coverage);
                Fade(pixels, width, x, height - 1 - y, coverage);
                Fade(pixels, width, width - 1 - x, height - 1 - y, coverage);
            }
        }
    }

    private static void Fade(byte[] pixels, int width, int x, int y, double coverage)
    {
        int i = (y * width + x) * 4 + 3;
        pixels[i] = (byte)Math.Round(pixels[i] * coverage);
    }

    private static void BlurHorizontal(byte[] src, byte[] dst, int w, int h, int r)
    {
        int window = 2 * r + 1;
        for (int y = 0; y < h; y++)
        {
            int row = y * w;
            int sum = 0;
            for (int i = -r; i <= r; i++)
            {
                sum += src[row + Math.Clamp(i, 0, w - 1)];
            }

            for (int x = 0; x < w; x++)
            {
                dst[row + x] = (byte)(sum / window);
                sum += src[row + Math.Min(x + r + 1, w - 1)] - src[row + Math.Max(x - r, 0)];
            }
        }
    }

    private static void BlurVertical(byte[] src, byte[] dst, int w, int h, int r)
    {
        int window = 2 * r + 1;
        for (int x = 0; x < w; x++)
        {
            int sum = 0;
            for (int i = -r; i <= r; i++)
            {
                sum += src[Math.Clamp(i, 0, h - 1) * w + x];
            }

            for (int y = 0; y < h; y++)
            {
                dst[y * w + x] = (byte)(sum / window);
                sum += src[Math.Min(y + r + 1, h - 1) * w + x] - src[Math.Max(y - r, 0) * w + x];
            }
        }
    }
}
