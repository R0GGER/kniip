using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Kniip.Resources;

namespace Kniip.Editor;

/// <summary>
/// Richting van een verloop: een cirkel met een knop die je rondsleept, plus een hoekveld en "vanuit het midden".
/// Hoeken in graden met de klok mee vanaf boven, zoals <see cref="CanvasBackground.Angle"/>.
/// </summary>
public partial class GradientAngleDial : UserControl
{
    private const double Center = 70;
    private const double Radius = 50;

    private double _angle = 180;
    private bool _isRadial;
    private bool _dragging;

    /// <summary>Tijdens het slepen, voor een live voorbeeld; nog geen stap in Ongedaan maken.</summary>
    public event Action<double>? AngleChanging;

    /// <summary>Definitieve hoek: bij loslaten, scrollen of typen. Kiest ook een lineair verloop.</summary>
    public event Action<double>? AngleCommitted;

    /// <summary>"Vanuit het midden" aan- of uitgezet.</summary>
    public event Action<bool>? RadialChanged;

    public GradientAngleDial()
    {
        InitializeComponent();
        BuildTicks();
        UpdateVisuals();
    }

    public double Angle
    {
        get => _angle;
        set
        {
            _angle = Normalize(value);
            UpdateVisuals();
        }
    }

    public bool IsRadial
    {
        get => _isRadial;
        set
        {
            _isRadial = value;
            UpdateVisuals();
        }
    }

    private static double Normalize(double angle)
    {
        angle %= 360;
        if (angle < 0)
        {
            angle += 360;
        }

        return Math.Round(angle, 1) >= 360 ? 0 : Math.Round(angle, 1);
    }

    private static Vector Direction(double angle)
    {
        double radians = angle * Math.PI / 180;
        return new Vector(Math.Sin(radians), -Math.Cos(radians));
    }

    private void BuildTicks()
    {
        for (int i = 0; i < 8; i++)
        {
            Vector d = Direction(i * 45);
            double inner = Radius + 4, outer = Radius + (i % 2 == 0 ? 11 : 8);
            Ticks.Children.Add(new Line
            {
                X1 = Center + d.X * inner,
                Y1 = Center + d.Y * inner,
                X2 = Center + d.X * outer,
                Y2 = Center + d.Y * outer,
                StrokeThickness = 1.5,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            });
            ((Line)Ticks.Children[i]).SetResourceReference(Shape.StrokeProperty, "SubtleTextBrush");
        }
    }

    private void UpdateVisuals()
    {
        Vector d = Direction(_angle);
        Point knob = new Point(Center, Center) + d * Radius;
        Canvas.SetLeft(Knob, knob.X - Knob.Width / 2);
        Canvas.SetTop(Knob, knob.Y - Knob.Height / 2);
        Needle.X2 = knob.X;
        Needle.Y2 = knob.Y;
        DialCanvas.Opacity = _isRadial ? 0.45 : 1;

        SmallRotate.Angle = _angle;
        SmallArrow.Visibility = _isRadial ? Visibility.Collapsed : Visibility.Visible;
        SmallRadial.Visibility = _isRadial ? Visibility.Visible : Visibility.Collapsed;
        RadialToggle.IsChecked = _isRadial;
        if (!AngleBox.IsKeyboardFocusWithin)
        {
            AngleBox.Text = _angle.ToString("0.#", CultureInfo.CurrentCulture);
        }

        Toggle.ToolTip = _isRadial
            ? Strings.Gradient_DirectionRadial
            : string.Format(Strings.Gradient_Direction, _angle.ToString("0.#", CultureInfo.CurrentCulture));
    }

    private double AngleAt(Point p)
    {
        Vector v = p - new Point(Center, Center);
        if (v.Length < 1)
        {
            return _angle;
        }

        double angle = Normalize(Math.Atan2(v.X, -v.Y) * 180 / Math.PI);
        ModifierKeys modifiers = Keyboard.Modifiers;
        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            return Normalize(Math.Round(angle));
        }

        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            return Normalize(Math.Round(angle / 15) * 15);
        }

        double nearest = Math.Round(angle / 45) * 45;
        return Normalize(Math.Abs(angle - nearest) <= 6 ? nearest : Math.Round(angle));
    }

    private void Dial_MouseDown(object sender, MouseButtonEventArgs e)
    {
        double angle = AngleAt(e.GetPosition(DialCanvas));
        _dragging = DialCanvas.CaptureMouse();
        if (_dragging)
        {
            Preview(angle);
        }
        else
        {
            Commit(angle);
        }

        e.Handled = true;
    }

    private void Dial_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragging)
        {
            Preview(AngleAt(e.GetPosition(DialCanvas)));
        }
    }

    private void Dial_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragging)
        {
            DialCanvas.ReleaseMouseCapture();
        }
    }

    private void Dial_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        Commit(_angle);
    }

    private void Dial_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        double step = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) ? 1 : 15;
        double angle = e.Delta > 0 ? _angle + step : _angle - step;
        Commit(step == 15 ? Math.Round(angle / 15) * 15 : angle);
        e.Handled = true;
    }

    private void Preview(double angle)
    {
        _isRadial = false;
        Angle = angle;
        AngleChanging?.Invoke(_angle);
    }

    private void Commit(double angle)
    {
        _isRadial = false;
        Angle = angle;
        AngleCommitted?.Invoke(_angle);
    }

    private void AngleBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ApplyTypedAngle();
            AngleBox.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            AngleBox.Text = _angle.ToString("0.#", CultureInfo.CurrentCulture);
            Toggle.IsChecked = false;
            e.Handled = true;
        }
    }

    private void AngleBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => ApplyTypedAngle();

    private void ApplyTypedAngle()
    {
        string text = AngleBox.Text.Trim().TrimEnd('°');
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out double angle)
            || double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out angle))
        {
            if (Normalize(angle) != _angle || _isRadial)
            {
                Commit(angle);
                return;
            }
        }

        AngleBox.Text = _angle.ToString("0.#", CultureInfo.CurrentCulture);
    }

    private void Radial_Click(object sender, RoutedEventArgs e)
    {
        _isRadial = RadialToggle.IsChecked == true;
        UpdateVisuals();
        RadialChanged?.Invoke(_isRadial);
    }
}
