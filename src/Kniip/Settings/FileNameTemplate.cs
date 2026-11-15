using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Kniip.Resources;

namespace Kniip.Settings;

/// <summary>Programmanaam en titel van het geknipte of opgenomen venster.</summary>
public sealed record WindowInfo(string ProcessName, string Title);

public enum TemplateCategory
{
    Window,
    Time,
    Incremental,
    Computer
}

/// <summary>Een token voor de Invoegen-dropdown: <see cref="Insert"/> komt in het veld, de uitleg uit Strings.</summary>
public sealed record TemplateToken(string Code, string Insert, TemplateCategory Category, string HelpKey)
{
    public string Help => Strings.ResourceManager.GetString(HelpKey, Strings.Culture) ?? HelpKey;

    public string Sample => FileNameTemplate.SampleValue(this);
}

/// <summary>
/// Naamsjabloon voor opslaan: een submap- en een bestandsnaampatroon met tokens (zie <see cref="Tokens"/>).
/// De nieuwe tokens zijn kleine letters en hoofdlettergevoelig; de oude (%J of %Y jaar, %M maand, %D dag,
/// %V programmanaam, %Rn willekeurige tekens, %Nn oplopend nummer) blijven werken.
/// / of \ maakt submappen.
/// </summary>
public static class FileNameTemplate
{
    private const int MaxWidth = 32;
    private const string AlphaNumeric = "abcdefghijklmnopqrstuvwxyz0123456789";
    private const string Unambiguous = "abcdefghjkmnpqrstuvwxyz23456789";
    private const string Digits = "0123456789";
    private const string Hex = "0123456789abcdef";

    public static readonly WindowInfo SampleWindow = new("Firefox", "Firefox - kniip.com");

    public static IReadOnlyList<TemplateToken> Tokens { get; } = new TemplateToken[]
    {
        new("%t", "%t", TemplateCategory.Window, "Token_Title"),
        new("%pn", "%pn", TemplateCategory.Window, "Token_ProcessName"),
        new("%yy", "%yy", TemplateCategory.Time, "Token_Year"),
        new("%y", "%y", TemplateCategory.Time, "Token_YearShort"),
        new("%m", "%m", TemplateCategory.Time, "Token_Month"),
        new("%mm", "%mm", TemplateCategory.Time, "Token_MonthName"),
        new("%wy", "%wy", TemplateCategory.Time, "Token_Week"),
        new("%d", "%d", TemplateCategory.Time, "Token_Day"),
        new("%h", "%h", TemplateCategory.Time, "Token_Hour12"),
        new("%hh", "%hh", TemplateCategory.Time, "Token_Hour24"),
        new("%mi", "%mi", TemplateCategory.Time, "Token_Minutes"),
        new("%s", "%s", TemplateCategory.Time, "Token_Seconds"),
        new("%ms", "%ms", TemplateCategory.Time, "Token_Milliseconds"),
        new("%n", "%n{3}", TemplateCategory.Incremental, "Token_Counter"),
        new("%rn", "%rn{4}", TemplateCategory.Incremental, "Token_RandomDigit"),
        new("%ra", "%ra{6}", TemplateCategory.Incremental, "Token_RandomAlphaNumeric"),
        new("%rna", "%rna{6}", TemplateCategory.Incremental, "Token_RandomUnambiguous"),
        new("%rx", "%rx{8}", TemplateCategory.Incremental, "Token_RandomHex"),
        new("%guid", "%guid", TemplateCategory.Incremental, "Token_Guid"),
        new("%un", "%un", TemplateCategory.Computer, "Token_UserName"),
        new("%uln", "%uln", TemplateCategory.Computer, "Token_Domain"),
        new("%cn", "%cn", TemplateCategory.Computer, "Token_MachineName"),
    };

    /// <summary>
    /// Eerst de oude %Rn/%Nn (zodat %n3 geen %n plus 3 wordt), dan de nieuwe tokens (langste eerst, met optioneel {n}),
    /// en dan de oude %J %Y %M %D %V als hoofdletter; %j en %v ook als kleine letter.
    /// </summary>
    private static readonly Regex Token = new(
        @"%(?:(?<old>[RrNn])(?<n>\d{1,2})"
        + @"|(?<code>" + string.Join("|", Tokens.Select(t => t.Code[1..]).OrderByDescending(c => c.Length)) + @")(?:\{(?<n>\d{1,2})\})?"
        + @"|(?<old>[JYMDVjv]))",
        RegexOptions.CultureInvariant);

    /// <summary>
    /// Volledig pad in <paramref name="folder"/> dat nog niet bestaat; de submappen worden aangemaakt.
    /// Een leeg bestandsnaampatroon geeft <paramref name="defaultName"/>.
    /// </summary>
    public static string Resolve(string folder, string? folderTemplate, string? fileTemplate, WindowInfo window, string defaultName, string extension)
    {
        string template = Combine(folderTemplate, fileTemplate);
        var cache = new Dictionary<int, string>();
        DateTime now = DateTime.Now;
        bool counts = Token.Matches(template).Any(IsCounter);
        for (int number = 1; ; number++)
        {
            List<string> parts = Parts(template, window, defaultName, now, number, cache);
            string directory = Path.Combine(new[] { folder }.Concat(parts.SkipLast(1)).ToArray());
            Directory.CreateDirectory(directory);
            string fileName = parts[^1] + extension;
            if (!counts)
            {
                return SaveLocation.UniquePath(directory, fileName);
            }

            string path = Path.Combine(directory, fileName);
            if (!File.Exists(path))
            {
                return path;
            }
        }
    }

