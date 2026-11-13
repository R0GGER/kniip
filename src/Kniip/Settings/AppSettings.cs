using System;
using System.Collections.Generic;
using System.Windows.Controls;
using Kniip.Editor;
using Kniip.Upload;

namespace Kniip.Settings;

public enum CaptureAction
{
    /// <summary>Melding rechtsonder tonen; klikken opent de editor.</summary>
    Notify,

    /// <summary>Editor direct openen.</summary>
    OpenEditor,

    /// <summary>Niets doen (alleen handmatig openen via tray of "Openen met").</summary>
    Nothing
}

public enum ScreencastFormat
{
    Mp4,
    Gif
}

public enum SaveButtonChoice
{
    /// <summary>Alleen het bestand opslaan.</summary>
    SaveOnly,

    /// <summary>Bestand opslaan en naar het klembord kopiëren.</summary>
    SaveAndCopy
}

public enum EditorLayout
{
    /// <summary>Eén werkbalk met gereedschappen en acties.</summary>
    Single,

    /// <summary>Split-weergave: gereedschappen boven, opslaan/kopiëren en zoom onder.</summary>
    Split,

    /// <summary>Als Split, met screenshot, screencast en OCR in een zijbalk naast het canvas of zwevend erboven.</summary>
    ThreeSplit
}

/// <summary>Wat er gebeurt als het editorvenster met tabbladen wordt gesloten.</summary>
public enum EditorCloseBehavior
{
    /// <summary>Elke keer vragen.</summary>
    Ask,

    /// <summary>Alle tabbladen sluiten (met de gewone vraag bij niet-opgeslagen aantekeningen).</summary>
    CloseAll,

    /// <summary>Venster verbergen; de tabbladen blijven bewaard tot het volgende knipsel of "Open kniip".</summary>
    KeepTabs
}

public enum ColorMode
{
    Light,
    Dark,

    /// <summary>De app-modus van Windows volgen (licht of donker).</summary>
    System
}

public sealed class AppSettings
{
    public bool WatchClipboard { get; set; } = true;
    public bool WatchScreenshotsFolder { get; set; } = true;
    public bool OnlyFromSnippingTool { get; set; } = true;
    public CaptureAction Action { get; set; } = CaptureAction.Notify;
    public bool StraightHighlight { get; set; } = true;
    public ToolKind LastTool { get; set; } = ToolKind.Rectangle;
    public string? LastSaveFolder { get; set; }

    /// <summary>Map waarin knipsels zonder te vragen worden opgeslagen; leeg betekent elke keer vragen.</summary>
    public string? SnipSaveFolder { get; set; }

    /// <summary>Naamsjabloon voor knipsels (zie FileNameTemplate); leeg betekent "Knipsel" met datum en tijd.</summary>
    public string? SnipFileNameTemplate { get; set; }
    public Dictionary<ToolKind, StyleSet> ToolStyles { get; set; } = new();

    /// <summary>Recent gekozen emoji (codepunten), nieuwste eerst.</summary>
    public List<string> RecentEmoji { get; set; } = new();

    /// <summary>Sneltoets voor een eigen rechthoekknipsel (zie HotkeyGesture); leeg betekent geen sneltoets.</summary>
    public string SnipHotkey { get; set; } = "PrintScreen";

    /// <summary>Sneltoets om een venster aan te klikken en te knippen.</summary>
    public string WindowHotkey { get; set; } = "Alt+PrintScreen";

    /// <summary>Sneltoets voor een screencast die als MP4 wordt aangeboden.</summary>
    public string RecordHotkey { get; set; } = "Ctrl+Shift+R";

    /// <summary>Sneltoets voor een screencast die als GIF wordt aangeboden.</summary>
    public string GifHotkey { get; set; } = "Ctrl+Shift+G";

    /// <summary>Sneltoets om de afbeelding op het klembord in de editor te openen; standaard geen.</summary>
    public string ClipboardHotkey { get; set; } = "";

    /// <summary>Sneltoets om een rechthoek te kiezen en de tekst erin te herkennen; standaard geen.</summary>
    public string OcrHotkey { get; set; } = "";
    public bool RecordSystemAudio { get; set; } = true;
    public bool RecordMicrophone { get; set; }
    public bool RecordShowCursor { get; set; } = true;
    public bool RecordHighlightClicks { get; set; } = true;
    public int RecordFps { get; set; } = 30;
    public int GifFps { get; set; } = 10;
    public int GifMaxWidth { get; set; } = 800;
    public ScreencastFormat LastScreencastFormat { get; set; } = ScreencastFormat.Mp4;
    public string? LastVideoFolder { get; set; }

