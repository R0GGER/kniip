using System;
using System.Windows.Media;

namespace Kniip.Watermark;

public enum WatermarkKind
{
    Text,
    Image
}

/// <summary>Plek in een raster van 3 bij 3 over het beeld; rij voor rij van linksboven naar rechtsonder.</summary>
public enum WatermarkPosition
{
    TopLeft,
    TopCenter,
    TopRight,
    MiddleLeft,
    Center,
    MiddleRight,
    BottomLeft,
    BottomCenter,
    BottomRight
}

/// <summary>
/// Een watermerk van tekst of een afbeelding. Grootte en marge zijn percentages van het beeld,
/// zodat een klein knipsel en een 4K-screencast er hetzelfde uitzien.
/// </summary>
public sealed class WatermarkSettings
{
    public WatermarkKind Kind { get; set; } = WatermarkKind.Text;

    public string Text { get; set; } = "© " + Environment.UserName;

    public string FontFamily { get; set; } = "Segoe UI";

    public bool Bold { get; set; } = true;

    public Color Color { get; set; } = Colors.White;

    /// <summary>Zachte donkere schaduw achter de tekst, zodat die ook op een lichte achtergrond leesbaar blijft.</summary>
    public bool Shadow { get; set; } = true;

    /// <summary>Afbeelding (bijvoorbeeld een logo met transparantie) bij <see cref="WatermarkKind.Image"/>.</summary>
    public string ImagePath { get; set; } = string.Empty;

    public WatermarkPosition Position { get; set; } = WatermarkPosition.BottomRight;

    private double _sizePercent = 20;

    /// <summary>Breedte van het watermerk als percentage van de breedte van het beeld (2-100).</summary>
    public double SizePercent
    {
        get => _sizePercent;
        set => _sizePercent = Math.Clamp(value, 2, 100);
    }

    private double _marginPercent = 3;

    /// <summary>Afstand tot de rand als percentage van de kortste zijde van het beeld (0-25).</summary>
    public double MarginPercent
    {
        get => _marginPercent;
        set => _marginPercent = Math.Clamp(value, 0, 25);
    }

    private int _opacity = 70;

    /// <summary>Dekking in procenten (5-100).</summary>
    public int Opacity
    {
        get => _opacity;
        set => _opacity = Math.Clamp(value, 5, 100);
    }

    /// <summary>Het watermerk herhalen over het hele beeld in plaats van één keer op <see cref="Position"/>.</summary>
    public bool Pattern { get; set; }

    private double _patternSpacing = 100;

    /// <summary>Ruimte tussen de herhalingen als percentage van de grootte van het watermerk (0-300).</summary>
    public double PatternSpacing
    {
        get => _patternSpacing;
        set => _patternSpacing = Math.Clamp(value, 0, 300);
    }

    private double _patternAngle = -30;

    /// <summary>Draaiing van het patroon in graden (-90 tot 90).</summary>
    public double PatternAngle
    {
        get => _patternAngle;
        set => _patternAngle = Math.Clamp(value, -90, 90);
    }

    public WatermarkSettings Clone() => (WatermarkSettings)MemberwiseClone();
}
