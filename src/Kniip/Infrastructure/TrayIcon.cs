using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Kniip.History;
using Kniip.Resources;

namespace Kniip.Infrastructure;

internal sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly Icon _baseIcon;
    private Icon? _badgeIcon;
    private bool _recording;
    private string? _updateVersion;
    private static string RecordText => Strings.Tray_RecordMp4;

    private readonly ToolStripMenuItem _clipboardItem;
    private readonly ToolStripMenuItem _pauseItem;
    private readonly ToolStripMenuItem _snipItem;
    private readonly ToolStripMenuItem _windowItem;
    private readonly ToolStripMenuItem _ocrItem;
    private readonly ToolStripMenuItem _recordItem;
    private readonly ToolStripMenuItem _gifItem;
    private readonly ToolStripMenuItem _foldersItem;
    private readonly ToolStripMenuItem _recentItem;
    private readonly ToolStripMenuItem _delayItem;
    private readonly List<(ToolStripItem Item, string Glyph)> _glyphItems = new();
    private Panel _delayPanel = null!;
    private TrackBar _delaySlider = null!;
    private NumericUpDown _delayNumber = null!;
    private Label _delayUnit = null!;
    private int _delaySeconds;
    private int _savedDelaySeconds;

    /// <summary>Nieuwe vertraging in seconden, afgevuurd bij het sluiten van het submenu als de waarde is gewijzigd.</summary>
    public event Action<int>? CaptureDelayChanged;

    public event Action? OpenFromClipboardRequested;
    public event Action? OpenRequested;
    public event Action? SnipRequested;
    public event Action? WindowSnipRequested;
    public event Action? TextRecognitionRequested;
    public event Action? RecordRequested;
    public event Action? GifRequested;
    public event Action? OpenFileRequested;
    public event Action? SettingsRequested;
    public event Action? ExitRequested;
    public event Action<bool>? PauseChanged;

    /// <summary>Geschiedenisvenster openen; true = met miniaturen.</summary>
    public event Action<bool>? HistoryRequested;

    /// <summary>Mappen voor het submenu Map openen, opgevraagd bij het openen; een pad van null staat grijs.</summary>
    public Func<IReadOnlyList<(string Label, string? Path)>>? FoldersProvider { get; set; }

    public TrayIcon()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(WithGlyph(new ToolStripMenuItem(Strings.Tray_Open, null, (_, _) => OpenRequested?.Invoke())
        {
            ToolTipText = Strings.Tray_OpenTip
        }, "\uE8A7"));
        menu.Items.Add(new ToolStripSeparator());
        _clipboardItem = WithGlyph(new ToolStripMenuItem(Strings.Tray_EditFromClipboard, null, (_, _) => OpenFromClipboardRequested?.Invoke()), "\uE77F");
        menu.Items.Add(_clipboardItem);
        menu.Items.Add(WithGlyph(new ToolStripMenuItem(Strings.Tray_OpenImage, null, (_, _) => OpenFileRequested?.Invoke()), "\uE8E5"));
        menu.Items.Add(new ToolStripSeparator());
        _snipItem = WithGlyph(new ToolStripMenuItem(Strings.Tray_SnipRectangle, null, (_, _) => SnipRequested?.Invoke())
        {
            Font = new Font(SystemFonts.MenuFont ?? Control.DefaultFont, FontStyle.Bold)
        }, "\uF407");
        menu.Items.Add(_snipItem);
        _windowItem = WithGlyph(new ToolStripMenuItem(Strings.Tray_SnipWindow, null, (_, _) => WindowSnipRequested?.Invoke()), "\uE737");
        menu.Items.Add(_windowItem);
        _ocrItem = WithGlyph(new ToolStripMenuItem(Strings.Tray_Ocr, null, (_, _) => TextRecognitionRequested?.Invoke())
        {
            ToolTipText = Strings.Tray_OcrTip
        }, "\uE8D2");
        menu.Items.Add(_ocrItem);
        _delayItem = WithGlyph(BuildDelayItem(), "\uE916");
        menu.Items.Add(_delayItem);
        _recordItem = WithGlyph(new ToolStripMenuItem(RecordText, null, (_, _) => RecordRequested?.Invoke()), "\uE714");
        menu.Items.Add(_recordItem);
        _gifItem = WithGlyph(new ToolStripMenuItem(Strings.Tray_RecordGif, null, (_, _) => GifRequested?.Invoke()), "\uF4A9");
        menu.Items.Add(_gifItem);
        menu.Items.Add(new ToolStripSeparator());

        _foldersItem = WithGlyph(new ToolStripMenuItem(Strings.Common_OpenFolder), "\uE838");
        _foldersItem.DropDownItems.Add(new ToolStripMenuItem("..."));
        _foldersItem.DropDownOpening += (_, _) => BuildFolders();
        menu.Items.Add(_foldersItem);
        menu.Items.Add(WithGlyph(new ToolStripMenuItem(Strings.Tray_History, null, (_, _) => HistoryRequested?.Invoke(false)), "\uE81C"));
        menu.Items.Add(WithGlyph(new ToolStripMenuItem(Strings.Tray_ImageHistory, null, (_, _) => HistoryRequested?.Invoke(true)), "\uE91B"));
        _recentItem = WithGlyph(new ToolStripMenuItem(Strings.Tray_RecentItems), "\uE823");
        _recentItem.DropDownItems.Add(new ToolStripMenuItem("..."));
        _recentItem.DropDownOpening += (_, _) => BuildRecent();
        menu.Items.Add(_recentItem);
        menu.Items.Add(new ToolStripSeparator());

        // Zonder icoon: het vinkje gebruikt dezelfde plek.
        _pauseItem = new ToolStripMenuItem(Strings.Tray_Pause) { CheckOnClick = true };
        _pauseItem.CheckedChanged += (_, _) => PauseChanged?.Invoke(_pauseItem.Checked);
        menu.Items.Add(_pauseItem);
        menu.Items.Add(WithGlyph(new ToolStripMenuItem(Strings.Tray_Settings, null, (_, _) => SettingsRequested?.Invoke()), "\uE713"));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(WithGlyph(new ToolStripMenuItem(Strings.Tray_Exit, null, (_, _) => ExitRequested?.Invoke()), "\uE7E8"));
        RenderGlyphs(menu.ImageScalingSize, SystemColors.ControlText);

        _baseIcon = LoadIcon();
        _icon = new NotifyIcon
        {
            Icon = _baseIcon,
            Text = IdleText,
            ContextMenuStrip = menu,
            Visible = true
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                SnipRequested?.Invoke();
            }
        };
    }

    public bool IsPaused => _pauseItem.Checked;

    /// <summary>Toont de sneltoetsen naast de menu-items; null = geen sneltoets.</summary>
    public void SetHotkeys(string? snip, string? window, string? record, string? gif, string? clipboard, string? ocr)
    {
        _clipboardItem.ShortcutKeyDisplayString = clipboard;
        _ocrItem.ShortcutKeyDisplayString = ocr;
        _snipItem.ShortcutKeyDisplayString = snip;
        _windowItem.ShortcutKeyDisplayString = window;
        _recordItem.ShortcutKeyDisplayString = record;
        _gifItem.ShortcutKeyDisplayString = gif;
    }

    /// <summary>Menutekst voor de opnameknop; null zet de standaardtekst terug.</summary>
    public void SetRecordItem(string? text, bool recording)
    {
        _recordItem.Text = text ?? RecordText;
        _gifItem.Visible = text == null;
        _recording = recording;
        UpdateToolTip();
    }

    /// <summary>Oranje stip rechtsonder in het icoon en een melding in de tooltip; null haalt de stip weg.</summary>
    public void SetUpdateAvailable(string? version)
    {
        _updateVersion = version;
        if (version != null)
        {
            _badgeIcon ??= CreateBadgeIcon(_baseIcon);
        }

        _icon.Icon = version != null ? _badgeIcon : _baseIcon;
        UpdateToolTip();
    }

    private void UpdateToolTip()
    {
        string text = _recording ? string.Format(Strings.Tray_RecordingToolTip, AppIdentity.DisplayName) : IdleText;
        if (_updateVersion != null)
        {
            text += "\n" + string.Format(Strings.Tray_UpdateToolTip, _updateVersion);
        }

        // NotifyIcon.Text gooit een uitzondering boven 127 tekens.
        _icon.Text = text.Length > 127 ? text[..127] : text;
    }

    private static Icon CreateBadgeIcon(Icon source)
    {
        using Bitmap bitmap = source.ToBitmap();
        using (Graphics g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            float size = bitmap.Width * 0.5f;
            float x = bitmap.Width - size, y = bitmap.Height - size;
            using var ring = new SolidBrush(Color.White);
            g.FillEllipse(ring, x, y, size, size);
            float inset = Math.Max(1f, bitmap.Width / 16f);
            using var dot = new SolidBrush(Color.FromArgb(0xF7, 0x63, 0x0C));
            g.FillEllipse(dot, x + inset, y + inset, size - 2 * inset, size - 2 * inset);
        }

        IntPtr handle = bitmap.GetHicon();
        try
        {
            using Icon temp = Icon.FromHandle(handle);
            return (Icon)temp.Clone();
        }
        finally
        {
            Kniip.Interop.NativeMethods.DestroyIcon(handle);
        }
    }

    public void SetCaptureDelay(int seconds)
    {
        _savedDelaySeconds = seconds;
        ShowCaptureDelay(seconds);
    }

    private ToolStripMenuItem BuildDelayItem()
    {
        var item = new ToolStripMenuItem();
        _delaySlider = new TrackBar
        {
            Minimum = 0,
            Maximum = Kniip.Settings.AppSettings.MaxCaptureDelaySeconds,
            TickFrequency = 5,
            LargeChange = 5,
            SmallChange = 1,
            AutoSize = false,
            Width = 200,
            Height = 32,
            Location = new Point(4, 6)
        };
        _delayNumber = new NumericUpDown
        {
            Minimum = 0,
            Maximum = Kniip.Settings.AppSettings.MaxCaptureDelaySeconds,
            Width = 52,
            Location = new Point(_delaySlider.Right + 6, 10),
            TextAlign = HorizontalAlignment.Right
        };
        _delayUnit = new Label
        {
            Text = Strings.Tray_Seconds,
            AutoSize = true,
            Location = new Point(_delayNumber.Right + 4, 13)
        };
        var panel = new Panel
        {
            Size = new Size(_delayUnit.Left + _delayUnit.PreferredWidth + 10, 44),
            BackColor = ProfessionalColors.ToolStripDropDownBackground
        };
        _delayPanel = panel;
        _delaySlider.BackColor = panel.BackColor;
        panel.Controls.AddRange(new Control[] { _delaySlider, _delayNumber, _delayUnit });

        _delaySlider.ValueChanged += (_, _) => ShowCaptureDelay(_delaySlider.Value);
        _delayNumber.ValueChanged += (_, _) => ShowCaptureDelay((int)_delayNumber.Value);
        _delayNumber.KeyDown += (_, e) =>
        {
            if (e.KeyCode is Keys.Enter or Keys.Escape)
            {
                e.SuppressKeyPress = true;
                _icon.ContextMenuStrip?.Close();
            }
        };

        item.DropDownItems.Add(new ToolStripControlHost(panel) { AutoSize = false, Size = panel.Size, Margin = Padding.Empty });
        item.DropDownClosed += (_, _) =>
        {
            ShowCaptureDelay((int)_delayNumber.Value);
            if (_delaySeconds != _savedDelaySeconds)
            {
                _savedDelaySeconds = _delaySeconds;
                CaptureDelayChanged?.Invoke(_delaySeconds);
            }
        };
        item.Text = string.Format(Strings.Tray_Delay, 0);
        return item;
    }

    private void ShowCaptureDelay(int seconds)
    {
        _delaySeconds = seconds;
        if (_delaySlider.Value != seconds)
        {
            _delaySlider.Value = seconds;
        }

        if ((int)_delayNumber.Value != seconds)
        {
            _delayNumber.Value = seconds;
        }

        _delayItem.Text = string.Format(Strings.Tray_Delay, seconds);
    }

    /// <summary>Menu in de kleurmodus van kniip.</summary>
    public void ApplyTheme(bool dark)
    {
        ToolStripManager.Renderer = dark ? new DarkMenuRenderer() : new ToolStripProfessionalRenderer();
        Color back = dark ? DarkMenuColors.Background : ProfessionalColors.ToolStripDropDownBackground;
        Color fore = dark ? DarkMenuColors.Text : SystemColors.ControlText;
        _delayPanel.BackColor = back;
        _delaySlider.BackColor = back;
        _delayUnit.ForeColor = fore;
        _delayNumber.BackColor = dark ? DarkMenuColors.Input : SystemColors.Window;
        _delayNumber.ForeColor = dark ? DarkMenuColors.Text : SystemColors.WindowText;
        if (_icon.ContextMenuStrip is { } menu)
        {
            RenderGlyphs(menu.ImageScalingSize, fore);
        }
    }

    private T WithGlyph<T>(T item, string glyph) where T : ToolStripItem
    {
        _glyphItems.Add((item, glyph));
        return item;
    }

    /// <summary>Tekent de menu-iconen uit Segoe Fluent Icons in de tekstkleur van het menu.</summary>
    private void RenderGlyphs(Size size, Color color)
    {
        using Font? font = CreateIconFont(size.Height);
        foreach ((ToolStripItem item, string glyph) in _glyphItems)
        {
            Image? old = item.Image;
            item.Image = font == null ? null : RenderGlyph(glyph, font, size, color);
            old?.Dispose();
        }
    }

    private static Font? CreateIconFont(float pixels)
    {
        foreach (string family in new[] { "Segoe Fluent Icons", "Segoe MDL2 Assets" })
        {
            var font = new Font(family, pixels, GraphicsUnit.Pixel);
            // GDI+ valt stil terug op een ander lettertype als de naam niet bestaat.
            if (font.Name == family)
            {
                return font;
            }

            font.Dispose();
        }

        return null;
    }

    private static Bitmap RenderGlyph(string glyph, Font font, Size size, Color color)
    {
        var bitmap = new Bitmap(size.Width, size.Height);
        using Graphics g = Graphics.FromImage(bitmap);
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        using var brush = new SolidBrush(color);
        using var format = new StringFormat(StringFormat.GenericTypographic)
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };
        g.DrawString(glyph, font, brush, new RectangleF(0, 0, size.Width, size.Height), format);
        return bitmap;
    }

    private static string IdleText => AppIdentity.IsTest
        ? $"{AppIdentity.DisplayName} {Kniip.Update.UpdateChecker.CurrentVersion}"
        : AppIdentity.DisplayName;

    private void BuildFolders()
    {
        _foldersItem.DropDownItems.Clear();
        foreach ((string label, string? path) in FoldersProvider?.Invoke() ?? Array.Empty<(string, string?)>())
        {
            var item = new ToolStripMenuItem(label) { Enabled = path != null, ToolTipText = path };
            if (path != null)
            {
                item.Click += (_, _) => HistoryActions.OpenFolder(path);
            }

            _foldersItem.DropDownItems.Add(item);
        }
    }

    private void BuildRecent()
    {
        foreach (ToolStripItem old in _recentItem.DropDownItems)
        {
            old.Image?.Dispose();
        }

        _recentItem.DropDownItems.Clear();
        List<HistoryEntry> entries = HistoryStore.Recent(10).ToList();
        if (entries.Count == 0)
        {
            _recentItem.DropDownItems.Add(new ToolStripMenuItem(Strings.Tray_NothingSaved) { Enabled = false });
            return;
        }

        foreach (HistoryEntry entry in entries)
        {
            var item = new ToolStripMenuItem($"[{entry.Time:HH:mm:ss}] {entry.Url ?? entry.Name}")
            {
                Image = LoadThumbnail(entry),
                ToolTipText = entry.FilePath
            };
            item.Click += (_, _) =>
            {
                _icon.ContextMenuStrip?.Close();
                HistoryActions.Open(entry);
            };
            foreach (HistoryCommand? command in HistoryActions.CommandsFor(entry))
            {
                if (command == null)
                {
                    item.DropDownItems.Add(new ToolStripSeparator());
                }
                else
                {
                    item.DropDownItems.Add(new ToolStripMenuItem(command.Label, null, (_, _) => command.Run()) { Enabled = command.Enabled });
                }
            }

            _recentItem.DropDownItems.Add(item);
        }
    }

    private static Image? LoadThumbnail(HistoryEntry entry)
    {
        if (HistoryStore.ThumbnailPath(entry) is not { } path || !File.Exists(path))
        {
            return null;
        }

        try
        {
            using var stream = new MemoryStream(File.ReadAllBytes(path));
            using var image = Image.FromStream(stream);
            Size size = SystemInformation.SmallIconSize;
            double scale = Math.Min((double)size.Width / image.Width, (double)size.Height / image.Height);
            var thumb = new Bitmap(size.Width, size.Height);
            using Graphics g = Graphics.FromImage(thumb);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            int w = Math.Max(1, (int)(image.Width * scale)), h = Math.Max(1, (int)(image.Height * scale));
            g.DrawImage(image, (size.Width - w) / 2, (size.Height - h) / 2, w, h);
            return thumb;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void ShowBalloon(string title, string text) =>
        _icon.ShowBalloonTip(4000, title, text, ToolTipIcon.Info);

    private static Icon LoadIcon()
    {
        // Uitsnede van AppIcon.ico zonder snijlijnen (tools\make-tray-icon.ps1): op 16px vult het kader zo het vak.
        var info = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/TrayIcon.ico"));
        if (info == null)
        {
            return SystemIcons.Application;
        }

        using var stream = info.Stream;
        return new Icon(stream, SystemInformation.SmallIconSize);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        foreach ((ToolStripItem item, _) in _glyphItems)
        {
            item.Image?.Dispose();
        }

        _badgeIcon?.Dispose();
        _baseIcon.Dispose();
    }

    private sealed class DarkMenuColors : ProfessionalColorTable
    {
        public static readonly Color Background = Color.FromArgb(0x2B, 0x2B, 0x2B);
        public static readonly Color Highlight = Color.FromArgb(0x3D, 0x3D, 0x3D);
        public static readonly Color Border = Color.FromArgb(0x45, 0x45, 0x45);
        public static readonly Color Input = Color.FromArgb(0x2D, 0x2D, 0x2D);
        public static readonly Color Text = Color.FromArgb(0xF2, 0xF2, 0xF2);

        public override Color ToolStripDropDownBackground => Background;
        public override Color ImageMarginGradientBegin => Background;
        public override Color ImageMarginGradientMiddle => Background;
        public override Color ImageMarginGradientEnd => Background;
        public override Color MenuBorder => Border;
        public override Color MenuItemBorder => Highlight;
        public override Color MenuItemSelected => Highlight;
        public override Color MenuItemSelectedGradientBegin => Highlight;
        public override Color MenuItemSelectedGradientEnd => Highlight;
        public override Color MenuItemPressedGradientBegin => Highlight;
        public override Color MenuItemPressedGradientEnd => Highlight;
        public override Color SeparatorDark => Border;
        public override Color SeparatorLight => Background;
        public override Color CheckBackground => Highlight;
        public override Color CheckSelectedBackground => Highlight;
        public override Color CheckPressedBackground => Highlight;
    }

    private sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkMenuColors())
        {
            RoundedEdges = false;
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? DarkMenuColors.Text : Color.FromArgb(0x80, 0x80, 0x80);
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = DarkMenuColors.Text;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            // Het standaardvinkje is zwart en valt weg op de donkere achtergrond.
            Rectangle r = e.ImageRectangle;
            using var background = new SolidBrush(DarkMenuColors.Highlight);
            e.Graphics.FillRectangle(background, r);
            using var pen = new Pen(DarkMenuColors.Text, 1.6f);
            System.Drawing.Drawing2D.SmoothingMode smoothing = e.Graphics.SmoothingMode;
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.DrawLines(pen, new[]
            {
                new PointF(r.Left + r.Width * 0.22f, r.Top + r.Height * 0.52f),
                new PointF(r.Left + r.Width * 0.42f, r.Top + r.Height * 0.72f),
                new PointF(r.Left + r.Width * 0.78f, r.Top + r.Height * 0.30f)
            });
            e.Graphics.SmoothingMode = smoothing;
        }
    }
}
