using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Kniip.Editor.Emoji;

public sealed record EmojiCategory(string Id, string Title, string IconEmoji);

public sealed class EmojiInfo
{
    public required string Unified { get; init; }
    public required string Name { get; init; }
    public required string ShortName { get; init; }
    public required string Category { get; init; }
    public required string Keywords { get; init; }
    public required int SheetX { get; init; }
    public required int SheetY { get; init; }
    public IReadOnlyDictionary<SkinTone, (string Unified, int X, int Y)>? Skins { get; init; }

    public bool HasSkinTones => Skins != null;
}

/// <summary>
/// De Twemoji-set uit emoji-datasource (zie tools/fetch-emoji.ps1): één spritesheet met cellen van 64 px
/// plus 1 px rand, en een compacte catalogus met Nederlandse en Engelse trefwoorden.
/// </summary>
public static class EmojiCatalog
{
    private const int CellSize = 64;
    private const int CellStride = CellSize + 2;
    private const string AssetRoot = "pack://application:,,,/Assets/Emoji/";

    public static readonly IReadOnlyList<EmojiCategory> Categories = new[]
    {
        new EmojiCategory("Smileys & Emotion", "Smileys en emotie", "1f600"),
        new EmojiCategory("People & Body", "Mensen", "1f44b"),
        new EmojiCategory("Animals & Nature", "Dieren en natuur", "1f43b"),
        new EmojiCategory("Food & Drink", "Eten en drinken", "1f354"),
        new EmojiCategory("Activities", "Activiteiten", "26bd"),
        new EmojiCategory("Travel & Places", "Reizen en plaatsen", "1f697"),
        new EmojiCategory("Objects", "Objecten", "1f4a1"),
        new EmojiCategory("Symbols", "Symbolen", "2764-fe0f"),
        new EmojiCategory("Flags", "Vlaggen", "1f3c1")
    };

    private static readonly Lazy<BitmapSource> SheetLoader = new(LoadSheet);
    private static readonly Lazy<Data> DataLoader = new(LoadData);
    private static readonly ConcurrentDictionary<(int, int), ImageSource> Cells = new();

    private sealed record Data(IReadOnlyList<EmojiInfo> All, IReadOnlyDictionary<string, EmojiInfo> ByUnified);

    public static IReadOnlyList<EmojiInfo> All => DataLoader.Value.All;

    /// <summary>Laadt de catalogus en het sheet alvast op de achtergrond, zodat de eerste keer kiezen niet hapert.</summary>
    public static void Preload() =>
        System.Threading.Tasks.Task.Run(() =>
        {
            _ = SheetLoader.Value;
            _ = DataLoader.Value;
        });

    public static EmojiInfo? Find(string unified) =>
        DataLoader.Value.ByUnified.TryGetValue(unified, out EmojiInfo? info) ? info : null;

    public static ImageSource Get(string unified, SkinTone tone)
    {
        EmojiInfo? info = Find(unified) ?? Find("1f44d");
        return info == null ? Placeholder : Get(info, tone);
    }

    public static ImageSource Get(EmojiInfo info, SkinTone tone)
    {
        (int x, int y) = tone != SkinTone.Default && info.Skins != null && info.Skins.TryGetValue(tone, out var skin)
            ? (skin.X, skin.Y)
            : (info.SheetX, info.SheetY);
        return Cells.GetOrAdd((x, y), key =>
        {
            var cell = new CroppedBitmap(SheetLoader.Value,
                new Int32Rect(key.Item1 * CellStride + 1, key.Item2 * CellStride + 1, CellSize, CellSize));
            cell.Freeze();
            return cell;
        });
    }

    /// <summary>Het emojiteken zelf (met huidskleur), bijvoorbeeld voor de tooltip.</summary>
    public static string ToText(EmojiInfo info, SkinTone tone)
    {
        string unified = tone != SkinTone.Default && info.Skins != null && info.Skins.TryGetValue(tone, out var skin)
            ? skin.Unified
            : info.Unified;
        return string.Concat(unified.Split('-').Select(cp => char.ConvertFromUtf32(Convert.ToInt32(cp, 16))));
    }

    /// <summary>
    /// Alle woorden uit de zoekterm moeten voorkomen; emoji waarvan de naam of shortcode met de term begint, komen eerst.
    /// </summary>
    public static IReadOnlyList<EmojiInfo> Search(string query)
    {
        string[] words = query.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return Array.Empty<EmojiInfo>();
        }

        string first = words[0];
        return All
            .Where(e => words.All(w => HasWordStartingWith(e, w)))
            .Select(e => (Emoji: e, Rank: Rank(e, first)))
            .OrderBy(r => r.Rank)
            .Select(r => r.Emoji)
            .ToList();
    }

    private static bool HasWordStartingWith(EmojiInfo e, string word) =>
        e.Keywords.Contains("|" + word, StringComparison.Ordinal) || e.Keywords.Contains(" " + word, StringComparison.Ordinal);

    private static int Rank(EmojiInfo e, string word)
    {
        if (e.ShortName.StartsWith(word, StringComparison.OrdinalIgnoreCase) || e.Name.StartsWith(word, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        return e.Keywords.Contains("|" + word + "|", StringComparison.Ordinal) ? 1 : 2;
    }

    private static readonly ImageSource Placeholder = CreatePlaceholder();

    private static ImageSource CreatePlaceholder()
    {
        var image = new DrawingImage(new GeometryDrawing(Brushes.LightGray, null, new RectangleGeometry(new Rect(0, 0, CellSize, CellSize))));
        image.Freeze();
        return image;
    }

    private static BitmapSource LoadSheet()
    {
        var sheet = new BitmapImage();
        sheet.BeginInit();
        sheet.UriSource = new Uri(AssetRoot + "twemoji-64.png");
        sheet.CacheOption = BitmapCacheOption.OnLoad;
        sheet.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        sheet.EndInit();
        sheet.Freeze();
        return sheet;
    }

    private static Data LoadData()
    {
        var all = new List<EmojiInfo>();
        var byUnified = new Dictionary<string, EmojiInfo>(StringComparer.OrdinalIgnoreCase);

        using (System.IO.Stream stream = Application.GetResourceStream(new Uri(AssetRoot + "emoji.json")).Stream)
        using (JsonDocument doc = JsonDocument.Parse(stream))
        {
            foreach (JsonElement e in doc.RootElement.EnumerateArray())
            {
                Dictionary<SkinTone, (string, int, int)>? skins = null;
                if (e.TryGetProperty("t", out JsonElement t))
                {
                    skins = new Dictionary<SkinTone, (string, int, int)>();
                    foreach (JsonProperty p in t.EnumerateObject())
                    {
                        skins[(SkinTone)int.Parse(p.Name)] = (p.Value[0].GetString()!, p.Value[1].GetInt32(), p.Value[2].GetInt32());
                    }
                }

                var info = new EmojiInfo
                {
                    Unified = e.GetProperty("u").GetString()!,
                    Category = e.GetProperty("c").GetString()!,
                    SheetX = e.GetProperty("x").GetInt32(),
                    SheetY = e.GetProperty("y").GetInt32(),
                    ShortName = e.GetProperty("s").GetString()!,
                    Name = e.GetProperty("n").GetString()!,
                    Keywords = "|" + e.GetProperty("k").GetString()!,
                    Skins = skins
                };
                all.Add(info);
                byUnified[info.Unified] = info;
            }
        }

        return new Data(all, byUnified);
    }
}
