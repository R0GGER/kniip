using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace Kniip.Editor;

/// <summary>
/// Werkbalk die zijn items op één regel zet, met <see cref="ToolbarSpacer"/>s die de overige ruimte opvullen;
/// bij twee spacers komt de groep ertussen in het midden. Past het niet, dan begint elke groep op een nieuwe
/// regel (begin, midden of eind uitgelijnd) en lopen te lange groepen door. Als de werkbalk niet vergrendeld is, kun je de items verslepen om de volgorde te veranderen.
/// </summary>
public sealed class ToolbarPanel : Panel
{
    public static readonly DependencyProperty OrientationProperty = DependencyProperty.Register(
        nameof(Orientation), typeof(Orientation), typeof(ToolbarPanel),
        new FrameworkPropertyMetadata(Orientation.Horizontal, FrameworkPropertyMetadataOptions.AffectsMeasure, OnOrientationChanged));

    public static readonly DependencyProperty IsLockedProperty = DependencyProperty.Register(
        nameof(IsLocked), typeof(bool), typeof(ToolbarPanel), new PropertyMetadata(true, OnIsLockedChanged));

    public static readonly DependencyProperty ItemIdProperty = DependencyProperty.RegisterAttached(
        "ItemId", typeof(string), typeof(ToolbarPanel), new PropertyMetadata(null));

    public static readonly DependencyProperty TitleProperty = DependencyProperty.RegisterAttached(
        "Title", typeof(string), typeof(ToolbarPanel), new PropertyMetadata(null));

    private const double DragThreshold = 4;

    private readonly List<ToolbarLine> _lines = new();
    private readonly HashSet<UIElement> _skipped = new();

    private UIElement? _pressedItem;
    private Point _pressPoint;
    private bool _dragging;
    private int _insertIndex = -1;
    private InsertionAdorner? _adorner;

    public Orientation Orientation
    {
        get => (Orientation)GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    public bool IsLocked
    {
        get => (bool)GetValue(IsLockedProperty);
        set => SetValue(IsLockedProperty, value);
    }

    public static string? GetItemId(DependencyObject element) => (string?)element.GetValue(ItemIdProperty);

    public static void SetItemId(DependencyObject element, string? value) => element.SetValue(ItemIdProperty, value);

    public static string? GetTitle(DependencyObject element) => (string?)element.GetValue(TitleProperty);

    public static void SetTitle(DependencyObject element, string? value) => element.SetValue(TitleProperty, value);

    /// <summary>Na het verslepen van een item.</summary>
    public event EventHandler? OrderChanged;

    /// <summary>Lengte (breedte of hoogte) die de zichtbare items samen op één regel nodig hebben.</summary>
    public double SingleLineLength { get; private set; }

    public IEnumerable<UIElement> Items => InternalChildren.Cast<UIElement>();

    private bool Horizontal => Orientation == Orientation.Horizontal;

    private double Main(Size size) => Horizontal ? size.Width : size.Height;

    private double Cross(Size size) => Horizontal ? size.Height : size.Width;

    private static void OnOrientationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        foreach (UIElement child in ((ToolbarPanel)d).InternalChildren)
        {
            child.InvalidateMeasure();
        }
    }

