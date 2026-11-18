using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Kniip.Resources;
using Kniip.Watermark;

namespace Kniip.Settings;

/// <summary>Het tabblad Watermerk: tekst of afbeelding, plek, grootte en patroon, met een live voorbeeld.</summary>
public partial class SettingsWindow
{
    private string _watermarkImage = string.Empty;
    private bool _fillingWatermark;
    private bool? _watermarkUsable;
    private BitmapSource? _watermarkSample;

    private void InitializeWatermark()
    {
        WatermarkFontCombo.ItemsSource = Fonts.SystemFontFamilies
            .Select(f => f.Source)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        WatermarkColorPicker.ColorChanged += _ =>
        {
            ScheduleSave();
            ShowWatermark();
        };

        WatermarkPage.AddHandler(ToggleButton.CheckedEvent, new RoutedEventHandler((_, _) => ShowWatermark()), true);
        WatermarkPage.AddHandler(ToggleButton.UncheckedEvent, new RoutedEventHandler((_, _) => ShowWatermark()), true);
        WatermarkPage.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,
            new TextChangedEventHandler((_, _) => ShowWatermark()), true);
        WatermarkPage.AddHandler(Selector.SelectionChangedEvent, new SelectionChangedEventHandler((_, _) => ShowWatermark()), true);
        WatermarkPage.AddHandler(RangeBase.ValueChangedEvent, new RoutedPropertyChangedEventHandler<double>((_, _) => ShowWatermark()), true);
    }

    public void ShowWatermarkTab() => WatermarkTab.IsSelected = true;

    private void LoadWatermark(WatermarkSettings watermark)
    {
        _fillingWatermark = true;
        try
        {
            WatermarkTextRadio.IsChecked = watermark.Kind == WatermarkKind.Text;
            WatermarkImageRadio.IsChecked = watermark.Kind == WatermarkKind.Image;
            WatermarkTextBox.Text = watermark.Text;
            WatermarkFontCombo.SelectedItem = WatermarkFontCombo.Items.Cast<string>()
                                                  .FirstOrDefault(f => string.Equals(f, watermark.FontFamily, StringComparison.OrdinalIgnoreCase))
                                              ?? "Segoe UI";
            WatermarkBoldToggle.IsChecked = watermark.Bold;
            WatermarkColorPicker.SelectedColor = watermark.Color;
            WatermarkShadowBox.IsChecked = watermark.Shadow;
            _watermarkImage = watermark.ImagePath;
            foreach (RadioButton radio in WatermarkPositionGrid.Children.OfType<RadioButton>())
            {
                radio.IsChecked = (string)radio.Tag == watermark.Position.ToString();
            }

            WatermarkSizeSlider.Value = watermark.SizePercent;
            WatermarkMarginSlider.Value = watermark.MarginPercent;
            WatermarkOpacitySlider.Value = watermark.Opacity;
            WatermarkPatternBox.IsChecked = watermark.Pattern;
            WatermarkSpacingSlider.Value = watermark.PatternSpacing;
            WatermarkAngleSlider.Value = watermark.PatternAngle;
        }
        finally
        {
            _fillingWatermark = false;
        }

        ShowWatermark();
    }

    private WatermarkSettings ReadWatermark()
    {
        WatermarkPosition position = WatermarkPositionGrid.Children.OfType<RadioButton>()
            .Where(r => r.IsChecked == true)
            .Select(r => Enum.TryParse((string)r.Tag, out WatermarkPosition p) ? p : WatermarkPosition.BottomRight)
            .DefaultIfEmpty(WatermarkPosition.BottomRight)
            .First();
        return new WatermarkSettings
        {
            Kind = WatermarkImageRadio.IsChecked == true ? WatermarkKind.Image : WatermarkKind.Text,
            Text = WatermarkTextBox.Text,
            FontFamily = WatermarkFontCombo.SelectedItem as string ?? "Segoe UI",
            Bold = WatermarkBoldToggle.IsChecked == true,
            Color = WatermarkColorPicker.SelectedColor,
            Shadow = WatermarkShadowBox.IsChecked == true,
            ImagePath = _watermarkImage,
            Position = position,
            SizePercent = WatermarkSizeSlider.Value,
            MarginPercent = WatermarkMarginSlider.Value,
            Opacity = (int)WatermarkOpacitySlider.Value,
            Pattern = WatermarkPatternBox.IsChecked == true,
            PatternSpacing = WatermarkSpacingSlider.Value,
            PatternAngle = WatermarkAngleSlider.Value
        };
    }

    /// <summary>Werkt de zichtbare onderdelen, de waarden naast de schuifregelaars en het voorbeeld bij.</summary>
    private void ShowWatermark()
    {
        if (_fillingWatermark || WatermarkPreview == null)
        {
            return;
        }

        WatermarkSettings watermark = ReadWatermark();
        bool image = watermark.Kind == WatermarkKind.Image;
        WatermarkTextPanel.Visibility = image ? Visibility.Collapsed : Visibility.Visible;
        WatermarkImagePanel.Visibility = image ? Visibility.Visible : Visibility.Collapsed;
        WatermarkImageText.Text = _watermarkImage.Length > 0 ? _watermarkImage : Strings.Watermark_NoImage;
        WatermarkImageText.ToolTip = _watermarkImage.Length > 0 ? _watermarkImage : null;
        WatermarkImageWarning.Visibility = image && _watermarkImage.Length > 0 && WatermarkRenderer.LoadImage(_watermarkImage) == null
            ? Visibility.Visible
            : Visibility.Collapsed;

        WatermarkPositionGrid.IsEnabled = !watermark.Pattern;
        WatermarkMarginSlider.IsEnabled = !watermark.Pattern;
        WatermarkPatternPanel.IsEnabled = watermark.Pattern;
        WatermarkSizeText.Text = Percent(watermark.SizePercent);
        WatermarkMarginText.Text = Percent(watermark.MarginPercent);
        WatermarkOpacityText.Text = Percent(watermark.Opacity);
        WatermarkSpacingText.Text = Percent(watermark.PatternSpacing);
        WatermarkAngleText.Text = string.Format(CultureInfo.CurrentCulture, "{0:0}°", watermark.PatternAngle);

        _watermarkSample ??= CreateWatermarkSample();
        WatermarkPreview.Source = WatermarkRenderer.Apply(_watermarkSample, watermark);

        bool usable = WatermarkRenderer.IsUsable(watermark);
        if (usable != _watermarkUsable)
        {
            _watermarkUsable = usable;
            ShowWorkflows();
            ShowWorkflowProblems();
        }
    }

    private static string Percent(double value) => string.Format(CultureInfo.CurrentCulture, "{0:0}%", value);

    private void WatermarkBrowse_Click(object sender, RoutedEventArgs e)
    {
        if (App.Instance.PickImageFile(this) is not { } path)
        {
            return;
        }

        if (WatermarkRenderer.LoadImage(path) == null)
        {
            MessageBox.Show(this, string.Format(Strings.Common_CannotOpenFile, Path.GetFileName(path), Strings.Watermark_ImageUnreadable),
                "kniip", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _watermarkImage = path;
        WatermarkImageRadio.IsChecked = true;
        ShowWatermark();
        ScheduleSave();
    }

    /// <summary>Een neutraal nepscherm met een venster en wat tekstregels, om het watermerk op te bekijken.</summary>
    private static BitmapSource CreateWatermarkSample()
    {
        const int width = 640, height = 360;
        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
        {
            var background = new LinearGradientBrush(Color.FromRgb(0x3A, 0x6E, 0xA5), Color.FromRgb(0x8E, 0xB8, 0xD8), 45);
            dc.DrawRectangle(background, null, new Rect(0, 0, width, height));
            var window = new Rect(70, 50, 500, 260);
            dc.DrawRoundedRectangle(Brushes.White, null, window, 8, 8);
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xEE)), null, new Rect(window.X, window.Y + 8, window.Width, 26));
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xEE)), null, new Rect(window.X, window.Y, window.Width, 30), 8, 8);
            var line = new SolidColorBrush(Color.FromRgb(0xD5, 0xD9, 0xDE));
            for (int i = 0; i < 7; i++)
            {
                double lineWidth = i % 3 == 2 ? 260 : 420;
                dc.DrawRoundedRectangle(line, null, new Rect(window.X + 30, window.Y + 60 + i * 27, lineWidth, 10), 5, 5);
            }
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }
}
