using System;
using System.Windows;
using System.Windows.Controls;

namespace Kniip.Editor;

/// <summary>Tabkoppen naast elkaar en even breed; bij weinig ruimte krimpen ze in plaats van een tweede rij te maken.</summary>
public sealed class TabStripPanel : Panel
{
    private const double MaxTabWidth = 240;
    private const double MinTabWidth = 64;

    private double _tabWidth = MaxTabWidth;

    protected override Size MeasureOverride(Size availableSize)
    {
        int count = InternalChildren.Count;
        if (count == 0)
        {
            return new Size();
        }

        _tabWidth = double.IsInfinity(availableSize.Width)
            ? MaxTabWidth
            : Math.Clamp(Math.Floor(availableSize.Width / count), MinTabWidth, MaxTabWidth);

        double height = 0;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(_tabWidth, availableSize.Height));
            height = Math.Max(height, child.DesiredSize.Height);
        }

        return new Size(Math.Min(_tabWidth * count, availableSize.Width), height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double x = 0;
        foreach (UIElement child in InternalChildren)
        {
            child.Arrange(new Rect(x, 0, _tabWidth, finalSize.Height));
            x += _tabWidth;
        }

        return finalSize;
    }
}