    /// <summary>Map waarin screencasts zonder te vragen worden opgeslagen; leeg betekent elke keer vragen.</summary>
    public string? VideoSaveFolder { get; set; }

    /// <summary>Naamsjabloon voor screencasts (zie FileNameTemplate); leeg betekent "Screencast" met datum en tijd.</summary>
    public string? VideoFileNameTemplate { get; set; }

    /// <summary>Gedrag van de knop Opslaan in de editor en de screencast.</summary>
    public SaveButtonChoice SaveButton { get; set; } = SaveButtonChoice.SaveOnly;

    /// <summary>Rand van het editorvenster waar de werkbalk staat.</summary>
    public Dock ToolbarPosition { get; set; } = Dock.Top;

    /// <summary>Vergrendeld: knoppen werken gewoon. Ontgrendeld: knoppen kunnen versleept worden.</summary>
    public bool ToolbarLocked { get; set; } = true;

    /// <summary>Volgorde van de werkbalkitems (ItemId's); leeg betekent de standaardvolgorde.</summary>
    public List<string> ToolbarOrder { get; set; } = new();

    /// <summary>Kleurmodus van alle vensters van kniip.</summary>
    public ColorMode ColorMode { get; set; } = ColorMode.Light;

    /// <summary>Indeling van de werkbalk(en) in de editor.</summary>
    public EditorLayout EditorLayout { get; set; } = EditorLayout.ThreeSplit;

    /// <summary>Volgorde van de werkbalkitems in de gesplitste indeling (boven, dan onder); leeg betekent de standaardvolgorde.</summary>
    public List<string> SplitToolbarOrder { get; set; } = new();

    /// <summary>Gedrag bij het sluiten van het editorvenster terwijl er tabbladen open zijn.</summary>
    public EditorCloseBehavior EditorCloseBehavior { get; set; } = EditorCloseBehavior.Ask;

    /// <summary>Kant van het canvas waar de balk met Nieuw en Screencast van de drie-split-indeling staat.</summary>
    public Dock CaptureBarPosition { get; set; } = Dock.Left;

    /// <summary>Nieuw en Screencast zwevend boven het canvas tonen in plaats van in een balk aan CaptureBarPosition.</summary>
    public bool CaptureBarFloating { get; set; } = true;

    /// <summary>Positie van de zwevende knoppen als fractie (0-1) van de vrije ruimte boven het canvas, horizontaal.</summary>
    public double CaptureBarFloatX { get; set; }

    /// <summary>Positie van de zwevende knoppen als fractie (0-1) van de vrije ruimte boven het canvas, verticaal.</summary>
    public double CaptureBarFloatY { get; set; } = 0.5;

    /// <summary>De zwevende knoppen staan vast en kunnen niet versleept worden.</summary>
    public bool CaptureBarFloatLocked { get; set; }

    public List<string> ToolbarHidden { get; set; } = new();

    /// <summary>Linialen en hulplijnen in de editor tonen; objecten klikken dan ook vast aan hulplijnen en de randen en het midden van de afbeelding.</summary>
    public bool ShowRulers { get; set; } = true;

    /// <summary>Zipline-server waar Uploaden naartoe gaat; leeg betekent geen uploadknop.</summary>
    public UploaderConfig? Uploader { get; set; }

    /// <summary>Bij het starten en daarna dagelijks op de achtergrond kijken of er een nieuwere versie is.</summary>
    public bool CheckForUpdates { get; set; } = true;

    /// <summary>Vergrootglas met kruisdraad en coördinaten tonen bij het kiezen van een gebied (knipsel en screencast).</summary>
    public bool ShowMagnifier { get; set; } = true;

    private int _captureDelaySeconds;

    /// <summary>Seconden wachten voordat een rechthoek- of vensterknipsel het scherm bevriest (0-60); 0 is direct.</summary>
    public int CaptureDelaySeconds
    {
        get => _captureDelaySeconds;
        set => _captureDelaySeconds = Math.Clamp(value, 0, MaxCaptureDelaySeconds);
    }

    public const int MaxCaptureDelaySeconds = 60;

    public StyleSet StyleFor(ToolKind tool)
    {
        if (!ToolStyles.TryGetValue(tool, out StyleSet? style))
        {
            style = StyleSet.DefaultFor(tool);
            ToolStyles[tool] = style;
        }

        return style;
    }
}