    /// <summary>Voorbeeld van het pad onder de opslagmap, zonder naar bestaande bestanden te kijken.</summary>
    public static string Preview(string? folderTemplate, string? fileTemplate, WindowInfo window, string defaultName, string extension) =>
        string.Join(Path.DirectorySeparatorChar,
            Parts(Combine(folderTemplate, fileTemplate), window, defaultName, DateTime.Now, 1, new Dictionary<int, string>())) + extension;

    /// <summary>Alleen de bestandsnaam zonder extensie, als voorstel in een opslagvenster.</summary>
    public static string SuggestedName(string? fileTemplate, WindowInfo window, string defaultName) =>
        Parts(Combine(null, fileTemplate), window, defaultName, DateTime.Now, 1, new Dictionary<int, string>())[^1];

    internal static string SampleValue(TemplateToken token)
    {
        Match match = Token.Match(token.Insert);
        return Value(match, SampleWindow, DateTime.Now, 1, new Dictionary<int, string>());
    }

    /// <summary>Submap en bestandsnaam als één sjabloon; eindigt het op /, dan volgt de standaardnaam.</summary>
    private static string Combine(string? folderTemplate, string? fileTemplate) =>
        $"{folderTemplate?.Trim()}/{fileTemplate?.Trim()}";

    private static bool IsCounter(Match match) =>
        match.Groups["code"].Value == "n" || match.Groups["old"].Value is "N" or "n";

    /// <summary>Opgeschoonde mapnamen gevolgd door de bestandsnaam zonder extensie.</summary>
    private static List<string> Parts(string template, WindowInfo window, string defaultName, DateTime now, int number, Dictionary<int, string> cache)
    {
        string expanded = Token.Replace(template, m => Value(m, window, now, number, cache));
        List<string> parts = expanded.Split('/', '\\').Select(Clean).Where(p => p.Length > 0).ToList();
        if (parts.Count == 0 || template.EndsWith('/') || template.EndsWith('\\'))
        {
            parts.Add(Clean(defaultName));
        }

        return parts;
    }

    private static string Value(Match match, WindowInfo window, DateTime now, int number, Dictionary<int, string> cache)
    {
        int? width = match.Groups["n"].Success ? Math.Min(int.Parse(match.Groups["n"].Value, CultureInfo.InvariantCulture), MaxWidth) : null;
        string code = match.Groups["code"].Success ? match.Groups["code"].Value : OldCode(match.Groups["old"].Value);
        return code switch
        {
            "t" => PathSafe(window.Title),
            "pn" => PathSafe(window.ProcessName),
            "yy" => now.ToString("yyyy", CultureInfo.InvariantCulture),
            "y" => now.ToString("yy", CultureInfo.InvariantCulture),
            "m" => now.ToString("MM", CultureInfo.InvariantCulture),
            "mm" => CultureInfo.CurrentUICulture.DateTimeFormat.GetMonthName(now.Month),
            "wy" => ISOWeek.GetWeekOfYear(now).ToString("00", CultureInfo.InvariantCulture),
            "d" => now.ToString("dd", CultureInfo.InvariantCulture),
            "h" => now.ToString("hhtt", CultureInfo.InvariantCulture).ToLowerInvariant(),
            "hh" => now.ToString("HH", CultureInfo.InvariantCulture),
            "mi" => now.ToString("mm", CultureInfo.InvariantCulture),
            "s" => now.ToString("ss", CultureInfo.InvariantCulture),
            "ms" => now.ToString("fff", CultureInfo.InvariantCulture),
            "n" => number.ToString(CultureInfo.InvariantCulture).PadLeft(width ?? 1, '0'),
            "rn" => Cached(match, cache, () => Random(Digits, width ?? 1)),
            "ra" => Cached(match, cache, () => Random(AlphaNumeric, width ?? 1)),
            "rna" => Cached(match, cache, () => Random(Unambiguous, width ?? 1)),
            "rx" => Cached(match, cache, () => Random(Hex, width ?? 1)),
            "guid" => Cached(match, cache, () => Guid.NewGuid().ToString("D")),
            "un" => Environment.UserName,
            "uln" => Environment.UserDomainName,
            "cn" => Environment.MachineName,
            _ => match.Value
        };
    }

    /// <summary>Oude tokens vertaald naar de nieuwe; %Rn gebruikt de oude tekenset van letters en cijfers.</summary>
    private static string OldCode(string old) => old switch
    {
        "J" or "j" or "Y" => "yy",
        "M" => "m",
        "D" => "d",
        "V" or "v" => "pn",
        "R" or "r" => "ra",
        _ => "n"
    };

    private static string Cached(Match match, Dictionary<int, string> cache, Func<string> create)
    {
        if (!cache.TryGetValue(match.Index, out string? value))
        {
            value = create();
            cache[match.Index] = value;
        }

        return value;
    }

    private static string Random(string chars, int length) =>
        new(Enumerable.Range(0, length).Select(_ => chars[RandomNumberGenerator.GetInt32(chars.Length)]).ToArray());

    private static string PathSafe(string text) => text.Replace('/', '_').Replace('\\', '_');

    private static string Clean(string segment)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string cleaned = new(segment.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return cleaned.Trim().TrimEnd('.').Trim();
    }
}
