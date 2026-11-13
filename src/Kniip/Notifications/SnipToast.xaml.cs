using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Kniip.Notifications;

/// <summary>
/// Kleine melding rechtsonder na een nieuw knipsel. Er is er altijd maar één; een nieuw knipsel vervangt de vorige.
/// </summary>
public partial class SnipToast : Window
{
    private static SnipToast? _current;

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(8) };
    private Action? _onEdit;

    private SnipToast()
    {
        InitializeComponent();
        _timer.Tick += (_, _) =>
        {
            if (!IsMouseOver)
            {
                Close();
            }
        };
        SizeChanged += (_, _) => PlaceBottomRight();
        Closed += (_, _) =>
        {
            _timer.Stop();
            if (ReferenceEquals(_current, this))
            {
                _current = null;
            }
        };
    }

    public static void ShowFor(BitmapSource image, Action onEdit)
    {
        _current ??= new SnipToast();
        _current.Update(image, onEdit);
    }

    private void Update(BitmapSource image, Action onEdit)
    {
        _onEdit = onEdit;
        Thumbnail.Source = image;
        DetailText.Text = $"{image.PixelWidth} × {image.PixelHeight} px  ·  klik om aantekeningen toe te voegen";
        if (!IsVisible)
        {
            Show();
        }

        PlaceBottomRight();
        _timer.Stop();
        _timer.Start();
    }

    private void PlaceBottomRight()
    {
        Rect area = SystemParameters.WorkArea;
        Left = area.Right - ActualWidth;
        Top = area.Bottom - ActualHeight;
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        Action? onEdit = _onEdit;
        Close();
        onEdit?.Invoke();
    }

    private void Thumbnail_Click(object sender, MouseButtonEventArgs e) => Edit_Click(sender, e);

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
