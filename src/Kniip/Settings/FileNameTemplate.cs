using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Kniip.Resources;

namespace Kniip.Settings;

/// <summary>
/// Naamsjabloon voor opslaan: %J of %Y jaar, %M maand, %D dag, %V programmanaam van het venster,
/// %Rn n willekeurige tekens, %Nn eerste vrije nummer in de map met n cijfers. / of \ maakt submappen.
/// </summary>
public static class FileNameTemplate
{
    private const int MaxWidth = 32;
    private const string RandomChars = "abcdefghijklmnopqrstuvwxyz0123456789";

    private static readonly Regex Token = new(@"%(?:(?<k>[JYMDV])|(?<k>[RN])(?<n>\d{1,2}))",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static string Help => Strings.Template_Help;

    /// <summary>
    /// Volledig pad in <paramref name="folder"/> dat nog niet bestaat; de submappen worden aangemaakt.
    /// Een leeg sjabloon geeft <paramref name="defaultName"/>.
    /// </summary>
    public static string Resolve(string folder, string? template, string windowName, string defaultName, string extension)
    {
        var randoms = new Dictionary<int, string>();
        DateTime now = DateTime.Now;
        bool counts = HasCounter(template);
        for (int number = 1; ; number++)
        {
            List<string> parts = Parts(template, windowName, defaultName, now, number, randoms);
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
    public static string Preview(string? template, string windowName, string defaultName, string extension) =>
        string.Join(Path.DirectorySeparatorChar, Parts(template, windowName, defaultName, DateTime.Now, 1, new Dictionary<int, string>())) + extension;

    /// <summary>Alleen de bestandsnaam zonder extensie, als voorstel in een opslagvenster.</summary>
    public static string SuggestedName(string? template, string windowName, string defaultName) =>
        Parts(template, windowName, defaultName, DateTime.Now, 1, new Dictionary<int, string>())[^1];

    private static bool HasCounter(string? template) =>
        !string.IsNullOrWhiteSpace(template)
        && Token.Matches(template).Any(m => char.ToUpperInvariant(m.Groups["k"].Value[0]) == 'N');

    /// <summary>Opgeschoonde mapnamen gevolgd door de bestandsnaam zonder extensie.</summary>
    private static List<string> Parts(string? template, string windowName, string defaultName, DateTime now, int number, Dictionary<int, string> randoms)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(template))
        {
            string expanded = Token.Replace(template, m => Value(m, windowName, now, number, randoms));
            parts.AddRange(expanded.Split('/', '\\').Select(Clean).Where(p => p.Length > 0));
        }

        if (parts.Count == 0 || template!.TrimEnd().EndsWith('/') || template.TrimEnd().EndsWith('\\'))
        {
            parts.Add(Clean(defaultName));
        }

        return parts;
    }

    private static string Value(Match match, string windowName, DateTime now, int number, Dictionary<int, string> randoms)
    {
        int width = match.Groups["n"].Success ? Math.Min(int.Parse(match.Groups["n"].Value, CultureInfo.InvariantCulture), MaxWidth) : 0;
        switch (char.ToUpperInvariant(match.Groups["k"].Value[0]))
        {
            case 'J':
            case 'Y':
                return now.Year.ToString(CultureInfo.InvariantCulture);
            case 'M':
                return now.Month.ToString("00", CultureInfo.InvariantCulture);
            case 'D':
                return now.Day.ToString("00", CultureInfo.InvariantCulture);
            case 'V':
                return windowName.Replace('/', '_').Replace('\\', '_');
            case 'R':
                if (!randoms.TryGetValue(match.Index, out string? random))
                {
                    random = new string(Enumerable.Range(0, width).Select(_ => RandomChars[RandomNumberGenerator.GetInt32(RandomChars.Length)]).ToArray());
                    randoms[match.Index] = random;
                }

                return random;
            default:
                return number.ToString(CultureInfo.InvariantCulture).PadLeft(width, '0');
        }
    }

    private static string Clean(string segment)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string cleaned = new(segment.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return cleaned.Trim().TrimEnd('.').Trim();
    }
}
