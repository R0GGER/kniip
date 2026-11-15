using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Kniip.Infrastructure;
using Kniip.Resources;

namespace Kniip.Upload;

/// <summary>De vensters achter {select}, {inputbox} en {outputbox}; altijd op de UI-thread.</summary>
internal static class UploadPrompts
{
    public static string Select(IReadOnlyList<string> options) => OnUiThread(() =>
    {
        string? chosen = null;
        var panel = new StackPanel { Margin = new Thickness(20, 18, 20, 16), MinWidth = 320 };
        panel.Children.Add(new TextBlock { Text = Strings.Upload_SelectPrompt, Margin = new Thickness(0, 0, 0, 10), TextWrapping = TextWrapping.Wrap });
        Window window = CreateWindow(Strings.Upload_SelectTitle, panel);
        foreach (string option in options)
        {
            var button = new Button
            {
                Content = new TextBlock { Text = option, TextTrimming = TextTrimming.CharacterEllipsis },
                Style = (Style)Application.Current.Resources["SecondaryButton"],
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 6)
            };
            button.Click += (_, _) =>
            {
                chosen = option;
                window.DialogResult = true;
            };
            panel.Children.Add(button);
        }

        return window.ShowDialog() == true && chosen != null ? chosen : throw new OperationCanceledException();
    });

    public static string Input(string title, string defaultText) => OnUiThread(() =>
    {
        var panel = new StackPanel { Margin = new Thickness(20, 18, 20, 16), MinWidth = 360 };
        var box = new TextBox { Text = defaultText, Height = 26, VerticalContentAlignment = VerticalAlignment.Center };
        panel.Children.Add(new TextBlock { Text = title.Length > 0 ? title : Strings.Upload_InputPrompt, Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(box);
        Window window = CreateWindow(title.Length > 0 ? title : "kniip", panel);
        panel.Children.Add(DialogButtons(window, okOnly: false));
        window.Loaded += (_, _) =>
        {
            box.Focus();
            box.SelectAll();
        };
        return window.ShowDialog() == true ? box.Text : throw new OperationCanceledException();
    });

    public static void Output(string title, string text) => OnUiThread(() =>
    {
        var panel = new StackPanel { Margin = new Thickness(20, 18, 20, 16) };
        panel.Children.Add(new TextBox
        {
            Text = text,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Width = 520,
            MaxHeight = 420,
            MinHeight = 80
        });
        Window window = CreateWindow(title.Length > 0 ? title : "kniip", panel);
        panel.Children.Add(DialogButtons(window, okOnly: true));
        window.ShowDialog();
        return true;
    });

    /// <summary>Eerste verbinding met een SSH-server: de gebruiker bevestigt de vingerafdruk.</summary>
    public static bool ConfirmHostKey(string host, string keyType, string fingerprint) => OnUiThread(() =>
        MessageBox.Show(string.Format(Strings.Upload_HostKeyConfirm, host, keyType, fingerprint), Strings.Upload_HostKeyConfirmTitle,
            MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes);

    private static T OnUiThread<T>(Func<T> action)
    {
        System.Windows.Threading.Dispatcher dispatcher = Application.Current.Dispatcher;
        return dispatcher.CheckAccess() ? action() : dispatcher.Invoke(action);
    }

    private static Window CreateWindow(string title, UIElement content)
    {
        var window = new Window
        {
            Title = title,
            Content = content,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
            FontSize = 13,
            Owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
        };
        window.SetResourceReference(Control.BackgroundProperty, "WindowBrush");
        window.SetResourceReference(Control.ForegroundProperty, "InkBrush");
        if (window.Owner == null)
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        ThemeManager.Attach(window);
        return window;
    }

    private static StackPanel DialogButtons(Window window, bool okOnly)
    {
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var ok = new Button { Content = "OK", IsDefault = true, Style = (Style)Application.Current.Resources["AccentButton"], Margin = new Thickness(0), MinWidth = 80 };
        ok.Click += (_, _) => window.DialogResult = true;
        buttons.Children.Add(ok);
        if (!okOnly)
        {
            buttons.Children.Add(new Button
            {
                Content = Strings.Common_Cancel,
                IsCancel = true,
                Style = (Style)Application.Current.Resources["SecondaryButton"],
                Margin = new Thickness(6, 0, 0, 0)
            });
        }
        else
        {
            ok.IsCancel = true;
        }

        return buttons;
    }
}
