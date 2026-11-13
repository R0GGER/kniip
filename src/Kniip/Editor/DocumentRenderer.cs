using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Kniip.Editor.Annotations;

namespace Kniip.Editor;

internal static class DocumentRenderer
{
    public static void Render(DrawingContext dc, EditorDocument document, bool includeBackground = true)
    {
        if (includeBackground && document.HasTransparency)
        {
            document.Background.Render(dc, document.Width, document.Height);
        }

        dc.DrawImage(document.Image, new Rect(0, 0, document.Width, document.Height));
        foreach (Annotation item in document.Items)
        {
            if (!item.IsHidden)
            {
                item.Render(dc, document);
            }
            else if (item is TextAnnotation text)
            {
                text.RenderOutline(dc);
            }
        }
    }

    /// <summary>Maakt de annotaties plat op de originele resolutie van het knipsel.</summary>
    /// <param name="includeBackground">False bij samenvoegen: de achtergrond blijft dan apart instelbaar.</param>
    public static BitmapSource Export(EditorDocument document, bool includeBackground = true)
    {
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.NearestNeighbor);
        TextOptions.SetTextRenderingMode(visual, TextRenderingMode.Grayscale);
        using (DrawingContext dc = visual.RenderOpen())
        {
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, document.Width, document.Height)));
            Render(dc, document, includeBackground);
            dc.Pop();
        }

        var bitmap = new RenderTargetBitmap(document.Width, document.Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }
}
