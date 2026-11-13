using System.Windows;
using Kniip.Settings;

namespace Kniip.Editor;

/// <summary>Vraagt bij het sluiten van het editorvenster of de tabbladen dicht moeten of bewaard blijven.</summary>
public partial class CloseEditorDialog : Window
{
    public CloseEditorDialog(int tabCount)
    {
        InitializeComponent();
        Infrastructure.ThemeManager.Attach(this);
        MessageText.Text = tabCount == 1
            ? "Er is 1 tabblad open. Wat wil je doen?"
            : $"Er zijn {tabCount} tabbladen open. Wat wil je doen?";
    }

    /// <summary>CloseAll of KeepTabs; alleen geldig als ShowDialog true teruggeeft.</summary>
    public EditorCloseBehavior Choice { get; private set; } = EditorCloseBehavior.CloseAll;

    /// <summary>De keuze voortaan gebruiken zonder te vragen.</summary>
    public bool Remember => RememberBox.IsChecked == true;

    private void CloseAll_Click(object sender, RoutedEventArgs e) => Finish(EditorCloseBehavior.CloseAll);

    private void KeepTabs_Click(object sender, RoutedEventArgs e) => Finish(EditorCloseBehavior.KeepTabs);

    private void Finish(EditorCloseBehavior choice)
    {
        Choice = choice;
        DialogResult = true;
    }
}
