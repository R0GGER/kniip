using System;
using System.Windows;
using System.Windows.Media.Imaging;
using Kniip.Infrastructure;

namespace Kniip.Editor;

internal static class ImageEffects
{
    public static BitmapSource Pixelate(EditorDocument doc, Int32Rect area, int blockSize)
    {
        byte[] src = doc.EffectPixels;
        int srcStride = doc.Width * 4;
        var dst = new byte[area.Width * area.Height * 4];
        int dstStride = area.Width * 4;

        for (int by = 0; by < area.Height; by += blockSize)
        {
            int bh = Math.Min(blockSize, area.Height - by);
            for (int bx = 0; bx < area.Width; bx += blockSize)
            {
                int bw = Math.Min(blockSize, area.Width - bx);
                long b = 0, g = 0, r = 0, a = 0;
                for (int y = 0; y < bh; y++)
                {
                    int row = (area.Y + by + y) * srcStride + (area.X + bx) * 4;
                    for (int x = 0; x < bw; x++)
                    {
                        int i = row + x * 4;
                        int alpha = src[i + 3];
                        b += src[i] * alpha;
                        g += src[i + 1] * alpha;
                        r += src[i + 2] * alpha;
                        a += alpha;
                    }
                }

                int count = bw * bh;
                byte ab = 0, ag = 0, ar = 0;
                if (a > 0)
                {
                    ab = (byte)(b / a);
                    ag = (byte)(g / a);
                    ar = (byte)(r / a);
                }

                byte aa = (byte)(a / count);
                for (int y = 0; y < bh; y++)
                {
                    int row = (by + y) * dstStride + bx * 4;
                    for (int x = 0; x < bw; x++)
                    {
                        int i = row + x * 4;
                        dst[i] = ab;
                        dst[i + 1] = ag;
                        dst[i + 2] = ar;
                        dst[i + 3] = aa;
                    }
                }
            }
        }

        return PixelUtil.CreateBitmap(dst, area.Width, area.Height);
    }

    /// <summary>Drie keer een box blur benadert een Gaussische vervaging.</summary>
    public static BitmapSource Blur(EditorDocument doc, Int32Rect area, int radius)
    {
        // Met marge rondom vervagen, zodat de randen van het gebied natuurlijk overlopen.
        int margin = radius * 3;
        int left = Math.Max(0, area.X - margin);
        int top = Math.Max(0, area.Y - margin);
        int right = Math.Min(doc.Width, area.X + area.Width + margin);
        int bottom = Math.Min(doc.Height, area.Y + area.Height + margin);
        int w = right - left;
        int h = bottom - top;

        var buffer = new byte[w * h * 4];
        int srcStride = doc.Width * 4;
        for (int y = 0; y < h; y++)
        {
            Buffer.BlockCopy(doc.EffectPixels, (top + y) * srcStride + left * 4, buffer, y * w * 4, w * 4);
        }

        // Voorvermenigvuldigd vervagen, anders lopen de (zwarte) kleuren van transparante pixels mee.
        for (int i = 0; i < buffer.Length; i += 4)
        {
            int alpha = buffer[i + 3];
            buffer[i] = (byte)(buffer[i] * alpha / 255);
            buffer[i + 1] = (byte)(buffer[i + 1] * alpha / 255);
            buffer[i + 2] = (byte)(buffer[i + 2] * alpha / 255);
        }

        var temp = new byte[buffer.Length];
        for (int pass = 0; pass < 3; pass++)
        {
            BoxBlurHorizontal(buffer, temp, w, h, radius);
            BoxBlurVertical(temp, buffer, w, h, radius);
        }

        var dst = new byte[area.Width * area.Height * 4];
        int offsetX = area.X - left;
        int offsetY = area.Y - top;
        for (int y = 0; y < area.Height; y++)
        {
            Buffer.BlockCopy(buffer, ((offsetY + y) * w + offsetX) * 4, dst, y * area.Width * 4, area.Width * 4);
        }

        for (int i = 0; i < dst.Length; i += 4)
        {
            int alpha = dst[i + 3];
            if (alpha is > 0 and < 255)
            {
                dst[i] = (byte)Math.Min(255, dst[i] * 255 / alpha);
                dst[i + 1] = (byte)Math.Min(255, dst[i + 1] * 255 / alpha);
                dst[i + 2] = (byte)Math.Min(255, dst[i + 2] * 255 / alpha);
            }
        }

        return PixelUtil.CreateBitmap(dst, area.Width, area.Height);
    }

    private static void BoxBlurHorizontal(byte[] src, byte[] dst, int w, int h, int r)
    {
        int window = 2 * r + 1;
        for (int y = 0; y < h; y++)
        {
            int row = y * w * 4;
            for (int c = 0; c < 4; c++)
            {
                int sum = 0;
                for (int i = -r; i <= r; i++)
                {
                    sum += src[row + Math.Clamp(i, 0, w - 1) * 4 + c];
                }

                for (int x = 0; x < w; x++)
                {
                    dst[row + x * 4 + c] = (byte)(sum / window);
                    int add = Math.Min(x + r + 1, w - 1);
                    int remove = Math.Max(x - r, 0);
                    sum += src[row + add * 4 + c] - src[row + remove * 4 + c];
                }
            }
        }
    }

    private static void BoxBlurVertical(byte[] src, byte[] dst, int w, int h, int r)
    {
        int window = 2 * r + 1;
        int stride = w * 4;
        for (int x = 0; x < w; x++)
        {
            int col = x * 4;
            for (int c = 0; c < 4; c++)
            {
                int sum = 0;
                for (int i = -r; i <= r; i++)
                {
                    sum += src[Math.Clamp(i, 0, h - 1) * stride + col + c];
                }

                for (int y = 0; y < h; y++)
                {
                    dst[y * stride + col + c] = (byte)(sum / window);
                    int add = Math.Min(y + r + 1, h - 1);
                    int remove = Math.Max(y - r, 0);
                    sum += src[add * stride + col + c] - src[remove * stride + col + c];
                }
            }
        }
    }
}
