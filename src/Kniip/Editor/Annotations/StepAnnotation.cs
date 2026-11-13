using System;
using System.Text;
using System.Windows;
using System.Windows.Media;

namespace Kniip.Editor.Annotations;

/// <summary>
/// Genummerde stap-badge. Het nummer volgt uit de volgorde van de stappen met dezelfde nummering,
/// dus na verwijderen of ongedaan maken loopt de reeks automatisch weer door.
/// </summary>
public sealed class StepAnnotation : Annotation
{
    public Point Center { get; set; }

    public override ToolKind Kind => ToolKind.Step;

    public override Rect Bounds
    {
        get
        {
            double r = Style.Size / 2;
            return new Rect(Center.X - r, Center.Y - r, Style.Size, Style.Size);
        }
    }

    public override void Render(DrawingContext dc, EditorDocument document)
    {
        double radius = Style.Size / 2;
        var circle = new EllipseGeometry(Center, radius, radius);
        Color fillColor = Style.Fill.A == 0 ? Palette.Red : Style.Fill;
        Pen? border = RenderHelpers.CreatePen(Style.Foreground, Math.Max(1.5, Style.Size / 16), LineDash.Solid);
        RenderHelpers.DrawWithShadow(dc, circle, RenderHelpers.BrushOrNull(fillColor), border, Style.Shadow);

        string label = document.GetStepLabel(this);
        double factor = label.Length switch { 1 => 0.55, 2 => 0.46, 3 => 0.36, _ => 0.3 };
        FormattedText ft = RenderHelpers.FormatText(label, Style.Size * factor, Style.Foreground, true);
        dc.DrawText(ft, new Point(Center.X - ft.Width / 2, Center.Y - ft.Height / 2));
    }

    public override bool HitTest(Point point, double tolerance) =>
        (point - Center).Length <= Style.Size / 2 + tolerance;

    public override void Move(Vector delta) => Center += delta;

    public static string FormatLabel(int ordinal, StepNumbering numbering) => numbering switch
    {
        StepNumbering.LowerAlpha => Alpha(ordinal).ToLowerInvariant(),
        StepNumbering.UpperAlpha => Alpha(ordinal),
        StepNumbering.Roman => Roman(ordinal),
        _ => ordinal.ToString()
    };

    private static string Alpha(int n)
    {
        var sb = new StringBuilder();
        while (n > 0)
        {
            n--;
            sb.Insert(0, (char)('A' + n % 26));
            n /= 26;
        }

        return sb.ToString();
    }

    private static string Roman(int n)
    {
        if (n <= 0 || n > 3999)
        {
            return n.ToString();
        }

        int[] values = { 1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1 };
        string[] symbols = { "M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I" };
        var sb = new StringBuilder();
        for (int i = 0; i < values.Length; i++)
        {
            while (n >= values[i])
            {
                sb.Append(symbols[i]);
                n -= values[i];
            }
        }

        return sb.ToString();
    }
}
