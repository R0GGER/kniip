using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Kniip.Editor.Emoji;

namespace Kniip.Editor;

public sealed record EmojiHeaderRow(string Title);

public sealed record EmojiCellRow(IReadOnlyList<EmojiCell> Cells);

public sealed class EmojiCell
{
    private ImageSource? _image;

    public EmojiCell(EmojiInfo info, SkinTone tone)
    {
        Info = info;
        Tone = tone;
    }

    public EmojiInfo Info { get; }
    public SkinTone Tone { get; }

    public ImageSource Image => _image ??= EmojiCatalog.Get(Info, Tone);
}

/// <summary>Emoji-kiezer in de stijl van emoji-mart: zoeken, categorieën, huidskleur en recent gebruikt.</summary>
public partial class EmojiPickerButton : UserControl
{
    public const int MaxRecent = 18;
    private const int Columns = 9;
    private const string RecentId = "Recent";

    private static readonly (SkinTone Tone, Color Color, string Name)[] Tones =
    {
        (SkinTone.Default, Color.FromRgb(0xFF, 0xC9, 0x3A), "Standaard"),
        (SkinTone.Light, Color.FromRgb(0xFA, 0xDC, 0xBC), "Licht"),
        (SkinTone.MediumLight, Color.FromRgb(0xE0, 0xBB, 0x95), "Middellicht"),
        (SkinTone.Medium, Color.FromRgb(0xBF, 0x8F, 0x68), "Middel"),
        (SkinTone.MediumDark, Color.FromRgb(0x9B, 0x64, 0x3D), "Middeldonker"),
        (SkinTone.Dark, Color.FromRgb(0x59, 0x45, 0x39), "Donker")
    };

    private static readonly Brush AccentBrush = Annotations.RenderHelpers.Freeze(new SolidColorBrush(Palette.Accent));

    private readonly List<(string Id, int RowIndex)> _sections = new();
    private readonly Dictionary<string, ToggleButton> _tabs = new();
    private string _emoji = "1f44d";
    private SkinTone _tone;
    private ScrollViewer? _scroller;
    private bool _syncingTabs;

    /// <summary>Er is een andere emoji gekozen (codepunten zonder huidskleur).</summary>
    public event Action<string>? EmojiPicked;

    public event Action<SkinTone>? SkinToneChanged;

    public EmojiPickerButton()
    {
        InitializeComponent();
        BuildTones();
        IsVisibleChanged += (_, _) => UpdateFace();
    }

    /// <summary>Lijst met recent gekozen emoji, nieuwste eerst; wordt door de eigenaar bewaard.</summary>
    public List<string>? Recent { get; set; }

    public bool IsOpen => PopupPanel.IsOpen;

    public string Emoji
    {
        get => _emoji;
        set
        {
            _emoji = value;
            UpdateFace();
        }
    }

    public SkinTone SkinTone
    {
        get => _tone;
        set
        {
            _tone = value;
            UpdateToneButtons();
            UpdateFace();
        }
    }

    // Het sheet pas laden als de knop echt te zien is.
    private void UpdateFace()
    {
        if (!IsVisible)
        {
            return;
        }

        Face.Source = EmojiCatalog.Get(_emoji, _tone);
        if (EmojiCatalog.Find(_emoji) is { } info)
        {
            Toggle.ToolTip = $"Emoji kiezen ({info.Name})";
        }
    }

    // ---------------------------------------------------------------- Popup

