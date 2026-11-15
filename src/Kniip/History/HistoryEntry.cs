using System;
using System.IO;
using System.Text.Json.Serialization;
using Kniip.Resources;

namespace Kniip.History;

public enum HistoryKind
{
    Snip,
    Mp4,
    Gif,

    /// <summary>Geüploade afbeelding, via het klembord of "Kopiëren naar".</summary>
    Image,

    /// <summary>Geüploade video die niet in kniip is opgenomen.</summary>
    Video,

    /// <summary>Geüploade tekst van het klembord.</summary>
    Text,

    /// <summary>Overig geüpload bestand.</summary>
    File
}

public sealed class HistoryEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime Time { get; set; } = DateTime.Now;
    public HistoryKind Kind { get; set; }

    /// <summary>Opgeslagen bestand; leeg als het alleen is geüpload.</summary>
    public string? FilePath { get; set; }

    /// <summary>Link na het uploaden; leeg als het alleen is opgeslagen.</summary>
    public string? Url { get; set; }

    /// <summary>Bestandsnaam van de miniatuur in <see cref="HistoryStore.ThumbnailFolder"/>.</summary>
    public string? Thumbnail { get; set; }

    public int Width { get; set; }
    public int Height { get; set; }

    [JsonIgnore]
    public string Name => FilePath is { Length: > 0 }
        ? Path.GetFileName(FilePath)
        : Url is { Length: > 0 } ? Url[(Url.TrimEnd('/').LastIndexOf('/') + 1)..] : "";

    [JsonIgnore]
    public string KindText => Kind switch
    {
        HistoryKind.Mp4 => "Screencast (MP4)",
        HistoryKind.Gif => "Screencast (GIF)",
        HistoryKind.Image => Strings.History_KindImage,
        HistoryKind.Video => "Video",
        HistoryKind.Text => Strings.History_KindText,
        HistoryKind.File => Strings.History_KindFile,
        _ => Strings.Snip_Name
    };

    /// <summary>Teken uit Segoe Fluent Icons voor items zonder miniatuur.</summary>
    [JsonIgnore]
    public string Glyph => Kind switch
    {
        HistoryKind.Text => "\uE8A5",
        HistoryKind.File => "\uE7C3",
        HistoryKind.Image or HistoryKind.Snip => "\uEB9F",
        _ => "\uE714"
    };

    [JsonIgnore]
    public bool IsImage => Kind is HistoryKind.Snip or HistoryKind.Image;

    [JsonIgnore]
    public bool FileExists => FilePath is { Length: > 0 } && System.IO.File.Exists(FilePath);
}
