using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Kniip.Resources;

namespace Kniip.Editor;

public partial class ColorPickerButton : UserControl
{
    private Color _color = Colors.Red;
    private bool _allowTransparent;

    public event Action<Color>? ColorChanged;

    public ColorPickerButton()
    {
        InitializeComponent();
        BuildSwatches();
        UpdateSwatch();
    }

    public bool AllowTransparent
    {
        get => _allowTransparent;
        set
        {
            _allowTransparent = value;
            BuildSwatches();
        }
    }

    public Color SelectedColor
    {
        get => _color;
        set
        {
            _color = value;
            UpdateSwatch();
        }
    }

    private void BuildSwatches()
    {
        SwatchGrid.Children.Clear();
        var colors = new List<Color>(Palette.Swatches);
        if (_allowTransparent)
        {
            colors.Add(Colors.Transparent);
        }

        foreach (Color color in colors)
        {
            var visual = new Border
            {
                Width = 22,
                Height = 22,
                CornerRadius = new CornerRadius(4),
                BorderBrush = new SolidColorBrush(Color.FromArgb(40, 0, 0, 0)),
                BorderThickness = new Thickness(1),
                Background = color.A == 0 ? (Brush)FindResource("Checker") : new SolidColorBrush(color)
            };

            var button = new Button
            {
                Style = (Style)FindResource("FlatButton"),
                MinWidth = 0,
                MinHeight = 0,
                Padding = new Thickness(3),
                Margin = new Thickness(1),
                Content = visual,
                ToolTip = color.A == 0 ? Strings.Background_None : $"#{color.R:X2}{color.G:X2}{color.B:X2}"
            };
            Color captured = color;
            button.Click += (_, _) => Pick(captured);
            SwatchGrid.Children.Add(button);
        }
    }

    private void Pick(Color color)
    {
        Toggle.IsChecked = false;
        SelectedColor = color;
        ColorChanged?.Invoke(color);
    }

    private void MoreColors_Click(object sender, RoutedEventArgs e)
    {
        Toggle.IsChecked = false;
        using var dialog = new System.Windows.Forms.ColorDialog
        {
            FullOpen = true,
            AnyColor = true,
            Color = System.Drawing.Color.FromArgb(255, _color.R, _color.G, _color.B)
        };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            Pick(Color.FromRgb(dialog.Color.R, dialog.Color.G, dialog.Color.B));
        }
    }

    /// <summary>Toon de kleur als rand (vierkant met gat) in plaats van als vlak, zoals bij lijnkleur.</summary>
    public bool ShowAsOutline
    {
        get => _showAsOutline;
        set
        {
            _showAsOutline = value;
            UpdateSwatch();
        }
    }

    private bool _showAsOutline;

    /// <summary>Toon een "A" met een kleurbalk eronder, zoals bij tekstkleur.</summary>
    public bool ShowAsText
    {
        get => _showAsText;
        set
        {
            _showAsText = value;
            UpdateSwatch();
        }
    }

    private bool _showAsText;

    private void UpdateSwatch()
    {
        TextGlyph.Visibility = _showAsText ? Visibility.Visible : Visibility.Collapsed;
        TextBar.Background = new SolidColorBrush(_color);
        TextBar.BorderBrush = new SolidColorBrush(Color.FromArgb(50, 0, 0, 0));
        TextBar.BorderThickness = new Thickness(_color.R > 230 && _color.G > 230 && _color.B > 230 ? 1 : 0);
        bool none = _color.A == 0;
        CheckerBack.Visibility = none || _color.A < 255 ? Visibility.Visible : Visibility.Collapsed;
        Swatch.Background = none ? Brushes.Transparent : new SolidColorBrush(_color);
        Swatch.BorderBrush = new SolidColorBrush(Color.FromArgb(40, 0, 0, 0));
        Swatch.BorderThickness = new Thickness(none ? 1 : 0);
        OutlineHole.Visibility = _showAsOutline && !none ? Visibility.Visible : Visibility.Collapsed;
    }
}