    private void Popup_Opened(object? sender, EventArgs e)
    {
        BuildTabs();
        SearchBox.Text = string.Empty;
        BuildRows();
        ShowPreview(EmojiCatalog.Find(_emoji));
        ScrollToRow(0);
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () => Keyboard.Focus(SearchBox));
    }

    private void Popup_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Toggle.IsChecked = false;
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && SearchBox.IsKeyboardFocused
                 && EmojiList.Items.OfType<EmojiCellRow>().FirstOrDefault()?.Cells.FirstOrDefault() is { } first)
        {
            Pick(first.Info);
            e.Handled = true;
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        SearchPlaceholder.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (PopupPanel.IsOpen)
        {
            BuildRows();
            ScrollToRow(0);
        }
    }

    private void Cell_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: EmojiCell cell })
        {
            Pick(cell.Info);
        }
    }

    private void Cell_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: EmojiCell cell })
        {
            ShowPreview(cell.Info);
        }
    }

    private void Pick(EmojiInfo info)
    {
        if (Recent != null)
        {
            Recent.Remove(info.Unified);
            Recent.Insert(0, info.Unified);
            if (Recent.Count > MaxRecent)
            {
                Recent.RemoveRange(MaxRecent, Recent.Count - MaxRecent);
            }
        }

        Toggle.IsChecked = false;
        Emoji = info.Unified;
        EmojiPicked?.Invoke(info.Unified);
    }

    private void ShowPreview(EmojiInfo? info)
    {
        if (info == null)
        {
            PreviewImage.Source = null;
            PreviewName.Text = PreviewCode.Text = string.Empty;
            return;
        }

        PreviewImage.Source = EmojiCatalog.Get(info, _tone);
        PreviewName.Text = info.Name.Length > 0 ? char.ToUpper(info.Name[0]) + info.Name[1..] : info.Name;
        PreviewCode.Text = $":{info.ShortName}:";
    }

    // ---------------------------------------------------------------- Raster

    private IEnumerable<(string Id, string Title, IEnumerable<EmojiInfo> Items)> Sections()
    {
        string query = SearchBox.Text.Trim();
        if (query.Length > 0)
        {
            yield return ("Search", "Zoekresultaten", EmojiCatalog.Search(query));
            yield break;
        }

        List<EmojiInfo> recent = RecentEmoji();
        if (recent.Count > 0)
        {
            yield return (RecentId, "Recent gebruikt", recent);
        }

        foreach (IGrouping<string, EmojiInfo> group in EmojiCatalog.All.GroupBy(e => e.Category))
        {
            if (EmojiCatalog.Categories.FirstOrDefault(c => c.Id == group.Key) is { } category)
            {
                yield return (category.Id, category.Title, group);
            }
        }
    }

    private List<EmojiInfo> RecentEmoji() =>
        (Recent ?? new List<string>()).Select(EmojiCatalog.Find).OfType<EmojiInfo>().ToList();

    private void BuildRows()
    {
        var rows = new List<object>();
        _sections.Clear();
        foreach ((string id, string title, IEnumerable<EmojiInfo> items) in Sections())
        {
            List<EmojiCell> cells = items.Select(info => new EmojiCell(info, _tone)).ToList();
            if (cells.Count == 0)
            {
                continue;
            }

            _sections.Add((id, rows.Count));
            rows.Add(new EmojiHeaderRow(title));
            for (int i = 0; i < cells.Count; i += Columns)
            {
                rows.Add(new EmojiCellRow(cells.GetRange(i, Math.Min(Columns, cells.Count - i))));
            }
        }

        EmojiList.ItemsSource = rows;
        NoResultsText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateActiveTab(0);
    }

    private ScrollViewer? Scroller
    {
        get
        {
            if (_scroller == null)
            {
                EmojiList.ApplyTemplate();
                _scroller = EmojiList.Template.FindName("PART_Scroller", EmojiList) as ScrollViewer;
                if (_scroller != null)
                {
                    _scroller.ScrollChanged += (_, _) => UpdateActiveTab((int)Math.Floor(_scroller.VerticalOffset));
                }
            }

            return _scroller;
        }
    }

    // Met CanContentScroll schuift de ScrollViewer per rij, dus de offset is een rij-index.
    private void ScrollToRow(int row) => Scroller?.ScrollToVerticalOffset(row);

    // ---------------------------------------------------------------- Tabs en huidskleur

    private void BuildTabs()
    {
        TabPanel.Children.Clear();
        _tabs.Clear();
        if (RecentEmoji().Count > 0)
        {
            AddTab(RecentId, "Recent gebruikt", new TextBlock { Text = "\uE81C", Style = (Style)FindResource("IconText") });
        }

        foreach (EmojiCategory category in EmojiCatalog.Categories)
        {
            var icon = new Image { Source = EmojiCatalog.Get(category.IconEmoji, SkinTone.Default), Width = 18, Height = 18 };
            RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
            AddTab(category.Id, category.Title, icon);
        }
    }

    private void AddTab(string id, string title, UIElement content)
    {
        var tab = new ToggleButton
        {
            Style = (Style)FindResource("ToolToggle"),
            MinWidth = 0,
            MinHeight = 0,
            Width = 35,
            Height = 32,
            Padding = new Thickness(2),
            Margin = new Thickness(0),
            ToolTip = title,
            Content = content
        };
        tab.Click += (_, _) =>
        {
            if (SearchBox.Text.Length > 0)
            {
                SearchBox.Text = string.Empty;
            }

            int index = _sections.FindIndex(s => s.Id == id);
            if (index >= 0)
            {
                ScrollToRow(_sections[index].RowIndex);
            }

            UpdateActiveTab(index >= 0 ? _sections[index].RowIndex : 0);
        };
        _tabs[id] = tab;
        TabPanel.Children.Add(tab);
    }

    private void UpdateActiveTab(int firstVisibleRow)
    {
        if (_syncingTabs)
        {
            return;
        }

        _syncingTabs = true;
        string? active = _sections.LastOrDefault(s => s.RowIndex <= firstVisibleRow).Id;
        foreach ((string id, ToggleButton tab) in _tabs)
        {
            tab.IsChecked = id == active;
        }

        _syncingTabs = false;
    }

    private void BuildTones()
    {
        foreach ((SkinTone tone, Color color, string name) in Tones)
        {
            var dot = new Border
            {
                Width = 16,
                Height = 16,
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(color),
                BorderBrush = new SolidColorBrush(Color.FromArgb(40, 0, 0, 0)),
                BorderThickness = new Thickness(1)
            };
            var button = new Button
            {
                Style = (Style)FindResource("FlatButton"),
                MinWidth = 0,
                MinHeight = 0,
                Padding = new Thickness(3),
                Margin = new Thickness(0),
                Content = dot,
                Tag = tone,
                ToolTip = $"Huidskleur: {name.ToLowerInvariant()}"
            };
            button.Click += (_, _) => SetTone(tone);
            TonePanel.Children.Add(button);
        }

        UpdateToneButtons();
    }

    private void SetTone(SkinTone tone)
    {
        if (tone == _tone)
        {
            return;
        }

        SkinTone = tone;
        double offset = Scroller?.VerticalOffset ?? 0;
        BuildRows();
        Scroller?.ScrollToVerticalOffset(offset);
        ShowPreview(EmojiCatalog.Find(_emoji));
        SkinToneChanged?.Invoke(tone);
    }

    private void UpdateToneButtons()
    {
        foreach (Button button in TonePanel.Children.OfType<Button>())
        {
            bool selected = button.Tag is SkinTone tone && tone == _tone;
            button.BorderBrush = selected ? AccentBrush : Brushes.Transparent;
        }
    }
}
