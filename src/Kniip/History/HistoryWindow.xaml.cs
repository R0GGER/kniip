using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Kniip.Resources;

namespace Kniip.History;

public partial class HistoryWindow : Window
{
    private List<HistoryRow> _rows = new();

    public HistoryWindow(bool images)
    {
        InitializeComponent();
        Infrastructure.ThemeManager.Attach(this);
        Reload();
        ShowImages(images);
        HistoryStore.Changed += Reload;
        Closed += (_, _) => HistoryStore.Changed -= Reload;
    }

    public void ShowImages(bool images)
    {
        (images ? GridToggle : ListToggle).IsChecked = true;
    }

    private void Reload()
    {
        _rows = HistoryStore.Entries.Select(entry => new HistoryRow(entry)).ToList();
        ListItems.ItemsSource = _rows;
        GridItems.ItemsSource = _rows;
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        string query = SearchBox.Text.Trim();
        ICollectionView view = System.Windows.Data.CollectionViewSource.GetDefaultView(_rows);
        view.Filter = query.Length == 0 ? null : item => ((HistoryRow)item).Matches(query);
        int shown = view.Cast<object>().Count();
        CountText.Text = query.Length == 0 ? string.Format(Strings.History_Count, _rows.Count) : string.Format(Strings.History_CountFiltered, shown, _rows.Count);
        EmptyText.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ClearButton.IsEnabled = _rows.Count > 0;
    }

    private void View_Checked(object sender, RoutedEventArgs e)
    {
        bool images = GridToggle.IsChecked == true;
        GridItems.Visibility = images ? Visibility.Visible : Visibility.Collapsed;
        ListItems.Visibility = images ? Visibility.Collapsed : Visibility.Visible;
        Title = images ? Strings.History_ImageTitle : Strings.History_Title;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        ApplyFilter();
    }

    private void Item_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: HistoryRow row })
        {
            HistoryActions.Open(row.Entry);
        }
    }

    private void Items_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var list = (Selector)sender;
        if (e.OriginalSource is DependencyObject source
            && ItemsControl.ContainerFromElement(list, source) is FrameworkElement { DataContext: HistoryRow clicked })
        {
            list.SelectedItem = clicked;
        }

        if (list.SelectedItem is not HistoryRow row || list.ContextMenu is not { } menu)
        {
            e.Handled = true;
            return;
        }

        menu.Items.Clear();
        foreach (HistoryCommand? command in HistoryActions.CommandsFor(row.Entry))
        {
            menu.Items.Add(command == null ? new Separator() : MenuItemFor(command.Label, command.Glyph, command.Enabled, command.Run));
        }

        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItemFor(Strings.History_Remove, "\uE74D", true, () => HistoryStore.Remove(row.Entry)));
    }

    private static MenuItem MenuItemFor(string label, string glyph, bool enabled, Action run)
    {
        var item = new MenuItem
        {
            Header = label,
            IsEnabled = enabled,
            Icon = new TextBlock { Text = glyph, Style = (Style)Application.Current.FindResource("IconText"), FontSize = 14 }
        };
        item.Click += (_, _) => run();
        return item;
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, Strings.History_ClearConfirm, "kniip",
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes)
        {
            HistoryStore.Clear();
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private sealed class HistoryRow
    {
        public HistoryRow(HistoryEntry entry)
        {
            Entry = entry;
            IsAvailable = entry.FileExists || entry.Url is { Length: > 0 };
            Thumbnail = LoadThumbnail(entry);
        }

        public HistoryEntry Entry { get; }
        public bool IsAvailable { get; }
        public ImageSource? Thumbnail { get; }
        public Visibility NoThumbnailVisibility => Thumbnail == null ? Visibility.Visible : Visibility.Collapsed;
        public string TimeText => Entry.Time.ToString("G", System.Globalization.CultureInfo.CurrentCulture);
        public string? Folder => Entry.FilePath is { Length: > 0 } path ? Path.GetDirectoryName(path) : null;

        public string ToolTip => string.Join("\n", new[]
        {
            Entry.KindText + (Entry.Width > 0 ? $", {Entry.Width} x {Entry.Height}" : ""),
            Entry.FilePath,
            Entry.Url
        }.Where(line => !string.IsNullOrEmpty(line)));

        public bool Matches(string query) =>
            Entry.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
            || (Entry.FilePath?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
            || (Entry.Url?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false);

        private static ImageSource? LoadThumbnail(HistoryEntry entry)
        {
            if (HistoryStore.ThumbnailPath(entry) is not { } path || !File.Exists(path))
            {
                return null;
            }

            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = new MemoryStream(File.ReadAllBytes(path));
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
