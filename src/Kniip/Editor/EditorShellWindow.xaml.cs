using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Kniip.Infrastructure;
using Kniip.Settings;
using Kniip.Resources;

namespace Kniip.Editor;

/// <summary>Het ene editorvenster van kniip; elk knipsel staat erin als tabblad.</summary>
public partial class EditorShellWindow : Window
{
    private readonly AppSettings _settings;

    /// <summary>Alle tabbladen zijn al gesloten; het venster mag nu echt dicht.</summary>
    private bool _allowClose;

    public static readonly RoutedCommand ToggleMaximizeCommand = new();

    public EditorShellWindow(AppSettings settings)
    {
        InitializeComponent();
        ThemeManager.Attach(this);
        _settings = settings;
        Activated += (_, _) => SelectedView?.RefreshFromSettings();
        DpiChanged += (_, _) => SelectedView?.OnHostDpiChanged();
        StateChanged += (_, _) => ApplyMaximizedInset();
        PreviewKeyDown += OnPreviewKeyDown;

        CommandBindings.Add(new CommandBinding(SystemCommands.MinimizeWindowCommand, (_, _) => SystemCommands.MinimizeWindow(this)));
        CommandBindings.Add(new CommandBinding(ToggleMaximizeCommand, (_, _) =>
        {
            if (WindowState == WindowState.Maximized)
            {
                SystemCommands.RestoreWindow(this);
            }
            else
            {
                SystemCommands.MaximizeWindow(this);
            }
        }));
        CommandBindings.Add(new CommandBinding(SystemCommands.CloseWindowCommand, (_, _) => Close()));
    }

    /// <summary>Zoals bij een gewone titelbalk: het icoon opent het venstermenu, dubbelklikken sluit het venster.</summary>
    private void AppIcon_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (e.ClickCount == 2)
        {
            Close();
            return;
        }

