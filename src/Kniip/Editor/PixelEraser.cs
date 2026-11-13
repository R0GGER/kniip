using System;
using System.Collections.Generic;
using System.Windows;

namespace Kniip.Editor;

/// <summary>Maakt pixels van een Bgra32-buffer (niet voorvermenigvuldigd) transparant.</summary>
public static class PixelEraser
{
    /// <summary>Poetst een ronde kwast langs het lijnstuk van a naar b weg; de rand loopt over één pixel af.</summary>
    public static Int32Rect? EraseSegment(byte[] bgra, int width, int height, Point a, Point b, double radius)
    {
        radius = Math.Max(0.5, radius);
        int left = Math.Max(0, (int)Math.Floor(Math.Min(a.X, b.X) - radius - 1));
        int top = Math.Max(0, (int)Math.Floor(Math.Min(a.Y, b.Y) - radius - 1));
        int right = Math.Min(width, (int)Math.Ceiling(Math.Max(a.X, b.X) + radius + 1));
        int bottom = Math.Min(height, (int)Math.Ceiling(Math.Max(a.Y, b.Y) + radius + 1));
        if (left >= right || top >= bottom)
        {
            return null;
        }

        Vector segment = b - a;
        double lengthSquared = segment.LengthSquared;
        for (int y = top; y < bottom; y++)
        {
            for (int x = left; x < right; x++)
            {
                var center = new Point(x + 0.5, y + 0.5);
                double t = lengthSquared > 0 ? Math.Clamp((center - a) * segment / lengthSquared, 0, 1) : 0;
                double distance = (center - (a + segment * t)).Length;
                double coverage = Math.Clamp(radius + 0.5 - distance, 0, 1);
                if (coverage <= 0)
                {
                    continue;
                }

                int i = (y * width + x) * 4;
                if (coverage >= 1)
                {
                    bgra[i] = bgra[i + 1] = bgra[i + 2] = bgra[i + 3] = 0;
                }
                else
                {
                    bgra[i + 3] = (byte)Math.Min(bgra[i + 3], Math.Round(bgra[i + 3] * (1 - coverage)));
                }
            }
        }

        return new Int32Rect(left, top, right - left, bottom - top);
    }

    /// <summary>
    /// Maakt alle aaneengesloten pixels (4 buren) transparant die per kanaal hooguit tolerance (0-100) afwijken
    /// van de pixel op (x, y). Geeft null als er niets veranderde.
    /// </summary>
    public static Int32Rect? FloodErase(byte[] bgra, int width, int height, int x, int y, int tolerance)
    {
        if (x < 0 || y < 0 || x >= width || y >= height)
        {
            return null;
        }

        int start = (y * width + x) * 4;
        if (bgra[start + 3] == 0)
        {
            return null;
        }

        byte b0 = bgra[start], g0 = bgra[start + 1], r0 = bgra[start + 2], a0 = bgra[start + 3];
        int limit = (int)Math.Round(Math.Clamp(tolerance, 0, 100) * 255 / 100.0);

        bool Matches(int px, int py)
        {
            int i = (py * width + px) * 4;
            return bgra[i + 3] != 0
                && Math.Abs(bgra[i] - b0) <= limit
                && Math.Abs(bgra[i + 1] - g0) <= limit
                && Math.Abs(bgra[i + 2] - r0) <= limit
                && Math.Abs(bgra[i + 3] - a0) <= limit;
        }

        int minX = x, maxX = x, minY = y, maxY = y;
        var stack = new Stack<(int X, int Y)>();
        stack.Push((x, y));
        while (stack.Count > 0)
        {
            (int sx, int sy) = stack.Pop();
            if (!Matches(sx, sy))
            {
                continue;
            }

            int l = sx;
            while (l > 0 && Matches(l - 1, sy))
            {
                l--;
            }

            int r = sx;
            while (r < width - 1 && Matches(r + 1, sy))
            {
                r++;
            }

            // Eerst de buren van de hele rij noteren, dan pas wissen: gewiste pixels tellen niet meer als treffer.
            for (int ny = sy - 1; ny <= sy + 1; ny += 2)
            {
                if (ny < 0 || ny >= height)
                {
                    continue;
                }

                bool inRun = false;
                for (int nx = l; nx <= r; nx++)
                {
                    bool match = Matches(nx, ny);
                    if (match && !inRun)
                    {
                        stack.Push((nx, ny));
                    }

                    inRun = match;
                }
            }

            Array.Clear(bgra, (sy * width + l) * 4, (r - l + 1) * 4);
            minX = Math.Min(minX, l);
            maxX = Math.Max(maxX, r);
            minY = Math.Min(minY, sy);
            maxY = Math.Max(maxY, sy);
        }

        return new Int32Rect(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }
}