    private static void OnIsLockedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var panel = (ToolbarPanel)d;
        panel.Cursor = panel.IsLocked ? null : Cursors.SizeAll;
        panel.Background = panel.IsLocked ? null : Brushes.Transparent;
        panel.EndDrag(commit: false);
    }

    // ---------------------------------------------------------------- Opmaak

    protected override Size MeasureOverride(Size availableSize)
    {
        var unlimited = new Size(double.PositiveInfinity, double.PositiveInfinity);
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(unlimited);
        }

        _lines.Clear();
        _skipped.Clear();
        List<UIElement> items = WithoutLooseSeparators(InternalChildren.Cast<UIElement>()
            .Where(c => c.Visibility != Visibility.Collapsed));
        SingleLineLength = items.Sum(c => Main(c.DesiredSize));

        double limit = Main(availableSize);
        if (SingleLineLength <= limit)
        {
            var line = new ToolbarLine(align: 0);
            items.ForEach(c => line.Add(c, Main(c.DesiredSize), Cross(c.DesiredSize)));
            _lines.Add(line);
        }
        else
        {
            WrapInto(items, limit);
        }

        double main = _lines.Count == 0 ? 0 : _lines.Max(l => l.Main);
        double cross = _lines.Sum(l => l.Cross);
        return Horizontal ? new Size(main, cross) : new Size(cross, main);
    }

    private void WrapInto(List<UIElement> items, double limit)
    {
        ToolbarLine? line = null;
        bool hasMiddle = items.Count(i => i is ToolbarSpacer) >= 2;
        int segment = 0;
        foreach (UIElement child in items)
        {
            if (child is ToolbarSpacer)
            {
                _skipped.Add(child);
                segment++;
                line = null;
                continue;
            }

            double length = Main(child.DesiredSize);
            if (line != null && line.Items.Count > 0 && line.Main + length > limit)
            {
                line.TrimTrailingSeparator(_skipped);
                line = null;
            }

            if (line == null)
            {
                if (child is ToolbarSeparator)
                {
                    _skipped.Add(child);
                    continue;
                }

                line = new ToolbarLine(segment == 0 ? 0 : hasMiddle && segment == 1 ? 0.5 : 1);
                _lines.Add(line);
            }

            line.Add(child, length, Cross(child.DesiredSize));
        }

        line?.TrimTrailingSeparator(_skipped);
    }

    /// <summary>Scheidingslijnen aan het begin of eind, naast de spacer of direct na elkaar weglaten.</summary>
    private List<UIElement> WithoutLooseSeparators(IEnumerable<UIElement> source)
    {
        var result = new List<UIElement>();
        foreach (UIElement child in source)
        {
            bool loose = child is ToolbarSeparator
                         && (result.Count == 0 || result[^1] is ToolbarSeparator or ToolbarSpacer);
            if (child is ToolbarSpacer && result.Count > 0 && result[^1] is ToolbarSeparator)
            {
                _skipped.Add(result[^1]);
                result.RemoveAt(result.Count - 1);
            }

            if (loose)
            {
                _skipped.Add(child);
            }
            else
            {
                result.Add(child);
            }
        }

        if (result.Count > 0 && result[^1] is ToolbarSeparator last)
        {
            _skipped.Add(last);
            result.RemoveAt(result.Count - 1);
        }

        return result;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (UIElement child in _skipped)
        {
            child.Arrange(new Rect());
        }

        double total = Main(finalSize);
        double crossPos = 0;
        foreach (ToolbarLine line in _lines)
        {
            double extra = Math.Max(0, total - line.Main);
            Queue<double> gaps = SpacerGaps(line, total, extra);
            double offset = gaps.Count == 0 ? extra * line.Align : 0;
            foreach (UIElement child in line.Items)
            {
                double length = Main(child.DesiredSize) + (child is ToolbarSpacer ? gaps.Dequeue() : 0);
                child.Arrange(Horizontal
                    ? new Rect(offset, crossPos, length, line.Cross)
                    : new Rect(crossPos, offset, line.Cross, length));
                offset += length;
            }

            crossPos += line.Cross;
        }

        return finalSize;
    }

    /// <summary>
    /// Ruimte per spacer. Met twee spacers staat de middelste groep precies in het midden van de werkbalk,
    /// tenzij hij dan over de groep links of rechts heen zou vallen.
    /// </summary>
    private Queue<double> SpacerGaps(ToolbarLine line, double total, double extra)
    {
        List<int> spacers = line.Items.Select((item, index) => (item, index))
            .Where(x => x.item is ToolbarSpacer).Select(x => x.index).ToList();
        if (spacers.Count != 2)
        {
            return new Queue<double>(spacers.Select(_ => extra / spacers.Count));
        }

        double Length(int from, int to) => line.Items.Skip(from).Take(to - from).Sum(i => Main(i.DesiredSize));
        double left = Length(0, spacers[0]);
        double middle = Length(spacers[0] + 1, spacers[1]);
        double right = Length(spacers[1] + 1, line.Items.Count);
        double start = Math.Clamp((total - middle) / 2, left, Math.Max(left, total - right - middle));
        return new Queue<double>(new[] { start - left, Math.Max(0, total - right - middle - start) });
    }

    // ---------------------------------------------------------------- Verslepen

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonDown(e);
        if (IsLocked)
        {
            return;
        }

        // Ontgrendeld: knoppen reageren niet op klikken, je pakt ze alleen op.
        e.Handled = true;
        Point p = e.GetPosition(this);
        UIElement? item = ItemAt(p);
        if (item == null || item is ToolbarSpacer)
        {
            return;
        }

        _pressedItem = item;
        _pressPoint = p;
        CaptureMouse();
    }

    protected override void OnPreviewMouseMove(MouseEventArgs e)
    {
        base.OnPreviewMouseMove(e);
        if (_pressedItem == null)
        {
            return;
        }

        Point p = e.GetPosition(this);
        if (!_dragging)
        {
            if (Math.Abs(p.X - _pressPoint.X) < DragThreshold && Math.Abs(p.Y - _pressPoint.Y) < DragThreshold)
            {
                return;
            }

            _dragging = true;
            _pressedItem.Opacity = 0.4;
            if (AdornerLayer.GetAdornerLayer(this) is { } layer)
            {
                _adorner = new InsertionAdorner(this);
                layer.Add(_adorner);
            }
        }

        _insertIndex = FindInsertIndex(p, out Rect marker);
        _adorner?.Show(_insertIndex < 0 ? Rect.Empty : marker);
        e.Handled = true;
    }

    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonUp(e);
        if (_pressedItem != null)
        {
            EndDrag(commit: _dragging);
            e.Handled = true;
        }
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        EndDrag(commit: false);
    }

    private void EndDrag(bool commit)
    {
        UIElement? item = _pressedItem;
        int target = _insertIndex;
        _pressedItem = null;
        _insertIndex = -1;
        _dragging = false;
        if (_adorner != null)
        {
            AdornerLayer.GetAdornerLayer(this)?.Remove(_adorner);
            _adorner = null;
        }

        if (item == null)
        {
            return;
        }

        item.Opacity = 1;
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }

        if (!commit || target < 0)
        {
            return;
        }

        int from = InternalChildren.IndexOf(item);
        if (target > from)
        {
            target--;
        }

        if (target == from)
        {
            return;
        }

        InternalChildren.RemoveAt(from);
        InternalChildren.Insert(target, item);
        OrderChanged?.Invoke(this, EventArgs.Empty);
    }

    // Op positie in plaats van via het klikdoel: uitgeschakelde knoppen krijgen zelf geen muisinvoer.
    private UIElement? ItemAt(Point p) =>
        InternalChildren.Cast<UIElement>().FirstOrDefault(child =>
            child.Visibility != Visibility.Collapsed && !_skipped.Contains(child)
            && LayoutInformation.GetLayoutSlot((FrameworkElement)child).Contains(p));

    /// <summary>Plek in de lijst waar het gesleepte item terechtkomt, plus de positie van de invoegstreep.</summary>
    private int FindInsertIndex(Point p, out Rect marker)
    {
        marker = Rect.Empty;
        UIElement? best = null;
        Rect bestRect = Rect.Empty;
        double bestDistance = double.MaxValue;
        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed || _skipped.Contains(child))
            {
                continue;
            }

            Rect rect = LayoutInformation.GetLayoutSlot((FrameworkElement)child);
            if (rect.Width <= 0 || rect.Height <= 0)
            {
                continue;
            }

            double dx = Math.Max(0, Math.Max(rect.Left - p.X, p.X - rect.Right));
            double dy = Math.Max(0, Math.Max(rect.Top - p.Y, p.Y - rect.Bottom));
            double distance = dx * dx + dy * dy;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = child;
                bestRect = rect;
            }
        }

        if (best == null)
        {
            return -1;
        }

        bool after = Horizontal ? p.X > bestRect.Left + bestRect.Width / 2 : p.Y > bestRect.Top + bestRect.Height / 2;
        marker = Horizontal
            ? new Rect((after ? bestRect.Right : bestRect.Left) - 1, bestRect.Top + 3, 2, Math.Max(0, bestRect.Height - 6))
            : new Rect(bestRect.Left + 3, (after ? bestRect.Bottom : bestRect.Top) - 1, Math.Max(0, bestRect.Width - 6), 2);
        return InternalChildren.IndexOf(best) + (after ? 1 : 0);
    }

    private sealed class ToolbarLine
    {
        public ToolbarLine(double align) => Align = align;

        /// <summary>0 = begin, 0,5 = midden, 1 = eind (als de regel geen spacer bevat).</summary>
        public double Align { get; }

        public List<UIElement> Items { get; } = new();

        public double Main { get; private set; }

        public double Cross { get; private set; }

        public void Add(UIElement child, double main, double cross)
        {
            Items.Add(child);
            Main += main;
            Cross = Math.Max(Cross, cross);
        }

        public void TrimTrailingSeparator(HashSet<UIElement> skipped)
        {
            if (Items.Count > 0 && Items[^1] is ToolbarSeparator separator)
            {
                Items.RemoveAt(Items.Count - 1);
                Main -= separator.DesiredSize.Width + separator.DesiredSize.Height;
                skipped.Add(separator);
            }
        }
    }

    private sealed class InsertionAdorner : Adorner
    {
        private Rect _marker = Rect.Empty;

        public InsertionAdorner(UIElement adornedElement) : base(adornedElement)
        {
            IsHitTestVisible = false;
        }

        public void Show(Rect marker)
        {
            _marker = marker;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            if (!_marker.IsEmpty)
            {
                dc.DrawRectangle(TryFindResource("AccentBrush") as Brush ?? Brushes.DodgerBlue, null, _marker);
            }
        }
    }
}

