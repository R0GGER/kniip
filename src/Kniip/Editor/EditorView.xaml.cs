using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Kniip.Capture;
using Kniip.Editor.Annotations;
using Kniip.Editor.Emoji;
using Kniip.Infrastructure;
using Kniip.Settings;
using Kniip.Upload;
using Kniip.Resources;

namespace Kniip.Editor;

/// <summary>Eén knipsel met zijn aantekeningen; staat als tabblad in het EditorShellWindow.</summary>
public partial class EditorView : UserControl
{
    private enum DragMode { None, Creating, Moving, Handle, Rotating, Cropping, Guide, Erasing }

    /// <summary>Afstand in schermpixels waarbinnen een object of punt aan een hulplijn vastklikt.</summary>
    private const double SnapDistance = 6;

    private enum ZOrder { Front, Forward, Backward, Back }

    private static readonly double[] ThicknessValues = { 0, 1, 2, 3, 4, 6, 8, 12, 16, 20, 24, 32 };
    private static readonly double[] EraserThicknessValues = { 4, 8, 12, 16, 20, 24, 32, 48, 64, 96 };
    private static readonly double[] FontSizeValues = { 10, 12, 14, 16, 18, 20, 24, 28, 32, 40, 48, 64 };
    private static readonly double[] SizeValues = { 16, 20, 24, 28, 32, 40, 48, 56, 64, 80 };
    private static readonly double[] EmojiSizeValues = { 24, 32, 48, 64, 96, 128 };
    private static readonly double[] CornerValues = { 0, 4, 10, 20 };
    private static readonly LineDash[] DashValues = { LineDash.Solid, LineDash.Dash, LineDash.Dot };
    private static readonly StepNumbering[] NumberingValues =
        { StepNumbering.Numbers, StepNumbering.LowerAlpha, StepNumbering.UpperAlpha, StepNumbering.Roman };
    private static readonly CursorKind[] CursorKindValues = { CursorKind.Arrow, CursorKind.Click, CursorKind.Hand };
    private static readonly BlurMode[] BlurModeValues = { BlurMode.Pixelate, BlurMode.Blur };
    private static readonly TextShape[] TextShapeValues = { TextShape.None, TextShape.Frame, TextShape.Balloon, TextShape.Oval, TextShape.Cloud };
    private static readonly ShapeKind[] ShapeKindValues =
    {
        ShapeKind.Triangle, ShapeKind.BlockArrow, ShapeKind.Star, ShapeKind.Cloud, ShapeKind.Balloon, ShapeKind.Heart,
        ShapeKind.Moon, ShapeKind.Drop, ShapeKind.Egg, ShapeKind.Lightning, ShapeKind.Spiral
    };
    private static readonly int[] CloudBubblesValues = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 10, 12, 15, 20 };
    private static readonly BackgroundKind[] BackgroundKindValues =
        { BackgroundKind.None, BackgroundKind.Color, BackgroundKind.Gradient, BackgroundKind.Image };
    /// <summary>Standaardvolgorde in de gesplitste indeling: eerst de bovenste werkbalk, dan de onderste.</summary>
    private static readonly string[] DefaultSplitOrder =
    {
        "SplitSpacer1", "Select", "Sep1", "Rectangle", "Ellipse", "Shape", "Line", "Arrow", "Text", "Step", "Cursor", "Emoji", "Highlight", "Blur",
        "Eraser", "Sep2", "Crop", "Sep3", "Undo", "Redo", "Delete", "Sep4", "Clear", "SplitSpacer2", "Folders", "Print", "Settings",
        "New", "Screencast", "OcrSnip", "Spacer", "Save", "Upload", "Copy", "Sep5", "Spacer2", "Rulers", "ZoomOut", "Zoom", "ZoomIn"
    };

    /// <summary>Items die in de gesplitste indeling op de onderste werkbalk staan.</summary>
    private static readonly HashSet<string> SplitBottomIds = new()
    {
        "New", "Screencast", "OcrSnip", "Spacer", "Save", "Upload", "Copy", "Sep5", "Spacer2", "Rulers", "ZoomOut", "Zoom", "ZoomIn"
    };

    /// <summary>Items die alleen in de gesplitste indeling op een werkbalk staan.</summary>
    private static readonly HashSet<string> SplitOnlyIds = new() { "SplitSpacer1", "SplitSpacer2" };

    /// <summary>Items die in de drie-split-indeling in de zijbalk staan in plaats van de onderste werkbalk.</summary>
    private static readonly HashSet<string> CaptureBarIds = new() { "New", "Screencast", "OcrSnip" };

    private readonly EditorDocument _doc;
    private readonly UndoStack _undo = new();
    private readonly AppSettings _settings;
    private readonly DispatcherTimer _hintResetTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly List<string> _defaultToolbarOrder = new();
    private readonly List<string> _defaultSplitOrder = new();
    private readonly Dictionary<string, UIElement> _toolbarItems = new();
    private CanvasBackground? _backgroundBeforePreview;
    private bool _recognizing;

    private ToolKind _tool;
    private Annotation? _selected;

    private DragMode _drag;
    private Point _dragStart;
    private Point _lastPoint;
    private Annotation? _active;
    private int _handleIndex = -1;
    private bool _dragChanged;
    private bool _createdOnDown;
    private DocumentState? _pending;
    private List<Point>? _strokePoints;
    private Rect _moveStartBounds;

    private readonly List<double> _guidesX = new();
    private readonly List<double> _guidesY = new();
    private List<double>? _guideDrag;
    private int _guideIndex = -1;

    private TextBox? _textEditor;
    private TextAnnotation? _editingText;
    private DocumentState? _textPending;
    private string _textOriginal = string.Empty;
    private bool _textIsNew;

    private bool _suppressStyleEvents = true;
    private Annotation? _lastStyleTarget;
    private string? _lastStyleProp;
    private DateTime _lastStyleTime;

    private double _zoom = 1;
    private bool _fitMode = true;
    private bool _dirty;
    private bool _exported;
    private bool _toolbarsHidden;
    private readonly bool _isBlank;
    private readonly string? _windowName;

    /// <summary>Aantekeningen zijn gewijzigd sinds het laatste kopiëren, opslaan of uploaden.</summary>
    public event EventHandler? DirtyChanged;

    /// <param name="windowName">Programmanaam van het geknipte venster, voor %V in het naamsjabloon.</param>
    public EditorView(BitmapSource image, AppSettings settings, bool isBlank = false, string? windowName = null)
    {
        InitializeComponent();
        _settings = settings;
        _isBlank = isBlank;
        _windowName = windowName;
        TabTitle = windowName ?? (isBlank ? Strings.Editor_BlankCanvas : string.Format(Strings.Editor_SnipTabTitle, DateTime.Now));
        _doc = new EditorDocument(image, isBlank);

        Surface.Document = _doc;
        Surface.GuidesX = _guidesX;
        Surface.GuidesY = _guidesY;
        Scroller.ScrollChanged += (_, _) => UpdateRulers();
        ThicknessCombo.ItemsSource = ThicknessValues;
        FontSizeCombo.ItemsSource = FontSizeValues;
        SizeCombo.ItemsSource = SizeValues;
        CornerCombo.ItemsSource = CornerValues;
        DashCombo.ItemsSource = DashValues;
        NumberingCombo.ItemsSource = NumberingValues;
        CursorKindCombo.ItemsSource = CursorKindValues;
        BlurModeCombo.ItemsSource = BlurModeValues;
        TextShapeCombo.ItemsSource = TextShapeValues;
        CloudBubblesCombo.ItemsSource = CloudBubblesValues;
        ShapeKindCombo.ItemsSource = ShapeKindValues;
        BackgroundKindCombo.ItemsSource = BackgroundKindValues;
        UpdateShapeToolIcon();

        StrokePicker.ColorChanged += c => ChangeStyle("stroke", s => s.Stroke = c);
        FillPicker.ColorChanged += c => ChangeStyle("fill", s => s.Fill = c);
        ForegroundPicker.ColorChanged += c => ChangeStyle("foreground", s => s.Foreground = c);
        BackgroundColorPicker.ColorChanged += c => ChangeBackground(b => b with { Color = c });
        BackgroundColor2Picker.ColorChanged += c => ChangeBackground(b => b with { Color2 = c });
        GradientDial.AngleChanging += PreviewGradientAngle;
        GradientDial.AngleCommitted += angle => CommitGradient(b => b with { Direction = GradientDirection.Linear, Angle = angle });
        GradientDial.RadialChanged += radial =>
            CommitGradient(b => b with { Direction = radial ? GradientDirection.Radial : GradientDirection.Linear });
        EmojiPicker.Recent = settings.RecentEmoji;
        EmojiPicker.EmojiPicked += emoji => ChangeStyle("emoji", s => s.Emoji = emoji);
        EmojiPicker.SkinToneChanged += tone => ChangeStyle("skintone", s => s.SkinTone = tone);

        _undo.Changed += UpdateCommandStates;
        _hintResetTimer.Tick += (_, _) =>
        {
            _hintResetTimer.Stop();
            UpdateHint();
        };

        // Loaded komt ook bij elke terugkeer naar dit tabblad; een eigen zoom blijft dan staan.
        Loaded += (_, _) =>
        {
            if (_fitMode)
            {
                ZoomToFit();
            }
            else
            {
                ApplyZoom();
            }

            Surface.Focus();
        };
        SizeChanged += (_, _) =>
        {
            if (_fitMode)
            {
                ZoomToFit();
            }
        };
        PreviewKeyDown += OnPreviewKeyDown;

        App.Instance.Updates.AvailabilityChanged += OnUpdateAvailabilityChanged;
        ShowUpdateBadge();

        InitializeToolbar();
        ApplyRulers();
        ApplySaveButton();
        _suppressStyleEvents = false;
        SelectTool(settings.LastTool == ToolKind.Crop ? ToolKind.Rectangle : settings.LastTool);
        UpdateCommandStates();
        UpdateInfo();
        if (isBlank)
        {
            HintText.Text = Strings.Editor_BlankHint;
        }
    }

    /// <summary>Tekst op de tabkop: de programmanaam van het geknipte venster, anders het tijdstip of "Leeg canvas".</summary>
    public string TabTitle { get; }

    public bool IsDirty => _dirty;

    /// <summary>TryClose zal vragen of de aantekeningen gekopieerd moeten worden.</summary>
    public bool NeedsCloseConfirmation => _dirty && _doc.Items.Count > 0;

    /// <summary>Leeg canvas waarop nog niets is gedaan; een nieuw knipsel mag dit tabblad vervangen.</summary>
    public bool IsUntouchedBlank => _isBlank && !_dirty && _doc.Items.Count == 0 && !_undo.CanUndo;

    private EditorShellWindow? OwnerShell => Window.GetWindow(this) as EditorShellWindow;

    private void SetDirty(bool dirty)
    {
        if (_dirty == dirty)
        {
            return;
        }

        _dirty = dirty;
        DirtyChanged?.Invoke(this, EventArgs.Empty);
    }

    private MessageBoxResult ShowMessage(string text, MessageBoxButton buttons, MessageBoxImage icon) =>
        Window.GetWindow(this) is { } owner
            ? MessageBox.Show(owner, text, "kniip", buttons, icon)
            : MessageBox.Show(text, "kniip", buttons, icon);

    // ---------------------------------------------------------------- Werkbalk

    private void InitializeToolbar()
    {
        foreach (UIElement item in ToolbarItems.Items)
        {
            _toolbarItems[ItemId(item)] = item;
            if (!SplitOnlyIds.Contains(ItemId(item)))
            {
                _defaultToolbarOrder.Add(ItemId(item));
            }
        }

        _defaultSplitOrder.AddRange(DefaultSplitOrder.Where(_toolbarItems.ContainsKey));
        _defaultSplitOrder.AddRange(_defaultToolbarOrder.Except(_defaultSplitOrder));
        ApplyToolbarLayout();
        foreach (UIElement item in _toolbarItems.Values)
        {
            item.Visibility = _settings.ToolbarHidden.Contains(ItemId(item)) ? Visibility.Collapsed : Visibility.Visible;
        }

        ApplyUploadButton();
        ApplyToolbarLock();
    }

    private static string ItemId(UIElement item) => ToolbarPanel.GetItemId(item) ?? string.Empty;

    private bool IsSplitLayout => _settings.EditorLayout is EditorLayout.Split or EditorLayout.ThreeSplit;

    private bool IsThreeSplit => _settings.EditorLayout == EditorLayout.ThreeSplit;

    private IEnumerable<UIElement> AllToolbarItems => ToolbarItems.Items.Concat(ActionItems.Items).Concat(CaptureItems.Items);

    /// <summary>Items volgens de opgeslagen volgorde van de huidige indeling over de bovenste en onderste werkbalk verdelen.</summary>
    private void ApplyToolbarLayout()
    {
        bool split = IsSplitLayout;
        bool threeSplit = IsThreeSplit;
        List<string> order = split
            ? MergeToolbarOrder(_settings.SplitToolbarOrder, _defaultSplitOrder)
            : MergeToolbarOrder(_settings.ToolbarOrder, _defaultToolbarOrder);

        ToolbarItems.Children.Clear();
        ActionItems.Children.Clear();
        CaptureItems.Children.Clear();
        foreach (string id in order.Where(id => split || !SplitOnlyIds.Contains(id)))
        {
            ToolbarPanel panel = threeSplit && CaptureBarIds.Contains(id) ? CaptureItems
                : split && SplitBottomIds.Contains(id) ? ActionItems
                : ToolbarItems;
            panel.Children.Add(_toolbarItems[id]);
        }

        ActionBar.Visibility = split && !_toolbarsHidden ? Visibility.Visible : Visibility.Collapsed;
        ApplyCaptureBarPosition();
        ApplyToolbarPosition();
    }

    private void ApplyCaptureBarPosition()
    {
        bool floating = _settings.CaptureBarFloating;
        bool show = IsThreeSplit && !_toolbarsHidden;
        CaptureBar.Visibility = show && !floating ? Visibility.Visible : Visibility.Collapsed;
        CaptureFloat.Visibility = show && floating ? Visibility.Visible : Visibility.Collapsed;

        Decorator host = floating ? CaptureFloatHost : CaptureBar;
        if (host.Child != CaptureItems)
        {
            ((Decorator)CaptureItems.Parent).Child = null;
            host.Child = CaptureItems;
        }

        Dock dock = _settings.CaptureBarPosition;
        bool vertical = floating || dock is Dock.Left or Dock.Right;
        DockPanel.SetDock(CaptureBar, dock);
        CaptureItems.Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal;
        CaptureItems.HorizontalAlignment = vertical ? HorizontalAlignment.Stretch : HorizontalAlignment.Center;
        CaptureItems.VerticalAlignment = vertical ? VerticalAlignment.Center : VerticalAlignment.Stretch;
        CaptureBar.Padding = vertical ? new Thickness(6, 8, 6, 8) : new Thickness(8, 6, 8, 6);
        CaptureBar.BorderThickness = dock switch
        {
            Dock.Right => new Thickness(1, 0, 0, 0),
            Dock.Top => new Thickness(0, 0, 0, 1),
            Dock.Bottom => new Thickness(0, 1, 0, 0),
            _ => new Thickness(0, 0, 1, 0)
        };
        if (floating)
        {
            ApplyFloatOrientation();
        }

        CaptureFloatGrip.Visibility = _settings.CaptureBarFloatLocked ? Visibility.Collapsed : Visibility.Visible;
        UpdateActionLabels(CaptureBar, CaptureItems);
        PlaceCaptureFloat();
    }

    private const double FloatMargin = 8;

    /// <summary>Binnen deze afstand van de boven- of onderrand klikken de zwevende knoppen vast tegen de rand en liggen ze horizontaal.</summary>
    private const double FloatSnap = 16;

    private Point? _floatDragOffset;

    private bool IsFloatHorizontal => _settings.CaptureBarFloatY is <= 0 or >= 1;

    private void ApplyFloatOrientation()
    {
        bool horizontal = IsFloatHorizontal;
        Orientation orientation = horizontal ? Orientation.Horizontal : Orientation.Vertical;
        if (CaptureFloatPanel.Orientation == orientation && CaptureItems.Orientation == orientation)
        {
            return;
        }

        CaptureFloatPanel.Orientation = orientation;
        CaptureItems.Orientation = orientation;
        CaptureItems.HorizontalAlignment = HorizontalAlignment.Stretch;
        CaptureItems.VerticalAlignment = VerticalAlignment.Stretch;
        CaptureFloatGrip.Text = horizontal ? "\uE784" : "\uE76F";
        CaptureFloatGrip.Padding = horizontal ? new Thickness(2, 0, 2, 0) : new Thickness(0, 2, 0, 2);
        CaptureFloat.Padding = horizontal ? new Thickness(2, 4, 4, 4) : new Thickness(4, 2, 4, 4);
    }

    private void PlaceCaptureFloat()
    {
        double rangeX = Math.Max(0, FloatLayer.ActualWidth - CaptureFloat.ActualWidth - 2 * FloatMargin);
        double rangeY = Math.Max(0, FloatLayer.ActualHeight - CaptureFloat.ActualHeight - 2 * FloatMargin);
        Canvas.SetLeft(CaptureFloat, FloatMargin + Math.Clamp(_settings.CaptureBarFloatX, 0, 1) * rangeX);
        Canvas.SetTop(CaptureFloat, FloatMargin + Math.Clamp(_settings.CaptureBarFloatY, 0, 1) * rangeY);
    }

    private void FloatLayer_SizeChanged(object sender, SizeChangedEventArgs e) => PlaceCaptureFloat();

    // Knoppen handelen hun eigen klik af; dit vuurt alleen op de rand en het greepje.
    private void CaptureFloat_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_settings.CaptureBarFloatLocked)
        {
            return;
        }

        _floatDragOffset = e.GetPosition(CaptureFloat);
        CaptureFloat.CaptureMouse();
        e.Handled = true;
    }

    private void CaptureFloat_MouseMove(object sender, MouseEventArgs e)
    {
        if (_floatDragOffset is not { } offset)
        {
            return;
        }

        Point p = e.GetPosition(FloatLayer);
        double rangeX = FloatLayer.ActualWidth - CaptureFloat.ActualWidth - 2 * FloatMargin;
        double rangeY = FloatLayer.ActualHeight - CaptureFloat.ActualHeight - 2 * FloatMargin;
        if (rangeX > 0)
        {
            _settings.CaptureBarFloatX = Math.Clamp((p.X - offset.X - FloatMargin) / rangeX, 0, 1);
        }

        if (rangeY > 0)
        {
            double top = p.Y - offset.Y - FloatMargin;
            _settings.CaptureBarFloatY = top <= FloatSnap ? 0
                : top >= rangeY - FloatSnap ? 1
                : Math.Clamp(top / rangeY, 0, 1);
        }

        bool wasHorizontal = CaptureFloatPanel.Orientation == Orientation.Horizontal;
        ApplyFloatOrientation();
        if (wasHorizontal != IsFloatHorizontal)
        {
            // Na het draaien valt het oude grijppunt mogelijk buiten de knoppen; houd het greepje onder de muis.
            _floatDragOffset = new Point(10, 10);
        }

        PlaceCaptureFloat();
    }

    private void SetCaptureFloatLocked(bool locked)
    {
        _settings.CaptureBarFloatLocked = locked;
        ApplyCaptureBarPosition();
    }

    private void CaptureFloat_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => CaptureFloat.ReleaseMouseCapture();

    private void CaptureFloat_LostMouseCapture(object sender, MouseEventArgs e) => _floatDragOffset = null;

    private void SetCaptureBarPosition(Dock? dock)
    {
        _settings.CaptureBarFloating = dock == null;
        if (dock is { } d)
        {
            _settings.CaptureBarPosition = d;
        }

        ApplyCaptureBarPosition();
    }

    private List<string> MergeToolbarOrder(IReadOnlyList<string> saved, IReadOnlyList<string> defaults)
    {
        List<string> order = saved.Where(_toolbarItems.ContainsKey).Distinct().ToList();

        // Items die nog niet in de opgeslagen volgorde staan (zoals nieuwe knoppen) komen na hun standaardvoorganger.
        for (int i = 0; i < defaults.Count; i++)
        {
            string id = defaults[i];
            if (!order.Contains(id))
            {
                order.Insert(i == 0 ? 0 : order.IndexOf(defaults[i - 1]) + 1, id);
            }
        }

        return order;
    }

    private void ApplyToolbarPosition()
    {
        Dock dock = IsSplitLayout ? Dock.Top : _settings.ToolbarPosition;
        bool vertical = dock is Dock.Left or Dock.Right;
        DockPanel.SetDock(Toolbar, dock);
        ToolbarItems.Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal;
        Toolbar.Padding = vertical ? new Thickness(6, 8, 6, 8) : new Thickness(8, 6, 8, 6);
        Toolbar.BorderThickness = dock switch
        {
            Dock.Bottom => new Thickness(0, 1, 0, 0),
            Dock.Left => new Thickness(0, 0, 1, 0),
            Dock.Right => new Thickness(1, 0, 0, 0),
            _ => new Thickness(0, 0, 0, 1)
        };
        UpdateToolbarLayout();
    }

    private void ApplyToolbarLock()
    {
        bool locked = _settings.ToolbarLocked;
        string background = locked ? "ToolbarBrush" : "UnlockedToolbarBrush";
        ToolbarItems.IsLocked = locked;
        ActionItems.IsLocked = locked;
        CaptureItems.IsLocked = locked;
        Toolbar.SetResourceReference(Border.BackgroundProperty, background);
        ActionBar.SetResourceReference(Border.BackgroundProperty, background);
        CaptureBar.SetResourceReference(Border.BackgroundProperty, background);
        CaptureFloat.SetResourceReference(Border.BackgroundProperty, background);
        UpdateHint();
    }

    private void Toolbar_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.WidthChanged)
        {
            UpdateToolbarLayout();
        }
    }

    private void UpdateToolbarLayout()
    {
        UpdateActionLabels(Toolbar, ToolbarItems);
        UpdateActionLabels(ActionBar, ActionItems);
        UpdateActionLabels(CaptureBar, CaptureItems);
    }

    // Labels alleen tonen als alles op één regel past; staat de werkbalk opzij of zweeft hij, dan altijd alleen iconen.
    private void UpdateActionLabels(Border bar, ToolbarPanel panel)
    {
        List<TextBlock> labels = new[] { NewLabel, ClearLabel, ScreencastLabel, SaveLabel, UploadLabel, OcrLabel, CopyLabel }
            .Where(label => PanelOf(label) == panel).ToList();
        if (labels.Count == 0)
        {
            return;
        }

        if (panel.Orientation == Orientation.Vertical || (panel == CaptureItems && _settings.CaptureBarFloating))
        {
            SetActionLabelsVisible(panel, labels, false);
            return;
        }

        double available = bar.ActualWidth - bar.Padding.Left - bar.Padding.Right
                           - bar.BorderThickness.Left - bar.BorderThickness.Right;
        SetActionLabelsVisible(panel, labels, true);
        panel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        SetActionLabelsVisible(panel, labels, panel.SingleLineLength <= available);
    }

    private static ToolbarPanel? PanelOf(DependencyObject element)
    {
        for (DependencyObject? d = element; d != null; d = VisualTreeHelper.GetParent(d))
        {
            if (d is ToolbarPanel panel)
            {
                return panel;
            }
        }

        return null;
    }

    private static void SetActionLabelsVisible(ToolbarPanel panel, IEnumerable<TextBlock> labels, bool visible)
    {
        Visibility visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        foreach (TextBlock label in labels)
        {
            label.Visibility = visibility;

            // Anders geeft een directe Measure() van het paneel nog de gecachete knopbreedtes terug.
            for (DependencyObject? d = label; d != null && d != panel; d = VisualTreeHelper.GetParent(d))
            {
                (d as UIElement)?.InvalidateMeasure();
            }
        }

        panel.InvalidateMeasure();
    }

    private void ToolbarItems_OrderChanged(object? sender, EventArgs e)
    {
        if (IsSplitLayout)
        {
            _settings.SplitToolbarOrder = AllToolbarItems.Select(ItemId).ToList();
        }
        else
        {
            _settings.ToolbarOrder = ToolbarItems.Items.Select(ItemId).ToList();
        }

        UpdateToolbarLayout();
    }

    private void SetEditorLayout(EditorLayout layout)
    {
        _settings.EditorLayout = layout;
        ApplyToolbarLayout();
    }

    private void Toolbar_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var bar = (Border)sender;
        ToolbarPanel panel = bar == ActionBar ? ActionItems : bar == CaptureBar || bar == CaptureFloat ? CaptureItems : ToolbarItems;
        ContextMenu menu = bar.ContextMenu;
        menu.Items.Clear();

        var lockItem = new MenuItem { Header = Strings.Toolbar_Lock, IsCheckable = true, IsChecked = _settings.ToolbarLocked };
        lockItem.Click += (_, _) => SetToolbarLocked(lockItem.IsChecked);
        menu.Items.Add(lockItem);

        if (IsThreeSplit && _settings.CaptureBarFloating)
        {
            var floatLock = new MenuItem { Header = Strings.Toolbar_LockFloating, IsCheckable = true, IsChecked = _settings.CaptureBarFloatLocked };
            floatLock.Click += (_, _) => SetCaptureFloatLocked(floatLock.IsChecked);
            menu.Items.Add(floatLock);
        }

        var layout = new MenuItem { Header = Strings.Toolbar_Layout };
        foreach ((EditorLayout value, string label) in new[]
                 {
                     (EditorLayout.Split, Strings.Settings_SplitLayout),
                     (EditorLayout.ThreeSplit, Strings.Toolbar_ThreeSplit),
                     (EditorLayout.Single, Strings.Toolbar_Single)
                 })
        {
            var item = new MenuItem { Header = label, IsCheckable = true, IsChecked = _settings.EditorLayout == value };
            item.Click += (_, _) => SetEditorLayout(value);
            layout.Items.Add(item);
        }

        menu.Items.Add(layout);

        if (IsThreeSplit)
        {
            var side = new MenuItem { Header = Strings.Toolbar_CaptureBarPosition };
            foreach ((Dock? dock, string label) in new (Dock?, string)[]
                     {
                         (Dock.Left, Strings.Common_Left), (Dock.Right, Strings.Common_Right), (Dock.Top, Strings.Common_Top), (Dock.Bottom, Strings.Common_Bottom), (null, Strings.Common_Floating)
                     })
            {
                bool isChecked = dock == null
                    ? _settings.CaptureBarFloating
                    : !_settings.CaptureBarFloating && _settings.CaptureBarPosition == dock;
                var item = new MenuItem { Header = label, IsCheckable = true, IsChecked = isChecked };
                item.Click += (_, _) => SetCaptureBarPosition(dock);
                side.Items.Add(item);
            }

            menu.Items.Add(side);
        }

        if (!IsSplitLayout)
        {
            var position = new MenuItem { Header = Strings.Toolbar_Position };
            foreach ((Dock dock, string label) in new[] { (Dock.Top, Strings.Common_Top), (Dock.Bottom, Strings.Common_Bottom), (Dock.Left, Strings.Common_Left), (Dock.Right, Strings.Common_Right) })
            {
                var item = new MenuItem { Header = label, IsCheckable = true, IsChecked = _settings.ToolbarPosition == dock };
                item.Click += (_, _) =>
                {
                    _settings.ToolbarPosition = dock;
                    ApplyToolbarPosition();
                };
                position.Items.Add(item);
            }

            menu.Items.Add(position);
        }

        if (panel.Items.Any(i => i is ToolbarSpacer))
        {
            var swap = new MenuItem { Header = Strings.Toolbar_SwapGroups };
            swap.Click += (_, _) => SwapToolbarGroups(panel);
            menu.Items.Add(swap);
        }

        var buttons = new MenuItem { Header = Strings.Toolbar_ShowButtons };
        foreach (UIElement element in AllToolbarItems)
        {
            if (ToolbarPanel.GetTitle(element) is not { } title || (element == UploadButton && !CanUpload))
            {
                continue;
            }

            var item = new MenuItem { Header = title, IsCheckable = true, IsChecked = element.Visibility == Visibility.Visible, StaysOpenOnClick = true };
            item.Click += (_, _) => SetToolbarItemVisible(element, item.IsChecked);
            buttons.Items.Add(item);
        }

        menu.Items.Add(buttons);
        menu.Items.Add(new Separator());

        var reset = new MenuItem { Header = Strings.Toolbar_Reset };
        reset.Click += (_, _) => ResetToolbar();
        menu.Items.Add(reset);
    }

    private void SetToolbarLocked(bool locked)
    {
        _settings.ToolbarLocked = locked;
        ApplyToolbarLock();
    }

    private void SetToolbarItemVisible(UIElement element, bool visible)
    {
        element.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        string id = ItemId(element);
        _settings.ToolbarHidden.Remove(id);
        if (!visible)
        {
            _settings.ToolbarHidden.Add(id);
        }

        UpdateToolbarLayout();
    }

    /// <summary>De groepen tussen de flexibele ruimtes in omgekeerde volgorde zetten; een middengroep blijft in het midden.</summary>
    private void SwapToolbarGroups(ToolbarPanel panel)
    {
        var groups = new List<List<UIElement>> { new() };
        var spacers = new List<UIElement>();
        foreach (UIElement item in panel.Items)
        {
            if (item is ToolbarSpacer)
            {
                spacers.Add(item);
                groups.Add(new List<UIElement>());
            }
            else
            {
                groups[^1].Add(item);
            }
        }

        groups.Reverse();
        var order = new List<UIElement>(groups[0]);
        for (int i = 0; i < spacers.Count; i++)
        {
            order.Add(spacers[i]);
            order.AddRange(groups[i + 1]);
        }

        panel.Children.Clear();
        order.ForEach(item => panel.Children.Add(item));
        ToolbarItems_OrderChanged(this, EventArgs.Empty);
    }

    private void ResetToolbar()
    {
        _settings.ToolbarOrder = new List<string>();
        _settings.SplitToolbarOrder = new List<string>();
        _settings.ToolbarHidden = new List<string>();
        _settings.ToolbarPosition = Dock.Top;
        _settings.CaptureBarPosition = Dock.Left;
        _settings.CaptureBarFloating = true;
        _settings.CaptureBarFloatX = 0;
        _settings.CaptureBarFloatY = 0.5;
        _settings.CaptureBarFloatLocked = false;
        _settings.ToolbarLocked = true;
        ApplyToolbarLayout();
        foreach (UIElement item in _toolbarItems.Values)
        {
            item.Visibility = Visibility.Visible;
        }

        ApplyUploadButton();
        ApplyToolbarLock();
    }

    // ---------------------------------------------------------------- Gereedschappen

    private void SelectTool(ToolKind tool)
    {
        foreach (RadioButton button in _toolbarItems.Values.OfType<RadioButton>())
        {
            if ((string)button.Tag == tool.ToString())
            {
                button.IsChecked = true;
                return;
            }
        }
    }

    private void Tool_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string tag } || !Enum.TryParse(tag, out ToolKind tool))
        {
            return;
        }

        CommitTextEdit();
        CancelDrag();
        _tool = tool;
        _settings.LastTool = tool;
        if (tool != ToolKind.Select)
        {
            SetSelected(null);
        }

        Surface.Cursor = CursorForTool(tool);
        Surface.BrushPreview = null;
        if (tool == ToolKind.Emoji)
        {
            EmojiCatalog.Preload();
        }

        UpdatePropsPanel();
        UpdateHint();
    }

    private Cursor CursorForTool(ToolKind tool) => tool switch
    {
        ToolKind.Select => Cursors.Arrow,
        ToolKind.Text => Cursors.IBeam,
        ToolKind.Step or ToolKind.Cursor or ToolKind.Emoji => Cursors.Hand,
        ToolKind.Eraser when IsBrushEraser => Cursors.None,
        _ => Cursors.Cross
    };

    private StyleSet StyleFor(ToolKind tool) => _settings.StyleFor(tool);

    private bool IsBrushEraser => StyleFor(ToolKind.Eraser).EraserMode == EraserMode.Brush;

    private Annotation? CreateAnnotation(ToolKind tool, Point p)
    {
        StyleSet style = StyleFor(tool).Clone();
        return tool switch
        {
            ToolKind.Rectangle => new ShapeAnnotation(false) { Rect = new Rect(p, p), Style = style },
            ToolKind.Ellipse => new ShapeAnnotation(true) { Rect = new Rect(p, p), Style = style },
            ToolKind.Shape => new FigureAnnotation { Rect = new Rect(p, p), Style = style },
            ToolKind.Line => new LineAnnotation(false) { Start = p, End = p, Style = style },
            ToolKind.Arrow => new LineAnnotation(true) { Start = p, End = p, Style = style },
            ToolKind.Highlight => new HighlightAnnotation { Points = { p, p }, Straight = _settings.StraightHighlight, Style = style },
            ToolKind.Blur => new BlurAnnotation { Rect = new Rect(p, p), Style = style },
            ToolKind.Step => new StepAnnotation { Center = p, Style = style },
            ToolKind.Cursor => new CursorAnnotation { Position = p, Style = style },
            ToolKind.Emoji => new EmojiAnnotation { Center = p, Style = style },
            _ => null
        };
    }

    // ---------------------------------------------------------------- Muis

    private double HitTolerance => 4 / Surface.ViewScale;

    private Annotation? HitTest(Point p)
    {
        for (int i = _doc.Items.Count - 1; i >= 0; i--)
        {
            if (_doc.Items[i].HitTest(p, HitTolerance))
            {
                return _doc.Items[i];
            }
        }

        return null;
    }

    private void Surface_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        CommitTextEdit();
        Surface.Focus();
        Point p = e.GetPosition(Surface);
        _dragStart = _lastPoint = p;
        _dragChanged = false;
        _createdOnDown = false;
        _pending = _doc.CaptureState();

        if (_selected != null && Surface.HitRotateHandle(_selected, p))
        {
            if (e.ClickCount == 2)
            {
                SetAngle(_selected, 0);
                _pending = null;
                return;
            }

            StartDrag(DragMode.Rotating);
            return;
        }

        if (_selected != null)
        {
            int handle = Surface.HitHandle(_selected, p);
            if (handle >= 0 && e.ClickCount == 2 && _selected.RemoveHandle(handle))
            {
                Commit(_pending);
                _pending = null;
                Surface.InvalidateVisual();
                return;
            }

            if (handle >= 0)
            {
                _handleIndex = handle;
                _selected.BeginHandleDrag(handle);
                StartDrag(DragMode.Handle);
                return;
            }
        }

        if (_tool == ToolKind.Select && Surface.ShowGuides && Surface.HitGuide(p, out bool vertical) is int guide and >= 0)
        {
            _pending = null;
            _guideDrag = vertical ? _guidesX : _guidesY;
            _guideIndex = guide;
            StartDrag(DragMode.Guide);
            return;
        }

        switch (_tool)
        {
            case ToolKind.Select:
            {
                Annotation? hit = HitTest(p);
                SetSelected(hit);
                if (hit is TextAnnotation text && e.ClickCount == 2)
                {
                    BeginTextEdit(text, false, _pending);
                    return;
                }

                if (hit != null)
                {
                    StartDrag(DragMode.Moving);
                }

                break;
            }

            case ToolKind.Crop:
                p = _dragStart = _lastPoint = SnapPoint(p);
                Surface.CropRect = new Rect(p, p);
                StartDrag(DragMode.Cropping);
                break;

            case ToolKind.Eraser when IsBrushEraser:
                PrepareRasterEdit();
                EraseStroke(p, p);
                StartDrag(DragMode.Erasing);
                break;

            case ToolKind.Eraser:
            {
                PrepareRasterEdit();
                Int32Rect? erased = PixelEraser.FloodErase(_doc.Pixels, _doc.Width, _doc.Height,
                    (int)Math.Floor(p.X), (int)Math.Floor(p.Y), StyleFor(ToolKind.Eraser).Tolerance);
                bool flattened = _doc.Items.Count == 0 && _pending?.Items.Count > 0;
                _doc.EndPixelEdit();
                if (erased != null || flattened)
                {
                    Commit(_pending);
                }

                _pending = null;
                UpdatePropsPanel();
                break;
            }

            case ToolKind.Text:
            {
                if (HitTest(p) is TextAnnotation existing)
                {
                    SetSelected(existing);
                    BeginTextEdit(existing, false, _pending);
                    return;
                }

                var text = new TextAnnotation { Position = p, Style = StyleFor(ToolKind.Text).Clone() };
                text.TailTip = text.DefaultTailTip(text.Box);
                _doc.Items.Add(text);
                BeginTextEdit(text, true, _pending);
                return;
            }

            case ToolKind.Step:
            case ToolKind.Cursor:
            case ToolKind.Emoji:
            {
                Annotation? stamp = CreateAnnotation(_tool, p);
                if (stamp == null)
                {
                    return;
                }

                _doc.Items.Add(stamp);
                SetSelected(stamp);
                _createdOnDown = true;
                StartDrag(DragMode.Moving);
                break;
            }

            default:
            {
                p = _dragStart = _lastPoint = SnapPoint(p);
                _active = CreateAnnotation(_tool, p);
                if (_active == null)
                {
                    return;
                }

                _strokePoints = new List<Point> { p };
                _doc.Items.Add(_active);
                SetSelected(null);
                StartDrag(DragMode.Creating);
                break;
            }
        }

        Surface.InvalidateVisual();
    }

    /// <summary>Voegt eventuele aantekeningen samen met de afbeelding en begint een bewerking op de pixels.</summary>
    private void PrepareRasterEdit()
    {
        SetSelected(null);
        if (_doc.Items.Count > 0)
        {
            _doc.Flatten(DocumentRenderer.Export(_doc, includeBackground: false));
            ShowTemporaryHint(Strings.Editor_FlattenedForEraser);
        }

        _doc.BeginPixelEdit();
    }

    private void EraseStroke(Point from, Point to)
    {
        double radius = StyleFor(ToolKind.Eraser).Thickness / 2;
        if (PixelEraser.EraseSegment(_doc.Pixels, _doc.Width, _doc.Height, from, to, radius) is Int32Rect area)
        {
            _doc.InvalidatePixels(area);
        }
    }

    private void UpdateBrushPreview(Point? p)
    {
        Surface.BrushPreview = p is Point center && _tool == ToolKind.Eraser && IsBrushEraser
            ? (center, StyleFor(ToolKind.Eraser).Thickness / 2)
            : null;
    }

    private void StartDrag(DragMode mode)
    {
        _drag = mode;
        if (mode == DragMode.Moving && _selected != null)
        {
            _moveStartBounds = _selected.Bounds;
        }

        Surface.CaptureMouse();
    }

    private void Surface_MouseMove(object sender, MouseEventArgs e)
    {
        Point p = e.GetPosition(Surface);
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        HRuler.SetMarker(p.X);
        VRuler.SetMarker(p.Y);
        if (_tool == ToolKind.Eraser && IsBrushEraser)
        {
            UpdateBrushPreview(p);
            Surface.InvalidateVisual();
        }

        switch (_drag)
        {
            case DragMode.None:
                UpdateHoverCursor(p);
                return;

            case DragMode.Erasing:
                EraseStroke(_lastPoint, p);
                break;

            case DragMode.Creating when _active != null:
                UpdateCreating(_active, p = SnapPoint(p), shift);
                break;

            case DragMode.Moving when _selected != null:
            {
                // Vanaf de beginpositie rekenen, zodat het object na het vastklikken gewoon weer loslaat.
                Rect target = Rect.Offset(_moveStartBounds, p - _dragStart);
                target.Offset(SnapMove(target));
                _selected.Move(target.TopLeft - _selected.Bounds.TopLeft);
                break;
            }

            case DragMode.Handle when _selected != null:
                _selected.MoveHandle(_handleIndex, p = SnapPoint(p), shift);
                break;

            case DragMode.Rotating when _selected != null:
                _selected.Angle = RotationFromPointer(_selected.Pivot, p, shift);
                HintText.Text = string.Format(Strings.Editor_RotationHint, Math.Round(_selected.Angle));
                break;

            case DragMode.Cropping:
                p = SnapPoint(p);
                Surface.CropRect = ClampToImage(RectAnnotation.FromAnchor(_dragStart, p, shift));
                break;

            case DragMode.Guide:
                UpdateGuideDrag(p);
                return;
        }

        if ((p - _dragStart).Length > 0.5)
        {
            _dragChanged = true;
        }

        _lastPoint = p;
        Surface.InvalidateVisual();
    }

    private void UpdateCreating(Annotation item, Point p, bool shift)
    {
        switch (item)
        {
            case RectAnnotation rect:
                rect.Rect = RectAnnotation.FromAnchor(_dragStart, p, shift);
                break;

            case LineAnnotation line:
                line.End = shift ? RenderHelpers.SnapAngle(line.Start, p) : p;
                break;

            case HighlightAnnotation highlight:
                _strokePoints?.Add(p);
                bool straight = _settings.StraightHighlight ^ shift;
                highlight.Straight = straight;
                highlight.Points = straight
                    ? new List<Point> { _dragStart, new(p.X, _dragStart.Y) }
                    : new List<Point>(_strokePoints ?? new List<Point> { _dragStart, p });
                break;
        }
    }

    private void Surface_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_drag == DragMode.None)
        {
            return;
        }

        DragMode mode = _drag;
        _drag = DragMode.None;
        Surface.ReleaseMouseCapture();

        switch (mode)
        {
            case DragMode.Creating when _active != null:
                if (_active.IsDegenerate)
                {
                    _doc.Items.Remove(_active);
                }
                else
                {
                    Commit(_pending);
                    SetSelected(_active);
                }

                _active = null;
                _strokePoints = null;
                break;

            case DragMode.Moving:
            case DragMode.Handle:
                if (_dragChanged || _createdOnDown)
                {
                    Commit(_pending);
                }

                break;

            case DragMode.Rotating:
                if (_dragChanged)
                {
                    Commit(_pending);
                }

                UpdateHint();
                break;

            case DragMode.Cropping:
                ApplyCrop();
                break;

            case DragMode.Guide:
                EndGuideDrag();
                break;

            case DragMode.Erasing:
                _doc.EndPixelEdit();
                Commit(_pending);
                break;
        }

        Surface.SnapX = Surface.SnapY = null;
        _pending = null;
        Surface.InvalidateVisual();
        UpdatePropsPanel();
    }

    private void Surface_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_drag != DragMode.None && Mouse.LeftButton == MouseButtonState.Released)
        {
            Surface_MouseLeftButtonUp(sender, new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left));
        }
    }

    private void CancelDrag()
    {
        if (_drag == DragMode.None)
        {
            return;
        }

        _drag = DragMode.None;
        Surface.ReleaseMouseCapture();
        _doc.EndPixelEdit();
        if (_pending != null)
        {
            _doc.Restore(_pending);
        }

        EndGuideDrag();
        _active = null;
        _pending = null;
        Surface.CropRect = null;
        Surface.SnapX = Surface.SnapY = null;
        SetSelected(null);
    }

    private void UpdateHoverCursor(Point p)
    {
        if (_selected != null)
        {
            if (Surface.HitRotateHandle(_selected, p))
            {
                Surface.Cursor = Cursors.Hand;
                return;
            }

            int handle = Surface.HitHandle(_selected, p);
            if (handle >= 0)
            {
                Surface.Cursor = HandleCursor(_selected, handle);
                return;
            }
        }

        if (_tool == ToolKind.Select && Surface.ShowGuides && Surface.HitGuide(p, out bool vertical) >= 0)
        {
            Surface.Cursor = vertical ? Cursors.SizeWE : Cursors.SizeNS;
        }
        else if (_tool == ToolKind.Select)
        {
            Surface.Cursor = HitTest(p) != null ? Cursors.SizeAll : Cursors.Arrow;
        }
        else if (_tool == ToolKind.Text && HitTest(p) is TextAnnotation)
        {
            Surface.Cursor = Cursors.IBeam;
        }
        else
        {
            Surface.Cursor = CursorForTool(_tool);
        }
    }

    private static Cursor HandleCursor(Annotation item, int handle)
    {
        if (item is not (RectAnnotation or TextAnnotation) || handle > 7)
        {
            return Cursors.SizeAll;
        }

        // De grepen liggen in stappen van 45° rond het kader; bij een gedraaid object schuift de pijlrichting mee.
        handle = ((handle + (int)Math.Round(item.Angle / 45)) % 8 + 8) % 8;
        return handle switch
        {
            0 or 4 => Cursors.SizeNWSE,
            2 or 6 => Cursors.SizeNESW,
            1 or 5 => Cursors.SizeNS,
            _ => Cursors.SizeWE
        };
    }

    private void Surface_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        CommitTextEdit();
        Point point = e.GetPosition(Surface);
        Annotation? hit = HitTest(point);
        SetSelected(hit);
        bool has = hit != null;
        int index = hit != null ? _doc.Items.IndexOf(hit) : -1;
        int last = _doc.Items.Count - 1;

        var menu = new ContextMenu { PlacementTarget = Surface };
        menu.Items.Add(MenuItem(Strings.Tool_Undo, "\uE7A7", "Ctrl+Z", Undo, _undo.CanUndo));
        menu.Items.Add(MenuItem(Strings.Tool_Redo, "\uE7A6", "Ctrl+Y", Redo, _undo.CanRedo));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem(Strings.Menu_Delete, "\uE74D", "Delete", DeleteSelected, has));
        menu.Items.Add(MenuItem(Strings.Menu_DeleteAll, "\uE894", "Shift+Delete", DeleteAll, last >= 0));
        menu.Items.Add(MenuItem(Strings.Menu_Flatten, "\uE81E", "Ctrl+Shift+F", Flatten, last >= 0));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem(Strings.Menu_Cut, "\uE8C6", "Ctrl+X", () => CutAnnotation(hit!), has));
        menu.Items.Add(MenuItem(Strings.Menu_Copy, "\uE8C8", "Ctrl+Shift+C", () => CopyAnnotation(hit!), has));
        menu.Items.Add(MenuItem(Strings.Menu_Paste, "\uE77F", "Ctrl+V", () => Paste(point), CanPaste()));
        menu.Items.Add(MenuItem(Strings.Menu_Duplicate, "\uE710", "Ctrl+D", () => Duplicate(hit!), has));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem(Strings.Menu_BringToFront, "\uE898", "Home", () => Reorder(hit!, ZOrder.Front), has && index < last));
        menu.Items.Add(MenuItem(Strings.Menu_BringForward, "\uE74A", "PageUp", () => Reorder(hit!, ZOrder.Forward), has && index < last));
        menu.Items.Add(MenuItem(Strings.Menu_SendBackward, "\uE74B", "PageDown", () => Reorder(hit!, ZOrder.Backward), has && index > 0));
        menu.Items.Add(MenuItem(Strings.Menu_SendToBack, "\uE896", "End", () => Reorder(hit!, ZOrder.Back), has && index > 0));
        var align = new MenuItem { Header = Strings.Menu_AlignToImage, IsEnabled = has };
        align.Items.Add(MenuItem(Strings.Common_Left, "\uE8E4", "", () => AlignSelected(0, null)));
        align.Items.Add(MenuItem(Strings.Menu_CenterHorizontally, "\uE8E3", "", () => AlignSelected(0.5, null)));
        align.Items.Add(MenuItem(Strings.Common_Right, "\uE8E2", "", () => AlignSelected(1, null)));
        align.Items.Add(new Separator());
        align.Items.Add(MenuItem(Strings.Common_Top, "", "", () => AlignSelected(null, 0)));
        align.Items.Add(MenuItem(Strings.Menu_CenterVertically, "", "", () => AlignSelected(null, 0.5)));
        align.Items.Add(MenuItem(Strings.Common_Bottom, "", "", () => AlignSelected(null, 1)));
        align.Items.Add(new Separator());
        align.Items.Add(MenuItem(Strings.Menu_CenterOfImage, "", "", () => AlignSelected(0.5, 0.5)));
        menu.Items.Add(align);
        menu.Items.Add(MenuItem(Strings.Arrange_FlipH, "", "", () => FlipSelected(true), hit?.CanFlip == true));
        menu.Items.Add(MenuItem(Strings.Arrange_FlipV, "", "", () => FlipSelected(false), hit?.CanFlip == true));
        if (hit?.CanRotate == true)
        {
            menu.Items.Add(MenuItem(Strings.Menu_RotateRight, "\uE7AD", "", () => RotateSelected(90)));
            menu.Items.Add(MenuItem(Strings.Arrange_RotateLeft, "", "", () => RotateSelected(-90)));
            menu.Items.Add(MenuItem(Strings.Menu_ResetRotation, "", "", () => SetAngle(hit, 0), hit.Angle != 0));
        }

        menu.Items.Add(new Separator());
        if (_settings.ShowRulers && _guidesX.Count + _guidesY.Count > 0)
        {
            menu.Items.Add(MenuItem(Strings.Menu_ClearGuides, "\uE894", "", ClearGuides));
        }

        menu.Items.Add(_toolbarsHidden
            ? MenuItem(Strings.Menu_ShowToolbars, "\uE7B3", "Ctrl+H", ToggleToolbars)
            : MenuItem(Strings.Menu_HideToolbars, "\uED1A", "Ctrl+H", ToggleToolbars));
        menu.IsOpen = true;
        e.Handled = true;
    }

    private static MenuItem MenuItem(string header, string glyph, string gesture, Action action, bool isEnabled = true)
    {
        var item = new MenuItem
        {
            Header = header,
            InputGestureText = gesture,
            IsEnabled = isEnabled,
            Icon = new TextBlock
            {
                Text = glyph,
                FontFamily = (FontFamily)Application.Current.FindResource("IconFont"),
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        item.Click += (_, _) => action();
        return item;
    }

    // ---------------------------------------------------------------- Bewerkingen

    private void Commit(DocumentState? stateBefore)
    {
        if (stateBefore != null)
        {
            _undo.Push(stateBefore);
        }

        SetDirty(true);
    }

    private void SetSelected(Annotation? item)
    {
        _selected = item;
        Surface.Selected = item;
        _lastStyleTarget = null;
        UpdatePropsPanel();
        UpdateCommandStates();
        Surface.InvalidateVisual();
    }

    private void DeleteSelected()
    {
        if (_selected == null)
        {
            return;
        }

        Commit(_doc.CaptureState());
        _doc.Items.Remove(_selected);
        SetSelected(null);
    }

    private void DeleteAll()
    {
        if (_doc.Items.Count == 0)
        {
            return;
        }

        Commit(_doc.CaptureState());
        _doc.Items.Clear();
        SetSelected(null);
    }

    private void Flatten()
    {
        CommitTextEdit();
        if (_doc.Items.Count == 0)
        {
            return;
        }

        Commit(_doc.CaptureState());
        _doc.Flatten(DocumentRenderer.Export(_doc, includeBackground: false));
        SetSelected(null);
        ShowTemporaryHint(Strings.Editor_Flattened);
    }

    private void Reorder(Annotation item, ZOrder order)
    {
        int index = _doc.Items.IndexOf(item);
        int last = _doc.Items.Count - 1;
        int target = order switch
        {
            ZOrder.Front => last,
            ZOrder.Forward => Math.Min(index + 1, last),
            ZOrder.Backward => Math.Max(index - 1, 0),
            _ => 0
        };
        if (index < 0 || target == index)
        {
            return;
        }

        Commit(_doc.CaptureState());
        _doc.Items.RemoveAt(index);
        _doc.Items.Insert(target, item);
        Surface.InvalidateVisual();
        UpdateCommandStates();
    }

    /// <param name="x">0 = links, 0,5 = midden, 1 = rechts op de afbeelding; null laat de horizontale positie staan.</param>
    /// <param name="y">0 = boven, 0,5 = midden, 1 = onder; null laat de verticale positie staan.</param>
    private void AlignSelected(double? x, double? y)
    {
        if (_selected == null)
        {
            return;
        }

        Rect b = _selected.Bounds;
        var delta = new Vector(
            x is double fx ? (_doc.Width - b.Width) * fx - b.Left : 0,
            y is double fy ? (_doc.Height - b.Height) * fy - b.Top : 0);
        if (delta.Length < 1e-6)
        {
            return;
        }

        Commit(_doc.CaptureState());
        _selected.Move(delta);
        Surface.InvalidateVisual();
    }

    private void FlipSelected(bool horizontal)
    {
        if (_selected is not { CanFlip: true } item)
        {
            return;
        }

        Commit(_doc.CaptureState());
        Rect before = item.Bounds;
        item.Flip(horizontal);
        Rect after = item.Bounds;
        item.Move(new Point(before.X + before.Width / 2, before.Y + before.Height / 2)
                  - new Point(after.X + after.Width / 2, after.Y + after.Height / 2));
        Surface.InvalidateVisual();
    }

    private void ArrangeToggle_Changed(object sender, RoutedEventArgs e)
    {
        bool open = ArrangeToggle.IsChecked == true;
        SetVisible(ArrangeButtons, open);
        ArrangeChevron.Text = open ? "\uE76B" : "\uE76C";
    }

    private void AlignLeft_Click(object sender, RoutedEventArgs e) => AlignSelected(0, null);

    private void AlignCenter_Click(object sender, RoutedEventArgs e) => AlignSelected(0.5, null);

    private void AlignRight_Click(object sender, RoutedEventArgs e) => AlignSelected(1, null);

    private void AlignTop_Click(object sender, RoutedEventArgs e) => AlignSelected(null, 0);

    private void AlignMiddle_Click(object sender, RoutedEventArgs e) => AlignSelected(null, 0.5);

    private void AlignBottom_Click(object sender, RoutedEventArgs e) => AlignSelected(null, 1);

    /// <summary>Hoek van de draaigreep ten opzichte van het midden; 0° is recht boven het object.</summary>
    private static double RotationFromPointer(Point pivot, Point p, bool shift)
    {
        double angle = Math.Atan2(p.Y - pivot.Y, p.X - pivot.X) * 180 / Math.PI + 90;
        if (shift)
        {
            angle = Math.Round(angle / 15) * 15;
        }
        else if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
        {
            double right = Math.Round(angle / 90) * 90;
            if (Math.Abs(angle - right) < 4)
            {
                angle = right;
            }
        }

        return Annotation.NormalizeAngle(angle);
    }

    private void RotateSelected(double delta)
    {
        if (_selected is { CanRotate: true } item)
        {
            SetAngle(item, item.Angle + delta);
        }
    }

    private void SetAngle(Annotation item, double angle)
    {
        angle = Annotation.NormalizeAngle(angle);
        if (!item.CanRotate || item.Angle == angle)
        {
            return;
        }

        Commit(_doc.CaptureState());
        item.Angle = angle;
        Surface.InvalidateVisual();
    }

    private void RotateLeft_Click(object sender, RoutedEventArgs e) => RotateSelected(-90);

    private void RotateRight_Click(object sender, RoutedEventArgs e) => RotateSelected(90);

    private void FlipH_Click(object sender, RoutedEventArgs e) => FlipSelected(true);

    private void FlipV_Click(object sender, RoutedEventArgs e) => FlipSelected(false);

    private void BringForward_Click(object sender, RoutedEventArgs e)
    {
        if (_selected != null)
        {
            Reorder(_selected, ZOrder.Forward);
        }
    }

    private void SendBackward_Click(object sender, RoutedEventArgs e)
    {
        if (_selected != null)
        {
            Reorder(_selected, ZOrder.Backward);
        }
    }

    // ---------------------------------------------------------------- Linialen en hulplijnen

    private void ApplyRulers()
    {
        bool show = _settings.ShowRulers;
        Visibility visibility = show ? Visibility.Visible : Visibility.Collapsed;
        HRuler.Visibility = visibility;
        VRuler.Visibility = visibility;
        RulerCorner.Visibility = visibility;
        RulersToggle.IsChecked = show;
        Surface.ShowGuides = show;
        RefreshGuides();
        UpdateRulers();
    }

    private void ToggleRulers()
    {
        _settings.ShowRulers = !_settings.ShowRulers;
        ApplyRulers();
    }

    private void Rulers_Click(object sender, RoutedEventArgs e)
    {
        _settings.ShowRulers = RulersToggle.IsChecked == true;
        ApplyRulers();
    }

    private void CanvasHost_LayoutUpdated(object? sender, EventArgs e) => UpdateRulers();

    private void UpdateRulers()
    {
        if (!_settings.ShowRulers || PresentationSource.FromVisual(Surface) == null)
        {
            return;
        }

        HRuler.SetView(Surface.TranslatePoint(new Point(0, 0), HRuler).X, Surface.ViewScale, _doc.Width);
        VRuler.SetView(Surface.TranslatePoint(new Point(0, 0), VRuler).Y, Surface.ViewScale, _doc.Height);
    }

    private void RefreshGuides()
    {
        HRuler.SetGuides(_guidesX);
        VRuler.SetGuides(_guidesY);
        Surface.InvalidateVisual();
    }

    private void ClearGuides()
    {
        _guidesX.Clear();
        _guidesY.Clear();
        RefreshGuides();
    }

    private void Surface_MouseLeave(object sender, MouseEventArgs e)
    {
        HRuler.SetMarker(null);
        VRuler.SetMarker(null);
        if (Surface.BrushPreview != null && _drag != DragMode.Erasing)
        {
            UpdateBrushPreview(null);
            Surface.InvalidateVisual();
        }
    }

    private void Ruler_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        CommitTextEdit();
        var ruler = (RulerControl)sender;

        // De bovenste liniaal geeft een horizontale hulplijn, de linker een verticale.
        _guideDrag = ruler == HRuler ? _guidesY : _guidesX;
        _guideDrag.Add(0);
        _guideIndex = _guideDrag.Count - 1;
        ruler.CaptureMouse();
        UpdateGuideDrag(e.GetPosition(Surface));
        e.Handled = true;
    }

    private void Ruler_MouseMove(object sender, MouseEventArgs e)
    {
        if (_guideDrag != null && ((UIElement)sender).IsMouseCaptured)
        {
            UpdateGuideDrag(e.GetPosition(Surface));
        }
    }

    private void Ruler_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => ((UIElement)sender).ReleaseMouseCapture();

    private void Ruler_LostMouseCapture(object sender, MouseEventArgs e) => EndGuideDrag();

    private void UpdateGuideDrag(Point p)
    {
        if (_guideDrag == null)
        {
            return;
        }

        bool vertical = _guideDrag == _guidesX;
        double value = Math.Round(vertical ? p.X : p.Y);
        _guideDrag[_guideIndex] = value;
        HRuler.SetMarker(vertical ? value : null);
        VRuler.SetMarker(vertical ? null : value);
        HintText.Text = string.Format(Strings.Editor_GuideHint, vertical ? "X" : "Y", value);
        RefreshGuides();
    }

    /// <summary>Een hulplijn die buiten de afbeelding of op een liniaal wordt losgelaten, verdwijnt.</summary>
    private void EndGuideDrag()
    {
        if (_guideDrag == null)
        {
            return;
        }

        List<double> list = _guideDrag;
        double value = list[_guideIndex];
        double size = list == _guidesX ? _doc.Width : _doc.Height;
        Point s = Mouse.GetPosition(Scroller);
        bool onCanvas = s.X >= 0 && s.Y >= 0 && s.X < Scroller.ViewportWidth && s.Y < Scroller.ViewportHeight;
        if (!onCanvas || value < 0 || value > size)
        {
            list.RemoveAt(_guideIndex);
        }

        _guideDrag = null;
        _guideIndex = -1;
        UpdateHint();
        RefreshGuides();
    }

    private void RulerCorner_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = RulerCorner };
        menu.Items.Add(MenuItem(Strings.Menu_ClearGuides, "\uE894", "", ClearGuides, _guidesX.Count + _guidesY.Count > 0));
        menu.Items.Add(MenuItem(Strings.Menu_HideRulers, "\uED1A", "Ctrl+R", ToggleRulers));
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void RulerFolder_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = RulerFolderButton, Placement = PlacementMode.Bottom };
        menu.Items.Add(MenuItem(Strings.Menu_OpenScreenshotFolder, "\uE91B", "", () => Kniip.History.HistoryActions.OpenFolder(ScreenshotFolder)));
        menu.Items.Add(MenuItem(Strings.Menu_OpenScreencastFolder, "\uE714", "", () => Kniip.History.HistoryActions.OpenFolder(App.Instance.VideoFolder)));
        menu.IsOpen = true;
    }

    private string ScreenshotFolder =>
        new[] { _settings.SnipSaveFolder, _settings.LastSaveFolder, Kniip.Capture.CaptureCoordinator.ScreenshotsFolder }
            .FirstOrDefault(folder => folder is { Length: > 0 } && System.IO.Directory.Exists(folder))
        ?? Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);

    private bool SnapEnabled => _settings.ShowRulers && !Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);

    /// <summary>Kleinste verschuiving waarmee een van de waarden op een hulplijn of op de rand of het midden van de afbeelding valt.</summary>
    private double? SnapOffset(bool vertical, double[] values, out double line)
    {
        double size = vertical ? _doc.Width : _doc.Height;
        double limit = SnapDistance / Surface.ViewScale;
        double? best = null;
        line = 0;
        foreach (double target in (vertical ? _guidesX : _guidesY).Concat(new[] { 0, size / 2, size }))
        {
            foreach (double value in values)
            {
                double offset = target - value;
                if (Math.Abs(offset) <= limit && (best == null || Math.Abs(offset) < Math.Abs(best.Value)))
                {
                    best = offset;
                    line = target;
                }
            }
        }

        return best;
    }

    private Point SnapPoint(Point p)
    {
        Surface.SnapX = Surface.SnapY = null;
        if (!SnapEnabled)
        {
            return p;
        }

        if (SnapOffset(true, new[] { p.X }, out double x) is double dx)
        {
            p.X += dx;
            Surface.SnapX = x;
        }

        if (SnapOffset(false, new[] { p.Y }, out double y) is double dy)
        {
            p.Y += dy;
            Surface.SnapY = y;
        }

        return p;
    }

    /// <summary>Verschuiving waarmee de linker-, midden- of rechterkant (en boven, midden of onder) van het kader vastklikt.</summary>
    private Vector SnapMove(Rect bounds)
    {
        Surface.SnapX = Surface.SnapY = null;
        var adjust = new Vector();
        if (!SnapEnabled)
        {
            return adjust;
        }

        if (SnapOffset(true, new[] { bounds.Left, bounds.Left + bounds.Width / 2, bounds.Right }, out double x) is double dx)
        {
            adjust.X = dx;
            Surface.SnapX = x;
        }

        if (SnapOffset(false, new[] { bounds.Top, bounds.Top + bounds.Height / 2, bounds.Bottom }, out double y) is double dy)
        {
            adjust.Y = dy;
            Surface.SnapY = y;
        }

        return adjust;
    }

    private void CopyAnnotation(Annotation item)
    {
        if (!AnnotationClipboard.Set(item))
        {
            ShowTemporaryHint(Strings.Common_CopyFailedBusy);
        }
    }

    private void CutAnnotation(Annotation item)
    {
        if (AnnotationClipboard.Set(item))
        {
            SetSelected(item);
            DeleteSelected();
        }
        else
        {
            ShowTemporaryHint(Strings.Editor_CutFailedBusy);
        }
    }

    /// <param name="at">Middelpunt voor de geplakte annotatie; zonder punt een kleine verschuiving.</param>
    private void Paste(Point? at = null)
    {
        if (AnnotationClipboard.Get() is not { } copy)
        {
            PasteImage();
            return;
        }

        Commit(_doc.CaptureState());
        Rect bounds = copy.Bounds;
        Vector offset = at is { } point
            ? point - new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2)
            : new Vector(12 / Surface.ViewScale, 12 / Surface.ViewScale);
        copy.Move(offset);
        _doc.Items.Add(copy);
        SetSelected(copy);
    }

    private static bool CanPaste()
    {
        if (AnnotationClipboard.Get() != null)
        {
            return true;
        }

        try
        {
            return Clipboard.ContainsImage() || Clipboard.ContainsData("PNG") || Clipboard.ContainsFileDropList();
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            return false;
        }
    }

    private void ToggleToolbars()
    {
        _toolbarsHidden = !_toolbarsHidden;
        Visibility visibility = _toolbarsHidden ? Visibility.Collapsed : Visibility.Visible;
        Toolbar.Visibility = visibility;
        PropsBar.Visibility = visibility;
        ActionBar.Visibility = IsSplitLayout ? visibility : Visibility.Collapsed;
        ApplyCaptureBarPosition();
        if (_toolbarsHidden)
        {
            ShowTemporaryHint(Strings.Editor_ToolbarsHidden);
        }
    }

    private void Duplicate(Annotation item)
    {
        Commit(_doc.CaptureState());
        Annotation copy = item.Clone();
        double offset = 12 / Surface.ViewScale;
        copy.Move(new Vector(offset, offset));
        _doc.Items.Add(copy);
        SetSelected(copy);
    }

    private void Nudge(Vector delta)
    {
        if (_selected == null)
        {
            return;
        }

        bool coalesce = _lastStyleTarget == _selected && _lastStyleProp == "nudge"
                        && DateTime.Now - _lastStyleTime < TimeSpan.FromSeconds(1.5);
        if (!coalesce)
        {
            Commit(_doc.CaptureState());
        }

        _lastStyleTarget = _selected;
        _lastStyleProp = "nudge";
        _lastStyleTime = DateTime.Now;
        _selected.Move(delta);
        Surface.InvalidateVisual();
    }

    private void Undo()
    {
        CommitTextEdit();
        CancelDrag();
        DocumentState? state = _undo.Undo(_doc.CaptureState());
        if (state != null)
        {
            RestoreState(state);
        }
    }

    private void Redo()
    {
        CommitTextEdit();
        CancelDrag();
        DocumentState? state = _undo.Redo(_doc.CaptureState());
        if (state != null)
        {
            RestoreState(state);
        }
    }

    private void RestoreState(DocumentState state)
    {
        bool sizeChanged = state.Image.PixelWidth != _doc.Width || state.Image.PixelHeight != _doc.Height;
        _doc.Restore(state);
        SetDirty(true);
        SetSelected(null);
        if (sizeChanged)
        {
            OnDocumentSizeChanged();
        }
    }

    private void ApplyCrop()
    {
        Rect? crop = Surface.CropRect;
        Surface.CropRect = null;
        if (crop is not Rect r)
        {
            return;
        }

        var area = new Int32Rect((int)Math.Round(r.X), (int)Math.Round(r.Y), (int)Math.Round(r.Width), (int)Math.Round(r.Height));
        area.Width = Math.Min(area.Width, _doc.Width - area.X);
        area.Height = Math.Min(area.Height, _doc.Height - area.Y);
        if (area.Width < 4 || area.Height < 4)
        {
            return;
        }

        Commit(_pending);
        _doc.Crop(area);
        SetSelected(null);
        OnDocumentSizeChanged();
        ShowTemporaryHint(Strings.Editor_Cropped);
    }

    private Rect ClampToImage(Rect r)
    {
        r.Intersect(new Rect(0, 0, _doc.Width, _doc.Height));
        return r.IsEmpty ? new Rect(_dragStart, _dragStart) : r;
    }

    private void OnDocumentSizeChanged()
    {
        Surface.InvalidateMeasure();
        if (_fitMode)
        {
            Dispatcher.BeginInvoke(ZoomToFit, DispatcherPriority.Loaded);
        }

        UpdateInfo();
    }

    // ---------------------------------------------------------------- Tekst bewerken

    private void BeginTextEdit(TextAnnotation text, bool isNew, DocumentState? stateBefore)
    {
        _editingText = text;
        _textIsNew = isNew;
        _textPending = stateBefore;
        _textOriginal = text.Text;
        text.IsHidden = true;
        SetSelected(text);

        var editor = new TextBox
        {
            Text = text.Text,
            AcceptsReturn = true,
            AcceptsTab = false,
            FontFamily = new FontFamily("Segoe UI")
        };
        ApplyTextEditorStyle(editor, text);
        editor.TextChanged += (_, _) =>
        {
            text.Text = editor.Text;
            ApplyTextEditorStyle(editor, text);
            Surface.InvalidateVisual();
        };
        editor.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape || (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)))
            {
                CommitTextEdit();
                Surface.Focus();
                e.Handled = true;
            }
        };
        editor.LostKeyboardFocus += (_, e) =>
        {
            // Naar een ander venster wisselen: blijven bewerken, WPF geeft de focus terug bij terugkeer.
            if (e.NewFocus == null || Window.GetWindow(this) is not { IsActive: true })
            {
                return;
            }

            // Klikken in de eigenschappenbalk (kleur, lettergrootte) mag het bewerken niet afbreken.
            if (e.NewFocus is DependencyObject target && IsInside(target, PropsPanel))
            {
                return;
            }

            if (e.NewFocus is Popup || (e.NewFocus is DependencyObject d && IsInsidePopup(d)))
            {
                return;
            }

            CommitTextEdit();
        };

        TextLayer.Children.Add(editor);
        _textEditor = editor;
        Dispatcher.BeginInvoke(() =>
        {
            editor.Focus();
            editor.CaretIndex = editor.Text.Length;
            editor.SelectAll();
        }, DispatcherPriority.Input);

        Surface.InvalidateVisual();
        UpdateHint();
    }

    private static bool IsInside(DependencyObject element, DependencyObject container)
    {
        DependencyObject? current = element;
        while (current != null)
        {
            if (ReferenceEquals(current, container))
            {
                return true;
            }

            current = current is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(current) ?? LogicalTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }

        return false;
    }

    private static bool IsInsidePopup(DependencyObject element)
    {
        DependencyObject? current = element;
        while (current != null)
        {
            if (current is Popup || current.GetType().Name == "PopupRoot")
            {
                return true;
            }

            current = current is Visual ? VisualTreeHelper.GetParent(current) ?? LogicalTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }

        return false;
    }

    private static void ApplyTextEditorStyle(TextBox editor, TextAnnotation text)
    {
        StyleSet s = text.Style;
        editor.FontSize = s.FontSize;
        editor.FontWeight = s.Bold ? FontWeights.Bold : FontWeights.Normal;
        editor.TextAlignment = s.TextAlign;
        editor.Foreground = new SolidColorBrush(s.Foreground);
        editor.CaretBrush = new SolidColorBrush(s.Foreground);
        editor.Padding = new Thickness(0);

        // De vorm zelf wordt eronder getekend; zonder rand en vulling toont het tekstvak waar je typt.
        bool shapeVisible = s.TextShape != TextShape.None && (s.Fill.A > 0 || (s.Thickness > 0 && s.Stroke.A > 0));
        double border = shapeVisible ? 0 : 1;
        editor.Background = shapeVisible ? Brushes.Transparent : new SolidColorBrush(Color.FromArgb(60, 255, 255, 255));
        editor.BorderBrush = shapeVisible ? Brushes.Transparent : new SolidColorBrush(Palette.Accent);
        editor.BorderThickness = new Thickness(border);

        // Het tekstvak heeft zelf 2 px marge links en rechts van de tekst.
        Point origin = text.TextOrigin;
        Canvas.SetLeft(editor, origin.X - 2 - border);
        Canvas.SetTop(editor, origin.Y - border);
        if (text.WrapWidth is double wrap)
        {
            editor.TextWrapping = TextWrapping.Wrap;
            editor.MinWidth = 0;
            editor.Width = wrap + 4 + 2 * border;
        }
        else
        {
            editor.TextWrapping = TextWrapping.NoWrap;
            editor.MinWidth = shapeVisible ? 0 : 40;
            editor.Width = double.NaN;
        }
    }

    private void CommitTextEdit()
    {
        if (_textEditor == null || _editingText == null)
        {
            return;
        }

        TextBox editor = _textEditor;
        TextAnnotation text = _editingText;
        _textEditor = null;
        _editingText = null;
        TextLayer.Children.Remove(editor);
        text.IsHidden = false;

        string value = editor.Text.TrimEnd();
        if (string.IsNullOrWhiteSpace(value))
        {
            if (!_textIsNew)
            {
                Commit(_textPending);
            }

            _doc.Items.Remove(text);
            SetSelected(null);
        }
        else
        {
            text.Text = value;
            if (_textIsNew || value != _textOriginal)
            {
                Commit(_textPending);
            }

            SetSelected(text);
        }

        _textPending = null;
        Surface.InvalidateVisual();
        UpdateHint();
    }

    // ---------------------------------------------------------------- Eigenschappen

    private void UpdatePropsPanel()
    {
        ToolKind kind = _selected?.Kind ?? _tool;
        StyleSet style = _selected?.Style ?? StyleFor(_tool);
        StyleProps props = StyleSet.PropsFor(kind);

        SetVisible(ShapeGroup, props.HasFlag(StyleProps.ShapeKind));
        SetVisible(TextShapeGroup, props.HasFlag(StyleProps.TextShape));
        SetVisible(CloudBubblesCombo, style.TextShape == TextShape.Cloud);
        bool frameless = kind == ToolKind.Text && style.TextShape == TextShape.None;
        SetVisible(StrokeGroup, props.HasFlag(StyleProps.Stroke) && !frameless);
        SetVisible(FillGroup, props.HasFlag(StyleProps.Fill) && !frameless && !(kind == ToolKind.Shape && ShapeGeometries.IsOpen(style.ShapeKind)));
        SetVisible(ForegroundGroup, props.HasFlag(StyleProps.Foreground));
        SetVisible(ThicknessGroup, props.HasFlag(StyleProps.Thickness) && !frameless);
        SetVisible(DashGroup, props.HasFlag(StyleProps.Dash) && !frameless);
        SetVisible(CornerGroup, props.HasFlag(StyleProps.Corner) && !frameless);
        SetVisible(FontGroup, props.HasFlag(StyleProps.Font));
        SetVisible(NumberingGroup, props.HasFlag(StyleProps.Numbering));
        SetVisible(CursorGroup, props.HasFlag(StyleProps.CursorKind));
        SetVisible(EmojiGroup, props.HasFlag(StyleProps.Emoji));
        SetVisible(SizeGroup, props.HasFlag(StyleProps.Size));
        SetVisible(BlurGroup, props.HasFlag(StyleProps.Blur));
        bool brushEraser = style.EraserMode == EraserMode.Brush;
        SetVisible(EraserModeGroup, props.HasFlag(StyleProps.EraserMode));
        SetVisible(ToleranceGroup, props.HasFlag(StyleProps.Tolerance) && !(kind == ToolKind.Eraser && brushEraser));
        if (kind == ToolKind.Eraser && !brushEraser)
        {
            SetVisible(ThicknessGroup, false);
        }
        SetVisible(StraightGroup, props.HasFlag(StyleProps.Straight));
        SetVisible(ShadowGroup, props.HasFlag(StyleProps.Shadow));

        NoPropsText.Text = kind switch
        {
            ToolKind.Crop => Strings.Editor_CropProps,
            ToolKind.Select => Strings.Editor_SelectProps,
            _ => string.Empty
        };
        SetVisible(NoPropsText, props == StyleProps.None);
        UpdateBackgroundGroup(kind == ToolKind.Select && _selected == null);

        bool strokeIsLine = kind is ToolKind.Line or ToolKind.Arrow or ToolKind.Highlight;
        StrokePicker.ShowAsOutline = !strokeIsLine;
        StrokePicker.ToolTip = strokeIsLine ? Strings.Common_Color : Strings.Prop_StrokeColor;
        FillPicker.ToolTip = kind switch
        {
            ToolKind.Text => Strings.Prop_BackgroundColor,
            ToolKind.Step => Strings.Common_Color,
            _ => Strings.Prop_FillColor
        };
        ThicknessCombo.ToolTip = kind switch
        {
            ToolKind.Text => Strings.Prop_BorderThickness,
            ToolKind.Eraser => Strings.Prop_EraserSize,
            _ => Strings.Prop_Thickness
        };

        _suppressStyleEvents = true;
        try
        {
            StrokePicker.SelectedColor = style.Stroke;
            FillPicker.SelectedColor = style.Fill;
            ForegroundPicker.SelectedColor = style.Foreground;
            double[] thicknesses = kind == ToolKind.Eraser ? EraserThicknessValues : ThicknessValues;
            if (ThicknessCombo.ItemsSource != thicknesses)
            {
                ThicknessCombo.ItemsSource = thicknesses;
            }

            ThicknessCombo.SelectedItem = Nearest(thicknesses, style.Thickness);
            FontSizeCombo.SelectedItem = Nearest(FontSizeValues, style.FontSize);
            double[] sizes = kind == ToolKind.Emoji ? EmojiSizeValues : SizeValues;
            if (SizeCombo.ItemsSource != sizes)
            {
                SizeCombo.ItemsSource = sizes;
            }

            SizeCombo.SelectedItem = Nearest(sizes, style.Size);
            EmojiPicker.Emoji = style.Emoji;
            EmojiPicker.SkinTone = style.SkinTone;
            DashCombo.SelectedItem = style.Dash;
            CornerCombo.SelectedItem = Nearest(CornerValues, style.CornerRadius);
            NumberingCombo.SelectedItem = style.StepNumbering;
            CursorKindCombo.SelectedItem = style.CursorKind;
            TextShapeCombo.SelectedItem = style.TextShape;
            ShapeKindCombo.SelectedItem = style.ShapeKind;
            CloudBubblesCombo.SelectedItem = CloudBubblesValues.OrderBy(v => Math.Abs(v - style.CloudBubbles)).First();
            BlurModeCombo.SelectedItem = style.BlurMode;
            BlurStrengthSlider.Value = style.BlurStrength;
            EraserSmartToggle.IsChecked = !brushEraser;
            EraserBrushToggle.IsChecked = brushEraser;
            ToleranceSlider.Value = style.Tolerance;
            ToleranceText.Text = style.Tolerance.ToString();
            BoldToggle.IsChecked = style.Bold;
            SyncTextAlignToggles(style.TextAlign);
            ShadowCheck.IsChecked = style.Shadow;
            StraightCheck.IsChecked = _settings.StraightHighlight;
        }
        finally
        {
            _suppressStyleEvents = false;
        }
    }

    /// <summary>Achtergrondkeuze alleen tonen als er iets van te zien is: waar het knipsel transparant is.</summary>
    private void UpdateBackgroundGroup(bool canvasProps)
    {
        bool show = canvasProps && _doc.HasTransparency;
        SetVisible(BackgroundGroup, show);
        if (!show)
        {
            return;
        }

        CanvasBackground background = _doc.Background;
        BackgroundKind kind = background.Kind;
        SetVisible(BackgroundColorPicker, kind is BackgroundKind.Color or BackgroundKind.Gradient);
        SetVisible(BackgroundColor2Picker, kind == BackgroundKind.Gradient);
        SetVisible(GradientDial, kind == BackgroundKind.Gradient);
        SetVisible(BackgroundImageButton, kind == BackgroundKind.Image);
        BackgroundColorPicker.ToolTip = kind == BackgroundKind.Gradient ? Strings.Background_FirstColor : Strings.Common_Color;
        BackgroundImageThumb.Source = background.Image;
        SetVisible(BackgroundImageThumb, background.Image != null);
        SetVisible(BackgroundImageIcon, background.Image == null);

        bool suppress = _suppressStyleEvents;
        _suppressStyleEvents = true;
        try
        {
            BackgroundKindCombo.SelectedItem = kind;
            GradientDial.Angle = background.Angle;
            GradientDial.IsRadial = background.Direction == GradientDirection.Radial;
            BackgroundColorPicker.SelectedColor = background.Color;
            BackgroundColor2Picker.SelectedColor = background.Color2;
        }
        finally
        {
            _suppressStyleEvents = suppress;
        }
    }

    private void ChangeBackground(Func<CanvasBackground, CanvasBackground> change)
    {
        if (_suppressStyleEvents)
        {
            return;
        }

        CanvasBackground updated = change(_doc.Background);
        if (updated == _doc.Background)
        {
            return;
        }

        Commit(_doc.CaptureState());
        _doc.Background = updated;
        SetDirty(true);
        UpdateBackgroundGroup(true);
        Surface.InvalidateVisual();
    }

    private void BackgroundKind_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressStyleEvents || BackgroundKindCombo.SelectedItem is not BackgroundKind kind)
        {
            return;
        }

        if (kind == BackgroundKind.Image && _doc.Background.Image == null)
        {
            if (PickBackgroundImage() is not { } image)
            {
                UpdateBackgroundGroup(true);
                return;
            }

            ChangeBackground(b => b with { Kind = kind, Image = image });
            return;
        }

        ChangeBackground(b => b with { Kind = kind });
    }

    /// <summary>Live voorbeeld tijdens het slepen van de draaiknop; <see cref="CommitGradient"/> maakt er één stap van.</summary>
    private void PreviewGradientAngle(double angle)
    {
        _backgroundBeforePreview ??= _doc.Background;
        _doc.Background = _doc.Background with { Direction = GradientDirection.Linear, Angle = angle };
        Surface.InvalidateVisual();
    }

    private void CommitGradient(Func<CanvasBackground, CanvasBackground> change)
    {
        if (_backgroundBeforePreview is { } before)
        {
            _backgroundBeforePreview = null;
            CanvasBackground updated = change(_doc.Background);
            _doc.Background = before;
            ChangeBackground(_ => updated);
            return;
        }

        ChangeBackground(change);
    }

    private void BackgroundImage_Click(object sender, RoutedEventArgs e)
    {
        if (PickBackgroundImage() is { } image)
        {
            ChangeBackground(b => b with { Kind = BackgroundKind.Image, Image = image });
        }
    }

    private BitmapSource? PickBackgroundImage()
    {
        if (App.Instance.PickImageFile(Window.GetWindow(this)) is not { } path)
        {
            return null;
        }

        try
        {
            BitmapSource image = PixelUtil.LoadFile(path);
            return PixelUtil.CreateBitmap(PixelUtil.ToBgra(image), image.PixelWidth, image.PixelHeight);
        }
        catch (Exception ex)
        {
            ShowMessage(string.Format(Strings.Common_CannotOpenFile, System.IO.Path.GetFileName(path), ex.Message), MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }
    }

    private static void SetVisible(UIElement element, bool visible) =>
        element.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    private static double Nearest(double[] values, double value) =>
        values.OrderBy(v => Math.Abs(v - value)).First();

    /// <summary>
    /// Past een stijl toe op het geselecteerde object (met undo) en onthoudt hem als standaard voor dat gereedschap.
    /// Snel opeenvolgende wijzigingen van dezelfde eigenschap (slider) vormen samen één undo-stap.
    /// </summary>
    private void ChangeStyle(string prop, Action<StyleSet> apply)
    {
        if (_suppressStyleEvents)
        {
            return;
        }

        if (_selected != null)
        {
            bool editingNewText = _editingText != null && _textIsNew;
            bool coalesce = _lastStyleTarget == _selected && _lastStyleProp == prop
                            && DateTime.Now - _lastStyleTime < TimeSpan.FromSeconds(1.5);
            if (!coalesce && !editingNewText)
            {
                Commit(_doc.CaptureState());
            }

            _lastStyleTarget = _selected;
            _lastStyleProp = prop;
            _lastStyleTime = DateTime.Now;
            apply(_selected.Style);
            apply(StyleFor(_selected.Kind));
            SetDirty(true);

            if (_textEditor != null && _editingText != null)
            {
                ApplyTextEditorStyle(_textEditor, _editingText);
            }
        }
        else
        {
            apply(StyleFor(_tool));
        }

        Surface.InvalidateVisual();
    }

    private void Thickness_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (ThicknessCombo.SelectedItem is double value)
        {
            ChangeStyle("thickness", s => s.Thickness = value);
        }
    }

    private void FontSize_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (FontSizeCombo.SelectedItem is double value)
        {
            ChangeStyle("font", s => s.FontSize = value);
        }
    }

    private void Size_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (SizeCombo.SelectedItem is double value)
        {
            ChangeStyle("size", s => s.Size = value);
        }
    }

    private void Dash_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (DashCombo.SelectedItem is LineDash dash)
        {
            ChangeStyle("dash", s => s.Dash = dash);
        }
    }

    private void Corner_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (CornerCombo.SelectedItem is double radius)
        {
            ChangeStyle("corner", s => s.CornerRadius = radius);
        }
    }

    private void Numbering_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (NumberingCombo.SelectedItem is StepNumbering numbering)
        {
            ChangeStyle("numbering", s => s.StepNumbering = numbering);
        }
    }

    private void TextShape_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (TextShapeCombo.SelectedItem is TextShape shape)
        {
            ChangeStyle("textshape", s => s.TextShape = shape);
            if (!_suppressStyleEvents)
            {
                UpdatePropsPanel();
            }
        }
    }

    private void ShapeKind_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (ShapeKindCombo.SelectedItem is ShapeKind kind && !_suppressStyleEvents)
        {
            ChangeStyle("shape", s => s.ShapeKind = kind);
            UpdateShapeToolIcon();
            SetVisible(FillGroup, !ShapeGeometries.IsOpen(kind));
        }
    }

    private void UpdateShapeToolIcon() => ShapeToolIcon.Data = ShapeGeometries.For(StyleFor(ToolKind.Shape).ShapeKind);

    private void CloudBubbles_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (CloudBubblesCombo.SelectedItem is int count)
        {
            ChangeStyle("cloudbubbles", s => s.CloudBubbles = count);
        }
    }

    private void CursorKind_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (CursorKindCombo.SelectedItem is CursorKind kind)
        {
            ChangeStyle("cursor", s => s.CursorKind = kind);
        }
    }

    private void BlurMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (BlurModeCombo.SelectedItem is BlurMode mode)
        {
            ChangeStyle("blurmode", s => s.BlurMode = mode);
        }
    }

    private void BlurStrength_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        int strength = (int)Math.Round(e.NewValue);
        ChangeStyle("blurstrength", s => s.BlurStrength = strength);
    }

    private void EraserMode_Click(object sender, RoutedEventArgs e)
    {
        EraserMode mode = sender == EraserBrushToggle ? EraserMode.Brush : EraserMode.Smart;
        ChangeStyle("erasermode", s => s.EraserMode = mode);
        Surface.Cursor = CursorForTool(_tool);
        UpdateBrushPreview(null);
        Surface.InvalidateVisual();
        UpdatePropsPanel();
        UpdateHint();
    }

    private void Tolerance_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        int tolerance = (int)Math.Round(e.NewValue);
        if (ToleranceText != null)
        {
            ToleranceText.Text = tolerance.ToString();
        }

        ChangeStyle("tolerance", s => s.Tolerance = tolerance);
    }

    private void Bold_Changed(object sender, RoutedEventArgs e)
    {
        bool bold = BoldToggle.IsChecked == true;
        ChangeStyle("bold", s => s.Bold = bold);
    }

    private void TextAlign_Click(object sender, RoutedEventArgs e)
    {
        TextAlignment align = sender == TextAlignCenterToggle ? TextAlignment.Center
            : sender == TextAlignRightToggle ? TextAlignment.Right
            : TextAlignment.Left;
        SyncTextAlignToggles(align);
        ChangeStyle("textalign", s => s.TextAlign = align);
    }

    private void SyncTextAlignToggles(TextAlignment align)
    {
        TextAlignLeftToggle.IsChecked = align == TextAlignment.Left;
        TextAlignCenterToggle.IsChecked = align == TextAlignment.Center;
        TextAlignRightToggle.IsChecked = align == TextAlignment.Right;
    }

    private void Shadow_Changed(object sender, RoutedEventArgs e)
    {
        bool shadow = ShadowCheck.IsChecked == true;
        ChangeStyle("shadow", s => s.Shadow = shadow);
    }

    private void Straight_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressStyleEvents)
        {
            return;
        }

        _settings.StraightHighlight = StraightCheck.IsChecked == true;
        UpdateHint();
    }

    // ---------------------------------------------------------------- Zoom

    private double DpiScale => VisualTreeHelper.GetDpi(this).DpiScaleX;

    private void SetZoom(double zoom, bool fit = false)
    {
        _fitMode = fit;
        _zoom = Math.Clamp(zoom, 0.05, 8);
        ApplyZoom();
    }

    private void ApplyZoom()
    {
        double scale = _zoom / DpiScale;
        ZoomTransform.ScaleX = scale;
        ZoomTransform.ScaleY = scale;
        Surface.ViewScale = scale;
        RenderOptions.SetBitmapScalingMode(Surface, _zoom >= 1 ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);
        ZoomText.Text = $"{Math.Round(_zoom * 100)}%";
        Surface.InvalidateVisual();
        UpdateInfo();
    }

    private void ZoomToFit()
    {
        double availableWidth = Scroller.ActualWidth - 64;
        double availableHeight = Scroller.ActualHeight - 64;
        if (availableWidth <= 0 || availableHeight <= 0 || _doc.Width == 0 || _doc.Height == 0)
        {
            return;
        }

        double fit = Math.Min(availableWidth / _doc.Width, availableHeight / _doc.Height) * DpiScale;
        SetZoom(Math.Min(1, fit), fit: true);
    }

    private void ZoomBy(double factor) => SetZoom(_zoom * factor);

    private void Scroller_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            return;
        }

        ZoomBy(e.Delta > 0 ? 1.15 : 1 / 1.15);
        e.Handled = true;
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e) => ZoomBy(1.25);

    private void ZoomOut_Click(object sender, RoutedEventArgs e) => ZoomBy(1 / 1.25);

    private void ZoomReset_Click(object sender, RoutedEventArgs e)
    {
        if (_fitMode || Math.Abs(_zoom - 1) > 0.001)
        {
            SetZoom(1);
        }
        else
        {
            ZoomToFit();
        }
    }

    // ---------------------------------------------------------------- Export

    private bool CopiesOnSave => _settings.SaveButton == SaveButtonChoice.SaveAndCopy;

    private void ApplySaveButton()
    {
        bool copy = CopiesOnSave;
        string label = copy ? Strings.Settings_SaveAndCopy : Strings.Common_Save;
        if (SaveLabel.Text == label)
        {
            return;
        }

        SaveLabel.Text = label;
        ToolbarPanel.SetTitle(SaveButton, label);
        SaveButton.ToolTip = copy
            ? Strings.Tool_SaveAndCopyTip
            : Strings.Tool_SaveTip;
        UpdateToolbarLayout();
    }

    /// <summary>
    /// Werkbalk, linialen en knoppen opnieuw afstemmen op de gedeelde instellingen, die in een ander tabblad
    /// of in Instellingen gewijzigd kunnen zijn. De schil roept dit aan bij het activeren van een tabblad of het venster.
    /// </summary>
    public void RefreshFromSettings()
    {
        foreach (UIElement item in _toolbarItems.Values)
        {
            item.Visibility = _settings.ToolbarHidden.Contains(ItemId(item)) ? Visibility.Collapsed : Visibility.Visible;
        }

        ApplyUploadButton();
        ApplyToolbarLayout();
        ApplySaveButton();
        if (ToolbarItems.IsLocked != _settings.ToolbarLocked)
        {
            ApplyToolbarLock();
        }

        if (RulersToggle.IsChecked != _settings.ShowRulers)
        {
            ApplyRulers();
        }
    }

    /// <summary>Het venster staat op een scherm met een andere schaal.</summary>
    public void OnHostDpiChanged() => ApplyZoom();

    private bool CanUpload => _settings.Uploader is { IsConfigured: true };

    private void ApplyUploadButton()
    {
        Visibility visibility = CanUpload && !_settings.ToolbarHidden.Contains(ItemId(UploadButton))
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (UploadButton.Visibility != visibility)
        {
            UploadButton.Visibility = visibility;
            UpdateToolbarLayout();
        }
    }

    private async void Upload()
    {
        if (_settings.Uploader is not { IsConfigured: true } config || !UploadButton.IsEnabled)
        {
            return;
        }

        CommitTextEdit();
        BitmapSource image = DocumentRenderer.Export(_doc);
        UploadButton.IsEnabled = false;
        ShowTemporaryHint(string.Format(Strings.Editor_Uploading, config.DisplayName));
        try
        {
            using System.IO.MemoryStream png = ImageExporter.EncodePng(image);
            string url = await ZiplineUploader.UploadAsync(config, png, DefaultName + ".png", "image/png");
            Kniip.History.HistoryStore.Add(Kniip.History.HistoryKind.Snip, null, url, image);
            SetDirty(false);
            _exported = true;
            ShowTemporaryHint(ClipboardWriter.SetText(url)
                ? string.Format(Strings.Editor_UploadedCopied, url)
                : string.Format(Strings.Editor_UploadedCopyFailed, url));
        }
        catch (Exception ex)
        {
            ShowTemporaryHint(Strings.Editor_UploadFailedShort);
            ShowMessage(string.Format(Strings.Common_UploadFailed, ex.Message), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            UploadButton.IsEnabled = true;
        }
    }

    private void Upload_Click(object sender, RoutedEventArgs e) => Upload();

    private void CopyToClipboard()
    {
        CommitTextEdit();
        BitmapSource image = DocumentRenderer.Export(_doc);
        if (ClipboardWriter.SetImage(image))
        {
            SetDirty(false);
            _exported = true;
            ShowTemporaryHint(Strings.Common_CopiedToClipboard);
        }
        else
        {
            ShowTemporaryHint(Strings.Common_CopyFailedBusy);
        }
    }

    /// <summary>Tekst van het geopende knipsel herkennen (Ctrl+Shift+T).</summary>
    private async void RecognizeText()
    {
        if (_recognizing)
        {
            return;
        }

        CommitTextEdit();
        BitmapSource image = DocumentRenderer.Export(_doc);
        _recognizing = true;
        ShowTemporaryHint(Strings.Ocr_Recognizing);
        try
        {
            await OcrResultWindow.Open(image, Window.GetWindow(this)).Completion;
        }
        finally
        {
            _recognizing = false;
        }
    }

    /// <summary>Editor wegzetten en een rechthoek op het scherm laten kiezen; daarna komt de editor terug met het tekstvenster erboven.</summary>
    private void Ocr_Click(object sender, RoutedEventArgs e)
    {
        CommitTextEdit();
        CancelDrag();
        Action? restore = OwnerShell?.Minimize();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            App.Instance.BeginTextSnip(restore);
        };
        timer.Start();
    }

    private static string DefaultName => string.Format(Strings.Snip_DefaultName, DateTime.Now);

    private bool Save() => Save(CopiesOnSave);

    private bool Save(bool copy)
    {
        if (_settings.SnipSaveFolder is not { Length: > 0 } target)
        {
            return SaveAs(copy);
        }

        CommitTextEdit();
        string path;
        try
        {
            path = FileNameTemplate.Resolve(target, _settings.SnipFileNameTemplate, _windowName ?? Strings.Snip_Name, DefaultName, ".png");
        }
        catch (Exception ex)
        {
            ShowMessage(string.Format(Strings.Editor_SaveFolderUnusable, target, ex.Message),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return SaveAs(copy);
        }

        return WriteTo(path, copy);
    }

    private bool SaveAs() => SaveAs(CopiesOnSave);

    private bool SaveAs(bool copy)
    {
        CommitTextEdit();
        string folder = _settings.LastSaveFolder is { } last && System.IO.Directory.Exists(last)
            ? last
            : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = Strings.Editor_SaveDialogTitle,
            Filter = Strings.Editor_SaveDialogFilter,
            FileName = FileNameTemplate.SuggestedName(_settings.SnipFileNameTemplate, _windowName ?? Strings.Snip_Name, DefaultName),
            DefaultExt = ".png",
            InitialDirectory = folder,
            AddExtension = true
        };
        Window? owner = Window.GetWindow(this);
        if ((owner == null ? dialog.ShowDialog() : dialog.ShowDialog(owner)) != true)
        {
            return false;
        }

        if (!WriteTo(dialog.FileName, copy))
        {
            return false;
        }

        _settings.LastSaveFolder = System.IO.Path.GetDirectoryName(dialog.FileName);
        return true;
    }

    private bool WriteTo(string path, bool copy)
    {
        try
        {
            App.Instance.Capture.IgnorePath(path);
            BitmapSource image = DocumentRenderer.Export(_doc);
            ImageExporter.Save(image, path);
            Kniip.History.HistoryStore.Add(Kniip.History.HistoryKind.Snip, path, null, image);
            SetDirty(false);
            _exported = true;
            string name = System.IO.Path.GetFileName(path);
            if (copy)
            {
                ShowTemporaryHint(ClipboardWriter.SetImage(image, path)
                    ? string.Format(Strings.Editor_SavedCopied, name)
                    : string.Format(Strings.Editor_SavedCopyFailed, name));
            }
            else
            {
                ShowTemporaryHint(string.Format(Strings.Editor_Saved, name));
            }

            return true;
        }
        catch (Exception ex)
        {
            ShowMessage(string.Format(Strings.Common_SaveFailed, ex.Message), MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    private void Copy_Click(object sender, RoutedEventArgs e) => CopyToClipboard();

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            SaveAs();
        }
        else
        {
            Save();
        }
    }

    private void SaveMenu_MouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (!SaveButton.IsEnabled)
        {
            return;
        }

        bool copy = CopiesOnSave;
        string ellipsis = string.IsNullOrEmpty(_settings.SnipSaveFolder) ? "..." : string.Empty;
        var menu = new ContextMenu { PlacementTarget = SaveButton, Placement = PlacementMode.Bottom };
        menu.Items.Add(copy
            ? MenuItem(Strings.Common_Save + ellipsis, "\uE74E", "", () => Save(false))
            : MenuItem(Strings.Settings_SaveAndCopy + ellipsis, "\uE74E", "", () => Save(true)));
        menu.Items.Add(MenuItem(Strings.Common_SaveAs, "\uE792", "Ctrl+Shift+S", () => SaveAs()));
        menu.IsOpen = true;
    }

    private void Folders_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = FoldersButton, Placement = PlacementMode.Bottom };
        menu.Items.Add(MenuItem(Strings.Menu_OpenScreenshotFolder, "\uE91B", "", () => Kniip.History.HistoryActions.OpenFolder(App.Instance.SnipFolder)));
        menu.Items.Add(MenuItem(Strings.Menu_OpenScreencastFolder, "\uE714", "", () => Kniip.History.HistoryActions.OpenFolder(App.Instance.VideoFolder)));
        menu.IsOpen = true;
    }

    private void Print_Click(object sender, RoutedEventArgs e) => Print();

    private void Print()
    {
        CommitTextEdit();
        var dialog = new PrintDialog();
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            BitmapSource image = DocumentRenderer.Export(_doc);
            double pageWidth = dialog.PrintableAreaWidth;
            double pageHeight = dialog.PrintableAreaHeight;
            double scale = Math.Min(1, Math.Min(pageWidth / image.Width, pageHeight / image.Height));
            double width = image.Width * scale;
            double height = image.Height * scale;

            var visual = new DrawingVisual();
            RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
            using (DrawingContext dc = visual.RenderOpen())
            {
                dc.DrawImage(image, new Rect((pageWidth - width) / 2, (pageHeight - height) / 2, width, height));
            }

            dialog.PrintVisual(visual, Strings.Editor_PrintJobName);
            ShowTemporaryHint(Strings.Editor_Printed);
        }
        catch (Exception ex)
        {
            ShowMessage(string.Format(Strings.Editor_PrintFailed, ex.Message), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Undo_Click(object sender, RoutedEventArgs e) => Undo();

    private void Redo_Click(object sender, RoutedEventArgs e) => Redo();

    private void Delete_Click(object sender, RoutedEventArgs e) => DeleteSelected();

    private void Settings_Click(object sender, RoutedEventArgs e) => App.Instance.ShowSettings();

    private void OnUpdateAvailabilityChanged(object? sender, EventArgs e) => ShowUpdateBadge();

    private void ShowUpdateBadge()
    {
        bool available = App.Instance.Updates.IsUpdateAvailable;
        SettingsBadge.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
        SettingsButton.ToolTip = available ? Strings.Editor_SettingsUpdateTip : Strings.Common_Settings;
    }

    private void NewSnip_Click(object sender, RoutedEventArgs e) => StartNewSnip();

    private void Clear_Click(object sender, RoutedEventArgs e) => DiscardAndStartBlank();

    /// <summary>Dit tabblad vervangen door een leeg canvas.</summary>
    private void DiscardAndStartBlank()
    {
        if (OwnerShell is not { } shell
            || !ConfirmDiscard(Strings.Editor_ConfirmClear))
        {
            return;
        }

        App.Instance.IgnoreCurrentClipboard();
        shell.ReplaceTab(this, App.Instance.CreateBlankEditor());
    }

    /// <summary>Editor wegzetten (aantekeningen blijven bewaard) zodat hij niet in de opname staat.</summary>
    private void Screencast_Click(object sender, RoutedEventArgs e)
    {
        CommitTextEdit();
        CancelDrag();
        OwnerShell?.Minimize();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            App.Instance.StartRecording();
        };
        timer.Start();
    }

    /// <summary>Editor wegzetten en een nieuw knipsel laten maken; dat komt in een nieuw tabblad.</summary>
    private void StartNewSnip()
    {
        CommitTextEdit();
        CancelDrag();
        App.Instance.StartNewSnip(OwnerShell?.Minimize());
    }

    private void PasteImage()
    {
        BitmapSource? image = ClipboardImageReader.TryRead(includeFiles: true);
        if (image == null)
        {
            ShowTemporaryHint(Strings.Editor_NoClipboardImage);
            return;
        }

        OpenInNewTab(image);
    }

    private void View_PreviewDragOver(object sender, DragEventArgs e)
    {
        if (!ClipboardImageReader.CanDrop(e.Data))
        {
            return;
        }

        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void View_PreviewDrop(object sender, DragEventArgs e)
    {
        if (!ClipboardImageReader.CanDrop(e.Data))
        {
            return;
        }

        e.Handled = true;
        BitmapSource? image = ClipboardImageReader.FromDrop(e.Data);
        if (image == null)
        {
            ShowTemporaryHint(Strings.Editor_DropFailed);
            return;
        }

        // Pas na het slepen openen: een dialoog tijdens Drop blokkeert Verkenner.
        Dispatcher.BeginInvoke(() => OpenInNewTab(image));
    }

    private void OpenImageFile()
    {
        CommitTextEdit();
        CancelDrag();
        if (App.Instance.PickImageFile(Window.GetWindow(this)) is not { } path)
        {
            return;
        }

        BitmapSource image;
        try
        {
            image = PixelUtil.LoadFile(path);
        }
        catch (Exception ex)
        {
            ShowMessage(string.Format(Strings.Common_CannotOpenFile, System.IO.Path.GetFileName(path), ex.Message), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        OpenInNewTab(image);
    }

    /// <summary>Afbeelding in een eigen tabblad openen; dit knipsel blijft ernaast staan.</summary>
    private void OpenInNewTab(BitmapSource image)
    {
        CommitTextEdit();
        CancelDrag();
        App.Instance.OpenEditor(image);
    }

    private bool ConfirmDiscard(string question)
    {
        CommitTextEdit();
        CancelDrag();
        if (!_dirty || _doc.Items.Count == 0)
        {
            return true;
        }

        return ShowMessage(string.Format(Strings.Editor_DiscardWarning, question),
            MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK;
    }

    /// <summary>
    /// Voorbereiden op het sluiten van dit tabblad. Bij aantekeningen die nog niet gekopieerd of opgeslagen zijn,
    /// wordt eerst gevraagd of het resultaat naar het klembord moet.
    /// </summary>
    /// <param name="force">Zonder vragen sluiten; het knipsel is bewust weggegooid (Wissen).</param>
    /// <param name="confirm">False om niet te vragen, zoals bij het sluiten van één tabblad.</param>
    /// <returns>False als de gebruiker annuleert; het tabblad blijft dan open.</returns>
    public bool TryClose(bool force = false, bool confirm = true)
    {
        CommitTextEdit();
        CancelDrag();
        if (!force && confirm && NeedsCloseConfirmation)
        {
            MessageBoxResult answer = ShowMessage(
                Strings.Editor_CloseUnsaved,
                MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Cancel)
            {
                return false;
            }

            if (answer == MessageBoxResult.Yes)
            {
                CopyToClipboard();
            }
        }

        // Een gekopieerd of opgeslagen knipsel is af; bij de volgende start niet opnieuw van het klembord openen.
        if (!force && _exported)
        {
            App.Instance.IgnoreCurrentClipboard();
        }

        SettingsStore.Save(_settings);
        App.Instance.Updates.AvailabilityChanged -= OnUpdateAvailabilityChanged;
        _hintResetTimer.Stop();
        return true;
    }

    // ---------------------------------------------------------------- Toetsenbord

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if ((_textEditor != null && _textEditor.IsKeyboardFocusWithin) || EmojiPicker.IsOpen)
        {
            return;
        }

        ModifierKeys mods = Keyboard.Modifiers;
        bool ctrl = mods.HasFlag(ModifierKeys.Control);
        bool shift = mods.HasFlag(ModifierKeys.Shift);
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (ctrl)
        {
            switch (key)
            {
                case Key.Z when shift:
                case Key.Y:
                    Redo();
                    break;
                case Key.Z:
                    Undo();
                    break;
                case Key.C when shift:
                    if (_selected != null)
                    {
                        CopyAnnotation(_selected);
                    }

                    break;
                case Key.C:
                    CopyToClipboard();
                    break;
                case Key.X when _selected != null:
                    CutAnnotation(_selected);
                    break;
                case Key.F when shift:
                    Flatten();
                    break;
                case Key.H:
                    ToggleToolbars();
                    break;
                case Key.R:
                    ToggleRulers();
                    break;
                case Key.S when shift:
                    SaveAs();
                    break;
                case Key.S:
                    Save();
                    break;
                case Key.P:
                    Print();
                    break;
                case Key.U when CanUpload:
                    Upload();
                    break;
                case Key.T when shift:
                    RecognizeText();
                    break;
                case Key.N:
                    StartNewSnip();
                    break;
                case Key.Delete:
                    DiscardAndStartBlank();
                    break;
                case Key.V:
                    Paste();
                    break;
                case Key.O:
                    OpenImageFile();
                    break;
                case Key.D when _selected != null:
                    Duplicate(_selected);
                    break;
                case Key.D0:
                case Key.NumPad0:
                    ZoomToFit();
                    break;
                case Key.D1:
                case Key.NumPad1:
                    SetZoom(1);
                    break;
                case Key.OemPlus:
                case Key.Add:
                    ZoomBy(1.25);
                    break;
                case Key.OemMinus:
                case Key.Subtract:
                    ZoomBy(1 / 1.25);
                    break;
                default:
                    return;
            }

            e.Handled = true;
            return;
        }

        if (mods != ModifierKeys.None && mods != ModifierKeys.Shift)
        {
            return;
        }

        double step = shift ? 10 : 1;
        switch (key)
        {
            case Key.Delete when shift:
                DeleteAll();
                break;
            case Key.Delete:
            case Key.Back:
                DeleteSelected();
                break;
            case Key.Home when _selected != null:
                Reorder(_selected, ZOrder.Front);
                break;
            case Key.PageUp when _selected != null:
                Reorder(_selected, ZOrder.Forward);
                break;
            case Key.PageDown when _selected != null:
                Reorder(_selected, ZOrder.Backward);
                break;
            case Key.End when _selected != null:
                Reorder(_selected, ZOrder.Back);
                break;
            case Key.Escape:
                if (_drag != DragMode.None)
                {
                    CancelDrag();
                }
                else
                {
                    SetSelected(null);
                }

                break;
            case Key.Enter when _selected is TextAnnotation text:
                BeginTextEdit(text, false, _doc.CaptureState());
                break;
            case Key.Left when _selected != null:
                Nudge(new Vector(-step, 0));
                break;
            case Key.Right when _selected != null:
                Nudge(new Vector(step, 0));
                break;
            case Key.Up when _selected != null:
                Nudge(new Vector(0, -step));
                break;
            case Key.Down when _selected != null:
                Nudge(new Vector(0, step));
                break;
            case Key.V: SelectTool(ToolKind.Select); break;
            case Key.R: SelectTool(ToolKind.Rectangle); break;
            case Key.E: SelectTool(ToolKind.Ellipse); break;
            case Key.F: SelectTool(ToolKind.Shape); break;
            case Key.L: SelectTool(ToolKind.Line); break;
            case Key.A: SelectTool(ToolKind.Arrow); break;
            case Key.T: SelectTool(ToolKind.Text); break;
            case Key.S: SelectTool(ToolKind.Step); break;
            case Key.C: SelectTool(ToolKind.Cursor); break;
            case Key.J: SelectTool(ToolKind.Emoji); break;
            case Key.H: SelectTool(ToolKind.Highlight); break;
            case Key.B: SelectTool(ToolKind.Blur); break;
            case Key.X: SelectTool(ToolKind.Crop); break;
            case Key.G: SelectTool(ToolKind.Eraser); break;
            default:
                return;
        }

        e.Handled = true;
    }

    // ---------------------------------------------------------------- Status

    private void UpdateCommandStates()
    {
        UndoButton.IsEnabled = _undo.CanUndo;
        RedoButton.IsEnabled = _undo.CanRedo;
        DeleteButton.IsEnabled = _selected != null;

        int index = _selected != null ? _doc.Items.IndexOf(_selected) : -1;
        SetVisible(ArrangeGroup, index >= 0);
        if (index < 0)
        {
            ArrangeToggle.IsChecked = false;
        }

        FlipHButton.IsEnabled = FlipVButton.IsEnabled = _selected?.CanFlip == true;
        RotateLeftButton.IsEnabled = RotateRightButton.IsEnabled = _selected?.CanRotate == true;
        ForwardButton.IsEnabled = index >= 0 && index < _doc.Items.Count - 1;
        BackwardButton.IsEnabled = index > 0;
    }

    private void UpdateInfo()
    {
        InfoText.Text = $"{_doc.Width} × {_doc.Height} px   ·   {Math.Round(_zoom * 100)}%";
    }

    private void ShowTemporaryHint(string message)
    {
        HintText.Text = message;
        _hintResetTimer.Stop();
        _hintResetTimer.Start();
    }

    private void UpdateHint()
    {
        if (_editingText != null)
        {
            HintText.Text = Strings.Hint_TextEditing;
            return;
        }

        if (!_settings.ToolbarLocked)
        {
            HintText.Text = Strings.Hint_ToolbarUnlocked;
            return;
        }

        HintText.Text = _tool switch
        {
            ToolKind.Select => Strings.Hint_Select,
            ToolKind.Rectangle => Strings.Hint_Rectangle,
            ToolKind.Ellipse => Strings.Hint_Ellipse,
            ToolKind.Shape => Strings.Hint_Shape,
            ToolKind.Line => Strings.Hint_Line,
            ToolKind.Arrow => Strings.Hint_Arrow,
            ToolKind.Text => Strings.Hint_Text,
            ToolKind.Step => Strings.Hint_Step,
            ToolKind.Cursor => Strings.Hint_Cursor,
            ToolKind.Emoji => Strings.Hint_Emoji,
            ToolKind.Highlight => _settings.StraightHighlight
                ? Strings.Hint_HighlightStraight
                : Strings.Hint_HighlightFree,
            ToolKind.Blur => Strings.Hint_Blur,
            ToolKind.Crop => Strings.Hint_Crop,
            ToolKind.Eraser when IsBrushEraser => Strings.Hint_EraserBrush,
            ToolKind.Eraser => Strings.Hint_EraserSmart,
            _ => string.Empty
        };
    }
}
