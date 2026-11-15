using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Kniip.Recording;
using Kniip.Settings;

namespace Kniip.Workflows;

/// <summary>Zwevende balk met een knop per workflow; zelf onzichtbaar in knipsels en screencasts.</summary>
public partial class WorkflowBarWindow : Window
{
    private readonly AppSettings _settings;

    internal WorkflowBarWindow(AppSettings settings)
    {
        InitializeComponent();
        Infrastructure.ThemeManager.Attach(this);
        _settings = settings;
        SourceInitialized += (_, _) => PhysicalWindow.ExcludeFromCapture(this);
        ContentRendered += (_, _) => PlaceOnScreen();
    }

    /// <param name="hotkeyText">Sneltoets zoals hij in het menu staat; null = geen.</param>
    internal void ShowWorkflows(IReadOnlyList<WorkflowDefinition> workflows, Func<string?, string?> hotkeyText)
    {
        var buttons = new List<Button>();
        foreach (WorkflowDefinition workflow in workflows)
        {
            Guid id = workflow.Id;
            string tip = workflow.Title;
            if (hotkeyText(workflow.Hotkey) is { } hotkey)
            {
                tip += $" ({hotkey})";
            }

            tip += "\n" + workflow.Summary(_settings.Uploaders);
            var button = new Button
            {
                Content = IconFor(workflow),
                ToolTip = tip,
                MinWidth = 34,
                MinHeight = 34,
                Margin = new Thickness(1, 0, 1, 0)
            };
            button.SetResourceReference(StyleProperty, "FlatButton");
            button.Click += (_, _) => App.Instance.RunWorkflow(id);
            buttons.Add(button);
        }

        WorkflowItems.ItemsSource = buttons;
    }

    private static UIElement IconFor(WorkflowDefinition workflow)
    {
        if (LoadIconFile(workflow.IconFile) is { } image)
        {
            return new Image { Source = image, Width = 18, Height = 18, Stretch = Stretch.Uniform };
        }

        var glyph = new TextBlock { Text = workflow.Glyph, FontSize = 16 };
        glyph.SetResourceReference(StyleProperty, "IconText");
        return glyph;
    }

    /// <summary>Eigen icoon (.png, .ico, ...); null als het niet bestaat of niet te laden is.</summary>
    internal static BitmapSource? LoadIconFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
        {
            return null;
        }

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(path);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 48;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Op de bewaarde plek als die nog op een scherm ligt, anders rechtsboven op het hoofdscherm.</summary>
    private void PlaceOnScreen()
    {
        Rect area = SystemParameters.WorkArea;
        double left = _settings.WorkflowBarLeft ?? area.Right - ActualWidth - 24;
        double top = _settings.WorkflowBarTop ?? area.Top + 24;
        var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        if (!screen.Contains(new Point(left + ActualWidth / 2, top + ActualHeight / 2)))
        {
            left = area.Right - ActualWidth - 24;
            top = area.Top + 24;
        }

        Left = left;
        Top = top;
    }

    private void Grip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        DragMove();
        _settings.WorkflowBarLeft = Left;
        _settings.WorkflowBarTop = Top;
        SettingsStore.Save(_settings);
    }

    private void Settings_Click(object sender, RoutedEventArgs e) => App.Instance.ShowWorkflowSettings();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