/// <summary>Flexibele ruimte in een <see cref="ToolbarPanel"/>; scheidt de groep aan het begin van die aan het eind.</summary>
public sealed class ToolbarSpacer : FrameworkElement
{
}

/// <summary>Dunne scheidingslijn die meedraait met de richting van de werkbalk.</summary>
public sealed class ToolbarSeparator : FrameworkElement
{
    private const double Thickness = 13;

    private static readonly DependencyProperty LineBrushProperty = DependencyProperty.Register(
        nameof(LineBrush), typeof(Brush), typeof(ToolbarSeparator),
        new FrameworkPropertyMetadata(Brushes.LightGray, FrameworkPropertyMetadataOptions.AffectsRender));

    public ToolbarSeparator() => SetResourceReference(LineBrushProperty, "DividerBrush");

    private Brush LineBrush => (Brush)GetValue(LineBrushProperty);

    private bool Vertical => VisualTreeHelper.GetParent(this) is ToolbarPanel { Orientation: Orientation.Vertical };

    protected override Size MeasureOverride(Size availableSize) =>
        Vertical ? new Size(0, Thickness) : new Size(Thickness, 0);

    protected override void OnRender(DrawingContext dc)
    {
        if (RenderSize.Width <= 0 || RenderSize.Height <= 0)
        {
            return;
        }

        Brush brush = LineBrush;
        Rect line = Vertical
            ? new Rect(6, Math.Floor(RenderSize.Height / 2), Math.Max(0, RenderSize.Width - 12), 1)
            : new Rect(Math.Floor(RenderSize.Width / 2), 6, 1, Math.Max(0, RenderSize.Height - 12));
        dc.DrawRectangle(brush, null, line);
    }
}
