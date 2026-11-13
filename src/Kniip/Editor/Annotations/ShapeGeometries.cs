using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace Kniip.Editor.Annotations;

/// <summary>Vormen voor het vormengereedschap, ontworpen in een raster van 100 x 100 en bij het tekenen naar het kader geschaald.</summary>
internal static class ShapeGeometries
{
    private static readonly Dictionary<ShapeKind, Geometry> Cache = new();

    public static bool IsOpen(ShapeKind kind) => kind == ShapeKind.Spiral;

    public static Geometry For(ShapeKind kind)
    {
        if (!Cache.TryGetValue(kind, out Geometry? geometry))
        {
            geometry = RenderHelpers.Freeze(Create(kind));
            Cache[kind] = geometry;
        }

        return geometry;
    }

    private static Geometry Create(ShapeKind kind) => kind switch
    {
        ShapeKind.BlockArrow => Geometry.Parse("M0,30 L58,30 L58,4 L100,50 L58,96 L58,70 L0,70 Z"),
        ShapeKind.Star => Star(),
        ShapeKind.Cloud => Cloud(),
        ShapeKind.Balloon => Balloon(),
        ShapeKind.Heart => Geometry.Parse(
            "M50,95 C14,68 0,48 0,28 C0,11 13,0 28,0 C39,0 46,7 50,15 C54,7 61,0 72,0 C87,0 100,11 100,28 C100,48 86,68 50,95 Z"),
        ShapeKind.Moon => Geometry.Combine(
            new EllipseGeometry(new Point(50, 50), 50, 50),
            new EllipseGeometry(new Point(74, 38), 42, 42),
            GeometryCombineMode.Exclude, null, 0.01, ToleranceType.Absolute),
        ShapeKind.Drop => Geometry.Parse("M50,0 C58,16 85,44 85,65 A35,35 0 1 1 15,65 C15,44 42,16 50,0 Z"),
        ShapeKind.Egg => Geometry.Parse("M50,0 C77,0 94,38 94,62 C94,86 74,100 50,100 C26,100 6,86 6,62 C6,38 23,0 50,0 Z"),
        ShapeKind.Lightning => Geometry.Parse("M58,0 L14,56 L44,56 L30,100 L86,38 L56,38 L74,0 Z"),
        ShapeKind.Spiral => Spiral(),
        _ => Geometry.Parse("M50,0 L100,100 L0,100 Z")
    };

    private static Geometry Star()
    {
        var points = new Point[10];
        for (int i = 0; i < points.Length; i++)
        {
            double angle = -Math.PI / 2 + i * Math.PI / 5;
            double radius = i % 2 == 0 ? 50 : 20;
            points[i] = new Point(50 + radius * Math.Cos(angle), 50 + radius * Math.Sin(angle));
        }

        return Polygon(points, closed: true);
    }

    private static Geometry Cloud()
    {
        var center = new Point(50, 50);
        const double rx = 33, ry = 19, bump = 19;
        const int count = 6;
        Geometry cloud = new EllipseGeometry(center, rx, ry);
        for (int i = 0; i < count; i++)
        {
            double angle = 2 * Math.PI * (i + 0.5) / count;
            var p = new Point(center.X + rx * Math.Cos(angle), center.Y + ry * Math.Sin(angle));
            cloud = Geometry.Combine(cloud, new EllipseGeometry(p, bump, bump), GeometryCombineMode.Union, null, 0.01, ToleranceType.Absolute);
        }

        return cloud;
    }

    private static Geometry Balloon() =>
        Geometry.Combine(
            new RectangleGeometry(new Rect(0, 0, 100, 72), 14, 14),
            Geometry.Parse("M20,68 L10,100 L42,68 Z"),
            GeometryCombineMode.Union, null, 0.01, ToleranceType.Absolute);

    private static Geometry Spiral()
    {
        const double turns = 3;
        const int steps = 240;
        double maxAngle = turns * 2 * Math.PI;
        var points = new Point[steps + 1];
        for (int i = 0; i <= steps; i++)
        {
            double angle = maxAngle * i / steps;
            double radius = 50 * angle / maxAngle;
            points[i] = new Point(50 + radius * Math.Cos(angle), 50 + radius * Math.Sin(angle));
        }

        return Polygon(points, closed: false);
    }

    private static Geometry Polygon(IReadOnlyList<Point> points, bool closed)
    {
        var geometry = new StreamGeometry();
        using (StreamGeometryContext ctx = geometry.Open())
        {
            ctx.BeginFigure(points[0], closed, closed);
            for (int i = 1; i < points.Count; i++)
            {
                ctx.LineTo(points[i], true, true);
            }
        }

        return geometry;
    }
}
