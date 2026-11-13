using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Kniip.Editor.Annotations;

/// <summary>Vervaagt of pixeleert een gebied. Het effect wordt berekend op de onderliggende afbeelding.</summary>
public sealed class BlurAnnotation : RectAnnotation
{
    private BitmapSource? _cache;
    private (Int32Rect Area, BlurMode Mode, int Strength, BitmapSource Source)? _cacheKey;

    public override ToolKind Kind => ToolKind.Blur;

    public override void Render(DrawingContext dc, EditorDocument document)
    {
        Int32Rect area = ToPixelArea(Rect, document.Width, document.Height);
        if (area.Width < 1 || area.Height < 1)
        {
            return;
        }

        var key = (area, Style.BlurMode, Style.BlurStrength, document.EffectImage);
        if (_cache == null || !_cacheKey.Equals(key))
        {
            _cache = Style.BlurMode == BlurMode.Pixelate
                ? ImageEffects.Pixelate(document, area, Math.Max(2, Style.BlurStrength * 2))
                : ImageEffects.Blur(document, area, Math.Max(1, Style.BlurStrength));
            _cacheKey = key;
        }

        dc.DrawImage(_cache, new Rect(area.X, area.Y, area.Width, area.Height));
    }

    private static Int32Rect ToPixelArea(Rect r, int width, int height)
    {
        int left = Math.Clamp((int)Math.Floor(r.Left), 0, width);
        int top = Math.Clamp((int)Math.Floor(r.Top), 0, height);
        int right = Math.Clamp((int)Math.Ceiling(r.Right), 0, width);
        int bottom = Math.Clamp((int)Math.Ceiling(r.Bottom), 0, height);
        return new Int32Rect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }
}
