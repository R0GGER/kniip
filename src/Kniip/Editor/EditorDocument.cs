using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Kniip.Editor.Annotations;
using Kniip.Infrastructure;

namespace Kniip.Editor;

public sealed record DocumentState(BitmapSource Image, byte[] Pixels, List<Annotation> Items, CanvasBackground Background);

/// <summary>Het knipsel (als Bgra32-pixels, 96 dpi zodat 1 DIP = 1 pixel) plus de annotaties erop.</summary>
public sealed class EditorDocument
{
    private (BitmapSource Image, byte[] Pixels)? _composite;
    private CanvasBackground _background = CanvasBackground.Default;

    /// <param name="isBlank">Leeg canvas: volledig transparant laten in plaats van als alpha-loze afbeelding te behandelen.</param>
    public EditorDocument(BitmapSource source, bool isBlank = false)
    {
        byte[] pixels = isBlank || !PixelUtil.IsFullyTransparent(source)
            ? PixelUtil.ToBgra(source)
            : PixelUtil.ToOpaqueBgra(source);
        SetPixels(pixels, source.PixelWidth, source.PixelHeight);
    }

    public BitmapSource Image { get; private set; } = null!;

    public byte[] Pixels { get; private set; } = Array.Empty<byte>();

    public int Width => Image.PixelWidth;

    public int Height => Image.PixelHeight;

    /// <summary>Of ergens doorheen te kijken is; alleen dan heeft een achtergrond zin.</summary>
    public bool HasTransparency { get; private set; }

    public List<Annotation> Items { get; private set; } = new();

    public CanvasBackground Background
    {
        get => _background;
        set
        {
            _background = value;
            _composite = null;
        }
    }

    /// <summary>Achtergrond plus knipsel zonder annotaties: de bron voor vervagen en pixeleren.</summary>
    public BitmapSource EffectImage => Composite().Image;

    public byte[] EffectPixels => Composite().Pixels;

    private void SetPixels(byte[] pixels, int width, int height)
    {
        Image = PixelUtil.CreateBitmap(pixels, width, height);
        Pixels = pixels;
        HasTransparency = PixelUtil.HasTransparency(pixels);
        _composite = null;
    }

    private (BitmapSource Image, byte[] Pixels) Composite()
    {
        if (_composite is { } cached)
        {
            return cached;
        }

        if (!HasTransparency || !Background.IsVisible)
        {
            _composite = (Image, Pixels);
            return _composite.Value;
        }

        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
        {
            Background.Render(dc, Width, Height);
            dc.DrawImage(Image, new Rect(0, 0, Width, Height));
        }

        var bitmap = new RenderTargetBitmap(Width, Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        byte[] pixels = PixelUtil.ToBgra(bitmap);
        _composite = (PixelUtil.CreateBitmap(pixels, Width, Height), pixels);
        return _composite.Value;
    }

    public DocumentState CaptureState() => new(Image, Pixels, Items.Select(i => i.Clone()).ToList(), Background);

    public void Restore(DocumentState state)
    {
        Image = state.Image;
        Pixels = state.Pixels;
        HasTransparency = PixelUtil.HasTransparency(state.Pixels);
        Items = state.Items.Select(i => i.Clone()).ToList();
        Background = state.Background;
    }

    public void Crop(Int32Rect area)
    {
        var cropped = new byte[area.Width * area.Height * 4];
        for (int y = 0; y < area.Height; y++)
        {
            Buffer.BlockCopy(Pixels, ((area.Y + y) * Width + area.X) * 4, cropped, y * area.Width * 4, area.Width * 4);
        }

        SetPixels(cropped, area.Width, area.Height);
        var offset = new Vector(-area.X, -area.Y);
        foreach (Annotation item in Items)
        {
            item.Move(offset);
        }
    }

    /// <summary>Vervangt het knipsel door de platgemaakte versie; de annotaties zitten daarna in de pixels.</summary>
    public void Flatten(BitmapSource rendered)
    {
        SetPixels(PixelUtil.ToBgra(rendered), rendered.PixelWidth, rendered.PixelHeight);
        Items = new List<Annotation>();
    }

    public bool IsEditingPixels => Image is WriteableBitmap { IsFrozen: false };

    /// <summary>
    /// Begint een bewerking op de pixels zelf. De pixels worden gekloond, want undo-snapshots verwijzen naar het oude array.
    /// </summary>
    public void BeginPixelEdit()
    {
        if (IsEditingPixels)
        {
            return;
        }

        Pixels = (byte[])Pixels.Clone();
        var bitmap = new WriteableBitmap(Width, Height, 96, 96, PixelFormats.Bgra32, null);
        bitmap.WritePixels(new Int32Rect(0, 0, Width, Height), Pixels, Width * 4, 0);
        Image = bitmap;
        _composite = null;
    }

    /// <summary>Zet een gewijzigd gebied van Pixels door naar de afbeelding tijdens een pixelbewerking.</summary>
    public void InvalidatePixels(Int32Rect area)
    {
        if (Image is not WriteableBitmap { IsFrozen: false } bitmap || area.Width <= 0 || area.Height <= 0)
        {
            return;
        }

        bitmap.WritePixels(area, Pixels, Width * 4, area.X, area.Y);
        HasTransparency = true;
        _composite = null;
    }

    public void EndPixelEdit()
    {
        if (IsEditingPixels)
        {
            SetPixels(Pixels, Width, Height);
        }
    }

    public string GetStepLabel(StepAnnotation step)
    {
        int ordinal = 0;
        foreach (Annotation item in Items)
        {
            if (item is StepAnnotation s && s.Style.StepNumbering == step.Style.StepNumbering)
            {
                ordinal++;
                if (ReferenceEquals(s, step))
                {
                    break;
                }
            }
        }

        return StepAnnotation.FormatLabel(Math.Max(1, ordinal), step.Style.StepNumbering);
    }
}