        var icon = (FrameworkElement)sender;
        Point corner = icon.PointToScreen(new Point(0, icon.ActualHeight));
        DpiScale dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
        SystemCommands.ShowSystemMenu(this, new Point(corner.X / dpi.DpiScaleX, corner.Y / dpi.DpiScaleY));
    }

    /// <summary>Gemaximaliseerd valt een venster met eigen titelbalk aan alle kanten een randbreedte buiten het scherm.</summary>
    private void ApplyMaximizedInset()
    {
        if (WindowState != WindowState.Maximized)
        {
            Tabs.Margin = new Thickness();
            return;
        }

        Thickness frame = SystemParameters.WindowResizeBorderThickness;
        Tabs.Margin = new Thickness(frame.Left + 4, frame.Top + 4, frame.Right + 4, frame.Bottom + 4);
    }

    public int TabCount => Tabs.Items.Count;

    private IEnumerable<TabItem> TabItems => Tabs.Items.Cast<TabItem>();

    private EditorView? SelectedView => (Tabs.SelectedItem as TabItem)?.Content as EditorView;

    /// <summary>Knipsel als nieuw tabblad toevoegen en selecteren; een ongebruikt leeg canvas maakt er plaats voor.</summary>
    public void AddTab(EditorView view)
    {
        if (SelectedView is { IsUntouchedBlank: true } blank)
        {
            ReplaceTab(blank, view);
            return;
        }

        TabItem tab = CreateTab(view);
        Tabs.Items.Add(tab);
        Tabs.SelectedItem = tab;
    }

    /// <summary>Tabblad op dezelfde plek vervangen door een ander knipsel; het oude wordt zonder vragen gesloten.</summary>
    public void ReplaceTab(EditorView old, EditorView replacement)
    {
        if (TabItems.FirstOrDefault(t => t.Content == old) is not { } tab)
        {
            AddTab(replacement);
            return;
        }

        old.TryClose(force: true);
        TabItem fresh = CreateTab(replacement);
        Tabs.Items.Insert(Tabs.Items.IndexOf(tab), fresh);
        Tabs.SelectedItem = fresh;
        Tabs.Items.Remove(tab);
    }

    /// <summary>Venster wegzetten voor een knipsel of opname; de teruggegeven actie zet het weer terug.</summary>
    public Action Minimize()
    {
        WindowState previous = WindowState == WindowState.Minimized ? WindowState.Normal : WindowState;
        WindowState = WindowState.Minimized;
        return () =>
        {
            if (IsVisible)
            {
                WindowState = previous;
            }
        };
    }

    /// <summary>
    /// Alle tabbladen sluiten, ongeacht de instelling voor het sluiten van het venster (afsluiten van kniip, update installeren).
    /// Elk tabblad vraagt zelf om te kopiëren bij niet-opgeslagen aantekeningen.
    /// </summary>
    /// <returns>False als de gebruiker bij een tabblad annuleert; het venster blijft dan open.</returns>
    public bool CloseAll()
    {
        if (!IsVisible && TabItems.Any(t => ((EditorView)t.Content).NeedsCloseConfirmation))
        {
            Show();
            Activate();
        }

        foreach (TabItem tab in TabItems.ToList())
        {
            var view = (EditorView)tab.Content;
            if (view.NeedsCloseConfirmation)
            {
                Tabs.SelectedItem = tab;
            }

            if (!view.TryClose())
            {
                return false;
            }

            Tabs.Items.Remove(tab);
        }

        _allowClose = true;
        Close();
        return true;
    }

    private TabItem CreateTab(EditorView view)
    {
        var title = new TextBlock
        {
            Text = view.TabTitle,
            ToolTip = view.TabTitle,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        var close = new Button
        {
            Style = (Style)FindResource("TabCloseButton"),
            ToolTip = Strings.Editor_CloseTab,
            Content = new TextBlock { Text = "\uE711", Style = (Style)FindResource("IconText"), FontSize = 10 }
        };
        var header = new DockPanel();
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(close);
        header.Children.Add(title);

        var tab = new TabItem { Header = header, Content = view };
        close.Click += (_, _) => CloseTab(tab);
        tab.MouseUp += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.Middle)
            {
                CloseTab(tab);
                e.Handled = true;
            }
        };
        view.DirtyChanged += (_, _) =>
        {
            title.Text = HeaderText(view);
            UpdateTitle();
        };
        return tab;
    }

    private static string HeaderText(EditorView view) => view.IsDirty ? view.TabTitle + " *" : view.TabTitle;

    private void UpdateTitle() => Title = SelectedView is { } view ? $"{HeaderText(view)} - kniip" : "kniip";

    /// <summary>Eén tabblad sluiten; na het laatste gaat het venster dicht, zonder te vragen.</summary>
    private void CloseTab(TabItem tab)
    {
        var view = (EditorView)tab.Content;
        view.TryClose(confirm: false);
        Tabs.Items.Remove(tab);
        if (Tabs.Items.Count == 0)
        {
            _allowClose = true;
            Close();
        }
    }

    private void SelectRelative(int step)
    {
        int count = Tabs.Items.Count;
        if (count > 1)
        {
            Tabs.SelectedIndex = ((Tabs.SelectedIndex + step) % count + count) % count;
        }
    }

    private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Keuzelijsten in de editor sturen hun SelectionChanged ook hierheen.
        if (e.OriginalSource != Tabs)
        {
            return;
        }

        SelectedView?.RefreshFromSettings();
        UpdateTitle();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        ModifierKeys mods = Keyboard.Modifiers;
        if (!mods.HasFlag(ModifierKeys.Control) || mods.HasFlag(ModifierKeys.Alt))
        {
            return;
        }

        bool shift = mods.HasFlag(ModifierKeys.Shift);
        switch (e.Key)
        {
            case Key.W when !shift:
                if (Tabs.SelectedItem is TabItem tab)
                {
                    CloseTab(tab);
                }

                break;
            case Key.Tab:
                SelectRelative(shift ? -1 : 1);
                break;
            case Key.PageDown when !shift:
                SelectRelative(1);
                break;
            case Key.PageUp when !shift:
                SelectRelative(-1);
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_allowClose || Tabs.Items.Count == 0)
        {
            return;
        }

        // Tijdens Closing kan het venster niet verborgen worden en geen eigen dialoog krijgen; daarom direct erna.
        e.Cancel = true;
        Dispatcher.BeginInvoke(HandleCloseRequest);
    }

    private void HandleCloseRequest()
    {
        EditorCloseBehavior behavior = _settings.EditorCloseBehavior;
        if (behavior == EditorCloseBehavior.Ask)
        {
            var dialog = new CloseEditorDialog(Tabs.Items.Count) { Owner = this };
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            behavior = dialog.Choice;
            if (dialog.Remember)
            {
                _settings.EditorCloseBehavior = behavior;
                SettingsStore.Save(_settings);
            }
        }

        if (behavior == EditorCloseBehavior.KeepTabs)
        {
            Hide();
            return;
        }

        CloseAll();
    }
}
