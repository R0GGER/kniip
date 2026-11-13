using System;
using System.IO;
using System.Text.Json.Serialization;

namespace Kniip.History;

public enum HistoryKind
{
    Snip,
    Mp4,
    Gif
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
        _ => "Knipsel"
    };

    [JsonIgnore]
    public bool FileExists => FilePath is { Length: > 0 } && File.Exists(FilePath);
}
