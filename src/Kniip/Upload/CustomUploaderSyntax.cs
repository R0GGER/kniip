using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;

namespace Kniip.Upload;

/// <summary>Gegevens waar de sjablonen van een custom uploader uit putten.</summary>
public sealed class SyntaxContext
{
    public string FileName { get; init; } = string.Empty;

    /// <summary>Tekst of URL bij tekst-uploaders en URL-verkorters; bij bestanden leeg.</summary>
    public string Input { get; init; } = string.Empty;

    public string? Response { get; init; }

    public string? ResponseUrl { get; init; }

    public HttpResponseMessage? ResponseMessage { get; init; }

    /// <summary>Codeert de uitkomst van functies, zodat die geldig in een JSON- of XML-body staan.</summary>
    public Func<string, string>? Encode { get; init; }
}

/// <summary>
/// De ShareX custom-uploadersyntax: <c>{functie:arg1|arg2}</c>, nestbaar, met <c>\</c> als escape voor <c>{ } | \</c>.
/// Daarnaast de dynamische waarden van ShareX zoals <c>%y</c> en <c>%mo</c> in de velden van het verzoek.
/// </summary>
public static class CustomUploaderSyntax
{
    private const char Escape = '\\';

    public static readonly string[] Functions =
    {
        "response", "responseurl", "header", "json", "xml", "regex", "input", "filename",
        "random", "select", "inputbox", "outputbox", "base64"
    };

    public static string Evaluate(string? template, SyntaxContext context)
    {
        if (string.IsNullOrEmpty(template))
        {
            return string.Empty;
        }

        int position = 0;
        return Parse(template, ref position, context, inArgument: false, depth: 0);
    }

    /// <summary>Velden van het verzoek: eerst de dynamische waarden (%y, %mo, ...), dan de syntax.</summary>
    public static string EvaluateRequest(string? template, SyntaxContext context) =>
        Evaluate(ReplaceDynamicValues(template ?? string.Empty), context);

    private static string Parse(string text, ref int position, SyntaxContext context, bool inArgument, int depth)
    {
        var result = new StringBuilder();
        while (position < text.Length)
        {
            char c = text[position];
            if (c == Escape && position + 1 < text.Length && text[position + 1] is '{' or '}' or '|' or Escape)
            {
                result.Append(text[position + 1]);
                position += 2;
                continue;
            }

            if (inArgument && c is '|' or '}')
            {
                break;
            }

            if (c == '{' && TryParseFunction(text, ref position, context, depth, out string value))
            {
                result.Append(depth == 0 && context.Encode != null ? context.Encode(value) : value);
                continue;
            }

            result.Append(c);
            position++;
        }

        return result.ToString();
    }

    private static bool TryParseFunction(string text, ref int position, SyntaxContext context, int depth, out string value)
    {
        value = string.Empty;
        int start = position;
        int nameStart = position + 1;
        int nameEnd = nameStart;
        while (nameEnd < text.Length && (char.IsLetterOrDigit(text[nameEnd]) || text[nameEnd] == '_'))
        {
            nameEnd++;
        }

        string name = text[nameStart..nameEnd].ToLowerInvariant();
        if (nameEnd >= text.Length || !Functions.Contains(name) || text[nameEnd] is not (':' or '}'))
        {
            return false;
        }

        var arguments = new List<string>();
        int cursor = nameEnd;
        if (text[cursor] == ':')
        {
            cursor++;
            while (true)
            {
                arguments.Add(Parse(text, ref cursor, context, inArgument: true, depth + 1));
                if (cursor >= text.Length)
                {
                    // Geen sluitaccolade: als gewone tekst laten staan.
                    position = start;
                    return false;
                }

                if (text[cursor] == '}')
                {
                    break;
                }

                cursor++;
            }
        }

        position = cursor + 1;
        value = Call(name, arguments, context);
        return true;
    }

