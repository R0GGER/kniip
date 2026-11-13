using System;
using System.Windows;
using System.Windows.Media;

namespace Kniip.Editor;

public enum ToolKind
{
    Select,
    Rectangle,
    Ellipse,
    Line,
    Arrow,
    Text,
    Step,
    Cursor,
    Emoji,
    Highlight,
    Blur,
    Crop,
    Shape,
    Eraser
}

/// <summary>Smart: klik haalt aaneengesloten dezelfde kleur weg. Brush: sleep om uit de vrije hand weg te poetsen.</summary>
public enum EraserMode { Smart, Brush }

public enum LineDash { Solid, Dash, Dot }

public enum StepNumbering { Numbers, LowerAlpha, UpperAlpha, Roman }

public enum BlurMode { Pixelate, Blur }

public enum CursorKind { Arrow, Hand, Click }

public enum SkinTone { Default, Light, MediumLight, Medium, MediumDark, Dark }

public enum TextShape { Frame, Balloon, Oval, Cloud, None }

public enum ShapeKind { Triangle, BlockArrow, Star, Cloud, Balloon, Heart, Moon, Drop, Egg, Lightning, Spiral }

[Flags]
public enum StyleProps
{
    None = 0,
    Stroke = 1,
    Fill = 2,
    Foreground = 4,
    Thickness = 8,
    Dash = 16,
    Corner = 32,
    Shadow = 64,
    Font = 128,
    Numbering = 256,
    Size = 512,
    CursorKind = 1024,
    Blur = 2048,
    Straight = 4096,
    Emoji = 8192,
    TextShape = 16384,
    ShapeKind = 32768,
    Tolerance = 65536,
    EraserMode = 131072
}

public sealed class StyleSet
{
    public Color Stroke { get; set; } = Palette.Red;
    public Color Fill { get; set; } = Colors.Transparent;
    public Color Foreground { get; set; } = Colors.White;
    public double Thickness { get; set; } = 3;
    public LineDash Dash { get; set; } = LineDash.Solid;
    public double CornerRadius { get; set; }
    public bool Shadow { get; set; }
    public double FontSize { get; set; } = 20;
    public bool Bold { get; set; }

    /// <summary>Uitlijning van de tekst in een tekstvorm: links, midden of rechts.</summary>
    public TextAlignment TextAlign { get; set; } = TextAlignment.Left;
    public StepNumbering StepNumbering { get; set; } = StepNumbering.Numbers;
    public double Size { get; set; } = 32;
    public BlurMode BlurMode { get; set; } = BlurMode.Pixelate;
    public int BlurStrength { get; set; } = 6;

    /// <summary>Hoe ver een kleur (0-100) mag afwijken om bij de smart-gum nog als dezelfde kleur te tellen.</summary>
    public int Tolerance { get; set; } = 32;
    public EraserMode EraserMode { get; set; } = EraserMode.Smart;
    public CursorKind CursorKind { get; set; } = CursorKind.Arrow;
    public TextShape TextShape { get; set; } = TextShape.Frame;
    public ShapeKind ShapeKind { get; set; } = ShapeKind.Triangle;

    /// <summary>Aantal rondjes in de staart van een wolk; 0 = automatisch naar de afstand.</summary>
    public int CloudBubbles { get; set; }

    /// <summary>Unicode-codepunten van de emoji zonder huidskleur, zoals "1f44d".</summary>
    public string Emoji { get; set; } = "1f44d";
    public SkinTone SkinTone { get; set; } = SkinTone.Default;

    public StyleSet Clone() => (StyleSet)MemberwiseClone();

    public static StyleSet DefaultFor(ToolKind tool) => tool switch
    {
        ToolKind.Line or ToolKind.Arrow => new StyleSet { Thickness = 4, Shadow = true },
        ToolKind.Text => new StyleSet
        {
            Stroke = Palette.Red,
            Fill = Colors.White,
            Foreground = Palette.Ink,
            Thickness = 2,
            CornerRadius = 4,
            FontSize = 20,
            Shadow = true
        },
        ToolKind.Step => new StyleSet { Fill = Palette.Red, Foreground = Colors.White, Size = 32, Shadow = true },
        ToolKind.Cursor => new StyleSet { Size = 32, Shadow = true },
        ToolKind.Emoji => new StyleSet { Size = 48 },
        ToolKind.Highlight => new StyleSet { Stroke = Palette.Yellow, Thickness = 20 },
        ToolKind.Blur => new StyleSet { BlurMode = BlurMode.Pixelate, BlurStrength = 6 },
        ToolKind.Eraser => new StyleSet { EraserMode = EraserMode.Smart, Thickness = 20, Tolerance = 32 },
        _ => new StyleSet()
    };

    public static StyleProps PropsFor(ToolKind tool) => tool switch
    {
        ToolKind.Rectangle => StyleProps.Stroke | StyleProps.Fill | StyleProps.Thickness | StyleProps.Dash | StyleProps.Corner | StyleProps.Shadow,
        ToolKind.Ellipse => StyleProps.Stroke | StyleProps.Fill | StyleProps.Thickness | StyleProps.Dash | StyleProps.Shadow,
        ToolKind.Shape => StyleProps.Stroke | StyleProps.Fill | StyleProps.Thickness | StyleProps.Dash | StyleProps.Shadow | StyleProps.ShapeKind,
        ToolKind.Line or ToolKind.Arrow => StyleProps.Stroke | StyleProps.Thickness | StyleProps.Dash | StyleProps.Shadow,
        ToolKind.Text => StyleProps.Stroke | StyleProps.Fill | StyleProps.Foreground | StyleProps.Thickness | StyleProps.Dash
                         | StyleProps.Corner | StyleProps.Shadow | StyleProps.Font | StyleProps.TextShape,
        ToolKind.Step => StyleProps.Fill | StyleProps.Foreground | StyleProps.Numbering | StyleProps.Size | StyleProps.Shadow,
        ToolKind.Cursor => StyleProps.CursorKind | StyleProps.Size | StyleProps.Shadow,
        ToolKind.Emoji => StyleProps.Emoji | StyleProps.Size | StyleProps.Shadow,
        ToolKind.Highlight => StyleProps.Stroke | StyleProps.Thickness | StyleProps.Straight,
        ToolKind.Blur => StyleProps.Blur,
        ToolKind.Eraser => StyleProps.EraserMode | StyleProps.Thickness | StyleProps.Tolerance,
        _ => StyleProps.None
    };
}

public static class Palette
{
    public static readonly Color Red = Color.FromRgb(0xE8, 0x11, 0x23);
    public static readonly Color Yellow = Color.FromRgb(0xFF, 0xE6, 0x00);
    public static readonly Color Ink = Color.FromRgb(0x1A, 0x1A, 0x1A);
    public static readonly Color Accent = Color.FromRgb(0x00, 0x67, 0xC0);

    public static readonly Color[] Swatches =
    {
        Red,
        Color.FromRgb(0xF7, 0x63, 0x0C),
        Color.FromRgb(0xFF, 0xB9, 0x00),
        Yellow,
        Color.FromRgb(0x16, 0xC6, 0x0C),
        Color.FromRgb(0x00, 0xB7, 0xC3),
        Color.FromRgb(0x00, 0x78, 0xD4),
        Color.FromRgb(0x87, 0x64, 0xB8),
        Color.FromRgb(0xE3, 0x00, 0x8C),
        Colors.Black,
        Color.FromRgb(0x50, 0x50, 0x50),
        Color.FromRgb(0xA0, 0xA0, 0xA0),
        Colors.White
    };
}
