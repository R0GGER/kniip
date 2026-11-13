using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Kniip.Infrastructure;
using Kniip.Settings;

namespace Kniip.History;

/// <summary>Wat er in kniip is opgeslagen of geüpload, nieuwste eerst.</summary>
public static class HistoryStore
{
    private const int MaxEntries = 500;
    private const int ThumbnailSize = 240;

    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static List<HistoryEntry>? _entries;

    private static string FilePath => Path.Combine(SettingsStore.Folder, "history.json");

    public static string ThumbnailFolder => Path.Combine(SettingsStore.Folder, "history");

    public static event Action? Changed;

    public static IReadOnlyList<HistoryEntry> Entries => Load();

    public static void Add(HistoryKind kind, string? filePath, string? url, BitmapSource? preview)
    {
        var entry = new HistoryEntry { Kind = kind, FilePath = filePath, Url = url };
        if (preview != null)
        {
            entry.Width = preview.PixelWidth;
            entry.Height = preview.PixelHeight;
            entry.Thumbnail = SaveThumbnail(entry.Id, preview);
        }

        List<HistoryEntry> entries = Load();
        entries.Insert(0, entry);
        while (entries.Count > MaxEntries)
        {
            DeleteThumbnail(entries[^1]);
            entries.RemoveAt(entries.Count - 1);
        }

        Save();
    }

    public static void Remove(HistoryEntry entry)
    {
        if (Load().Remove(entry))
        {
            DeleteThumbnail(entry);
            Save();
        }
    }

    public static void Clear()
    {
        foreach (HistoryEntry entry in Load())
        {
            DeleteThumbnail(entry);
        }

        Load().Clear();
        Save();
    }

    public static string? ThumbnailPath(HistoryEntry entry) =>
        entry.Thumbnail is { Length: > 0 } name ? Path.Combine(ThumbnailFolder, name) : null;

    /// <summary>Eerste beeld van een afbeelding of GIF, of null als dat niet lukt.</summary>
    public static BitmapSource? PreviewOf(string file)
    {
        try
        {
            return PixelUtil.LoadFile(file);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static List<HistoryEntry> Load()
    {
        if (_entries != null)
        {
            return _entries;
        }

        try
        {
            if (File.Exists(FilePath))
            {
                _entries = JsonSerializer.Deserialize<List<HistoryEntry>>(File.ReadAllText(FilePath), Options);
            }
        }
        catch (Exception)
        {
            // Beschadigd bestand: met een lege geschiedenis verder.
        }

        _entries ??= new List<HistoryEntry>();
        _entries.RemoveAll(entry => entry == null!);
        return _entries;
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsStore.Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Load(), Options));
        }
        catch (Exception)
        {
            // De geschiedenis bijwerken mag opslaan of uploaden nooit laten mislukken.
        }

        Changed?.Invoke();
    }

    private static string? SaveThumbnail(string id, BitmapSource image)
    {
        try
        {
            double scale = Math.Min(1.0, (double)ThumbnailSize / Math.Max(image.PixelWidth, image.PixelHeight));
            BitmapSource thumb = scale < 1 ? new TransformedBitmap(image, new ScaleTransform(scale, scale)) : image;
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(thumb));
            Directory.CreateDirectory(ThumbnailFolder);
            string name = id + ".png";
            using var stream = new FileStream(Path.Combine(ThumbnailFolder, name), FileMode.Create, FileAccess.Write);
            encoder.Save(stream);
            return name;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void DeleteThumbnail(HistoryEntry entry)
    {
        if (ThumbnailPath(entry) is not { } path)
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (Exception)
        {
            // Blijft staan; een losse miniatuur kan geen kwaad.
        }
    }

    public static IEnumerable<HistoryEntry> Recent(int count) => Load().Take(count);
}