    private static string Call(string name, IReadOnlyList<string> args, SyntaxContext context)
    {
        string Arg(int index) => index < args.Count ? args[index] : string.Empty;

        switch (name)
        {
            case "response":
                return context.Response ?? string.Empty;
            case "responseurl":
                return context.ResponseUrl ?? string.Empty;
            case "header":
                return ReadHeader(context.ResponseMessage, Arg(0));
            case "input":
                return context.Input;
            case "filename":
                return context.FileName;
            case "base64":
                return Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Join("|", args)));
            case "random":
                return args.Count == 0 ? string.Empty : args[RandomNumberGenerator.GetInt32(args.Count)];
            case "select":
                return args.Count switch
                {
                    0 => string.Empty,
                    1 => args[0],
                    _ => UploadPrompts.Select(args)
                };
            case "inputbox":
                return UploadPrompts.Input(Arg(0), Arg(1));
            case "outputbox":
                UploadPrompts.Output(args.Count > 1 ? args[0] : string.Empty, args.Count > 1 ? args[1] : Arg(0));
                return string.Empty;
            case "json":
                return args.Count > 1 ? ReadJson(args[0], args[1]) ?? string.Empty : ReadJson(context.Response, Arg(0)) ?? string.Empty;
            case "xml":
                return args.Count > 1 ? ReadXml(args[0], args[1]) : ReadXml(context.Response, Arg(0));
            case "regex":
                return args.Count switch
                {
                    >= 3 => ReadRegex(args[0], args[1], args[2]),
                    2 => ReadRegex(context.Response, args[0], args[1]),
                    _ => ReadRegex(context.Response, Arg(0), null)
                };
            default:
                return string.Empty;
        }
    }

    private static string ReadHeader(HttpResponseMessage? response, string name)
    {
        if (response == null || name.Length == 0)
        {
            return string.Empty;
        }

        if (response.Headers.TryGetValues(name, out IEnumerable<string>? values)
            || response.Content.Headers.TryGetValues(name, out values))
        {
            return string.Join(", ", values);
        }

        return string.Empty;
    }

    /// <summary>Een eenvoudig JsonPath: <c>files[0].url</c>, <c>$.data.link</c> of <c>['naam met punt']</c>.</summary>
    public static string? ReadJson(string? json, string path)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }

        using (document)
        {
            JsonElement current = document.RootElement;
            string trimmed = path.Trim();
            if (trimmed.StartsWith('$'))
            {
                trimmed = trimmed[1..];
            }

            foreach (Match part in Regex.Matches(trimmed, @"\[\s*'([^']*)'\s*\]|\[\s*""([^""]*)""\s*\]|\[\s*(-?\d+)\s*\]|([^.\[\]]+)"))
            {
                if (part.Groups[3].Success)
                {
                    int index = int.Parse(part.Groups[3].Value, CultureInfo.InvariantCulture);
                    if (current.ValueKind != JsonValueKind.Array)
                    {
                        return null;
                    }

                    int length = current.GetArrayLength();
                    if (index < 0)
                    {
                        index += length;
                    }

                    if (index < 0 || index >= length)
                    {
                        return null;
                    }

                    current = current[index];
                    continue;
                }

                string property = part.Groups[1].Success ? part.Groups[1].Value
                    : part.Groups[2].Success ? part.Groups[2].Value
                    : part.Groups[4].Value.Trim();
                if (current.ValueKind != JsonValueKind.Object || !TryGetProperty(current, property, out current))
                {
                    return null;
                }
            }

            return current.ValueKind switch
            {
                JsonValueKind.String => current.GetString(),
                JsonValueKind.Null or JsonValueKind.Undefined => null,
                _ => current.GetRawText()
            };
        }
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.TryGetProperty(name, out value))
        {
            return true;
        }

        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        return false;
    }

    private static string ReadXml(string? xml, string xpath)
    {
        if (string.IsNullOrWhiteSpace(xml) || xpath.Length == 0)
        {
            return string.Empty;
        }

        try
        {
            var document = new XmlDocument { XmlResolver = null };
            using (var reader = XmlReader.Create(new System.IO.StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit }))
            {
                document.Load(reader);
            }

            return document.SelectSingleNode(xpath)?.InnerText ?? string.Empty;
        }
        catch (Exception ex) when (ex is XmlException or System.Xml.XPath.XPathException)
        {
            return string.Empty;
        }
    }

    private static string ReadRegex(string? input, string pattern, string? group)
    {
        if (string.IsNullOrEmpty(input) || pattern.Length == 0)
        {
            return string.Empty;
        }

        Match match;
        try
        {
            match = Regex.Match(input, pattern, RegexOptions.None, TimeSpan.FromSeconds(2));
        }
        catch (Exception ex) when (ex is ArgumentException or RegexMatchTimeoutException)
        {
            return string.Empty;
        }

        if (!match.Success)
        {
            return string.Empty;
        }

        if (string.IsNullOrEmpty(group))
        {
            return match.Value;
        }

        return int.TryParse(group, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index)
            ? match.Groups[index].Value
            : match.Groups[group].Value;
    }

    // ---------------------------------------------------------------- Dynamische waarden

    private static readonly (string Token, Func<DateTime, string> Value)[] DynamicValues =
    {
        ("%unix", _ => DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)),
        ("%guid", _ => Guid.NewGuid().ToString()),
        ("%yy", now => now.ToString("yy", CultureInfo.InvariantCulture)),
        ("%mo", now => now.ToString("MM", CultureInfo.InvariantCulture)),
        ("%mi", now => now.ToString("mm", CultureInfo.InvariantCulture)),
        ("%ms", now => now.ToString("fff", CultureInfo.InvariantCulture)),
        ("%rn", _ => RandomNumberGenerator.GetInt32(10).ToString(CultureInfo.InvariantCulture)),
        ("%ra", _ => RandomAlphanumeric()),
        ("%y", now => now.ToString("yyyy", CultureInfo.InvariantCulture)),
        ("%d", now => now.ToString("dd", CultureInfo.InvariantCulture)),
        ("%h", now => now.ToString("HH", CultureInfo.InvariantCulture)),
        ("%s", now => now.ToString("ss", CultureInfo.InvariantCulture)),
    };

    private static string RandomAlphanumeric()
    {
        const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        return chars[RandomNumberGenerator.GetInt32(chars.Length)].ToString();
    }

    public static string ReplaceDynamicValues(string text)
    {
        if (!text.Contains('%'))
        {
            return text;
        }

        DateTime now = DateTime.Now;
        var result = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '%')
            {
                (string Token, Func<DateTime, string> Value) match = DynamicValues.FirstOrDefault(
                    v => string.CompareOrdinal(text, i, v.Token, 0, v.Token.Length) == 0);
                if (match.Token != null)
                {
                    result.Append(match.Value(now));
                    i += match.Token.Length - 1;
                    continue;
                }
            }

            result.Append(text[i]);
        }

        return result.ToString();
    }

    // ---------------------------------------------------------------- Escapes

    /// <summary>Maakt van gewone tekst een sjabloon dat precies die tekst oplevert.</summary>
    public static string EscapeLiteral(string text) =>
        Regex.Replace(text, @"[{}|\\]", m => "\\" + m.Value);

    public static string JsonEncode(string value) =>
        JsonSerializer.Serialize(value, new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping })[1..^1];

    public static string XmlEncode(string value) => System.Security.SecurityElement.Escape(value) ?? string.Empty;
}
