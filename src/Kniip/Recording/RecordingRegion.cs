using System;
using System.Drawing;

namespace Kniip.Recording;

/// <summary>Opnamegebied in fysieke schermpixels (bureaubladcoördinaten) binnen één beeldscherm.</summary>
internal sealed record RecordingRegion(string DeviceName, Rectangle Bounds, Rectangle MonitorBounds)
{
    /// <summary>H.264 vereist even afmetingen; het gebied wordt daarom zo nodig 1 pixel kleiner.</summary>
    public static RecordingRegion Create(string deviceName, Rectangle bounds, Rectangle monitorBounds)
    {
        Rectangle clipped = Rectangle.Intersect(bounds, monitorBounds);
        int width = Math.Max(2, clipped.Width & ~1);
        int height = Math.Max(2, clipped.Height & ~1);
        return new RecordingRegion(deviceName, new Rectangle(clipped.X, clipped.Y, width, height), monitorBounds);
    }

    /// <summary>Gebied ten opzichte van de linkerbovenhoek van het beeldscherm.</summary>
    public Rectangle MonitorRelative => new(Bounds.X - MonitorBounds.X, Bounds.Y - MonitorBounds.Y, Bounds.Width, Bounds.Height);
}
