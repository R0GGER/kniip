using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace Kniip.Editor.Annotations;

/// <summary>
/// Een bewerkbaar object op het knipsel. Coördinaten zijn in pixels van de afbeelding;
/// annotaties worden pas bij het exporteren "plat" gemaakt.
/// </summary>
public abstract class Annotation
{
    public StyleSet Style { get; set; } = new();

    /// <summary>Tijdelijk verborgen, bijvoorbeeld terwijl een tekstkader wordt bewerkt.</summary>
    public bool IsHidden { get; set; }

    public abstract ToolKind Kind { get; }

    public abstract Rect Bounds { get; }

    public abstract void Render(DrawingContext dc, EditorDocument document);

    public virtual bool HitTest(Point point, double tolerance)
    {
        Rect bounds = Bounds;
        bounds.Inflate(tolerance, tolerance);
        return bounds.Contains(point);
    }

    public abstract void Move(Vector delta);

    public virtual IReadOnlyList<Point> GetHandles() => Array.Empty<Point>();

    /// <summary>Een greep die pas iets wordt als je hem sleept, zoals het middenpunt om een lijn te buigen.</summary>
    public virtual bool IsGhostHandle(int index) => false;

    public virtual void BeginHandleDrag(int index)
    {
    }

    public virtual void MoveHandle(int index, Point point, bool constrain)
    {
    }

    public virtual bool RemoveHandle(int index) => false;

    /// <summary>False voor objecten die gespiegeld hetzelfde of onleesbaar zouden zijn, zoals rechthoeken en tekst.</summary>
    public virtual bool CanFlip => false;

    /// <summary>Spiegelt het object; de positie mag daarbij verschuiven, de editor zet het daarna terug op zijn plek.</summary>
    public virtual void Flip(bool horizontal)
    {
    }

    protected static Point Mirror(Point p, bool horizontal) => horizontal ? new Point(-p.X, p.Y) : new Point(p.X, -p.Y);

    /// <summary>Te draaien met de draaigreep boven het object.</summary>
    public virtual bool CanRotate => false;

    /// <summary>Draaiing in graden rechtsom, rond het midden van <see cref="Frame"/>.</summary>
    public double Angle { get; set; }

    /// <summary>Het kader van het object zonder draaiing; bij draaibare objecten is <see cref="Bounds"/> het omhullende kader daarvan.</summary>
    public virtual Rect Frame => Bounds;

    public Point Pivot
    {
        get
        {
            Rect f = Frame;
            return new Point(f.X + f.Width / 2, f.Y + f.Height / 2);
        }
    }

    public Matrix RotationMatrix
    {
        get
        {
            Matrix m = Matrix.Identity;
            if (Angle != 0)
            {
                Point c = Pivot;
                m.RotateAt(Angle, c.X, c.Y);
            }

            return m;
        }
    }

    protected Rect RotatedBounds(Rect frame) => Angle == 0 ? frame : Rect.Transform(frame, RotationMatrix);

    /// <summary>Hoek tussen -180 en 180 graden.</summary>
    public static double NormalizeAngle(double angle)
    {
        angle %= 360;
        if (angle > 180)
        {
            angle -= 360;
        }
        else if (angle <= -180)
        {
            angle += 360;
        }

        return Math.Abs(angle) < 1e-9 ? 0 : angle;
    }

    /// <summary>Te klein om te bewaren na het tekenen (bijv. een losse klik met het rechthoek-gereedschap).</summary>
    public virtual bool IsDegenerate => false;

    public virtual Annotation Clone()
    {
        var clone = (Annotation)MemberwiseClone();
        clone.Style = Style.Clone();
        clone.IsHidden = false;
        return clone;
    }
}
