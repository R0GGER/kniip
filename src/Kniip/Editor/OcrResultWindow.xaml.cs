using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;

namespace Kniip.Editor;

/// <summary>Toont de herkende tekst van een afbeelding, om aan te passen en te kopiëren.</summary>
public partial class OcrResultWindow : Window
{
    private OcrResultWindow()
    {
        InitializeComponent();
        Infrastructure.ThemeManager.Attach(this);
    }

    /// <summary>Klaar zodra de tekst herkend is of het herkennen is mislukt.</summary>
    public Task Completion { get; private set; } = Task.CompletedTask;

    /// <summary>Opent het venster meteen en vult het zodra de tekst herkend is.</summary>
    public static OcrResultWindow Open(BitmapSource image, Window? owner = null)
    {
        var window = new OcrResultWindow();
        if (owner is { IsVisible: true })
        {
            window.Owner = owner;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }

        window.SetBusy(true);
        window.Show();
        window.Activate();
        window.Completion = window.RecognizeAsync(image);
        return window;
    }

    private async Task RecognizeAsync(BitmapSource image)
    {
        try
        {
            ShowText(await OcrService.RecognizeAsync(image));
        }
        catch (Exception ex)
        {
            ShowError(ex is InvalidOperationException ? ex.Message : $"Tekst herkennen is mislukt.\n\n{ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        ResultBox.IsReadOnly = busy;
        CopyButton.IsEnabled = !busy && ResultBox.Text.Length > 0;
        if (busy)
        {
            StatusText.Text = "Tekst herkennen...";
        }
    }

    private void ShowText(string text)
    {
        ResultBox.Text = text;
        bool empty = text.Trim().Length == 0;
        EmptyPanel.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        int lines = text.Split('\n').Count(line => line.Trim().Length > 0);
        StatusText.Text = empty ? string.Empty
            : $"{lines} {(lines == 1 ? "regel" : "regels")} herkend. Je kunt de tekst hier aanpassen.";
        if (!empty)
        {
            ResultBox.Focus();
            ResultBox.SelectAll();
        }
    }

    private void ShowError(string message)
    {
        ResultBox.Text = string.Empty;
        ResultBox.IsEnabled = false;
        EmptyText.Text = message;
        EmptyPanel.Visibility = Visibility.Visible;
        StatusText.Text = string.Empty;
    }

    private void ResultBox_SelectionChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        CopyLabel.Text = ResultBox.SelectionLength > 0 && ResultBox.SelectionLength < ResultBox.Text.Length
            ? "Selectie kopiëren"
            : "Kopiëren";
        CopyButton.IsEnabled = !ResultBox.IsReadOnly && ResultBox.Text.Length > 0;
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        string text = ResultBox.SelectionLength > 0 ? ResultBox.SelectedText : ResultBox.Text;
        StatusText.Text = ClipboardWriter.SetText(text)
            ? "Gekopieerd naar het klembord."
            : "Kopiëren mislukt: het klembord is bezet. Probeer het opnieuw.";
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
