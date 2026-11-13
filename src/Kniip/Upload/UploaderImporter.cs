using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Kniip.Upload;

/// <summary>
/// Leest de uploaders die Zipline genereert (ShareX .sxcu en Shell Script .sh) in.
/// Het script wordt nooit uitgevoerd; alleen de gegevens uit het curl-commando worden overgenomen.
/// </summary>
public static class UploaderImporter
{
    public static UploaderConfig FromFile(string path)
    {
        string text = File.ReadAllText(path);
        return Path.GetExtension(path).Equals(".sxcu", StringComparison.OrdinalIgnoreCase) || text.TrimStart().StartsWith('{')
            ? FromSxcu(text)
            : FromShellScript(text);
    }

    // ---------------------------------------------------------------- ShareX

    public static UploaderConfig FromSxcu(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Dit is geen geldig ShareX-uploaderbestand.\n\n{ex.Message}");
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException("Dit is geen geldig ShareX-uploaderbestand.");
            }

            string? body = GetString(root, "Body");
            if (body != null && !body.Equals("MultipartFormData", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Deze uploader verstuurt geen bestanden (misschien is het de URL-verkorter). Kies in Zipline de uploader voor bestanden.");
            }

            string? method = GetString(root, "RequestMethod");
            if (method != null && !method.Equals("POST", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Deze uploader gebruikt {method}; kniip ondersteunt alleen POST.");
            }

            var config = new UploaderConfig
            {
                RequestUrl = GetString(root, "RequestURL")?.Trim() ?? string.Empty,
                FileFormName = GetString(root, "FileFormName") is { Length: > 0 } field ? field : "file",
                UrlPath = ExtractJsonPath(GetString(root, "URL")) ?? "files[0].url"
            };

            if (TryGetProperty(root, "Headers", out JsonElement headers) && headers.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty header in headers.EnumerateObject())
                {
                    config.Headers[header.Name] = header.Value.ValueKind == JsonValueKind.String
                        ? header.Value.GetString() ?? string.Empty
                        : header.Value.GetRawText();
                }
            }

            config.Name = GetString(root, "Name") is { Length: > 0 } name ? name : config.DisplayName;
            return Validate(config);
        }
    }

    /// <summary>Zet <c>{json:files[0].url}</c> (of het oudere <c>$json:files[0].url$</c>) om naar <c>files[0].url</c>.</summary>
    private static string? ExtractJsonPath(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        Match match = Regex.Match(url, @"\{json:([^}]+)\}|\$json:([^$]+)\$");
        if (!match.Success)
        {
            return null;
        }

        string path = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
        return path.Trim().TrimStart('.');
    }

    private static string? GetString(JsonElement obj, string name) =>
        TryGetProperty(obj, name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool TryGetProperty(JsonElement obj, string name, out JsonElement value)
    {
        foreach (JsonProperty property in obj.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    // ---------------------------------------------------------------- Shellscript

    public static UploaderConfig FromShellScript(string script)
    {
        string text = Regex.Replace(script, @"\\\r?\n", " ");
        string[] lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();

        var variables = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string line in lines)
        {
            Match assignment = Regex.Match(line, @"^\s*(?:export\s+|local\s+|readonly\s+)?([A-Za-z_][A-Za-z0-9_]*)=(.*)$");
            if (assignment.Success && !assignment.Groups[2].Value.TrimStart().StartsWith("$("))
            {
                List<string> value = Tokenize(assignment.Groups[2].Value, variables);
                variables[assignment.Groups[1].Value] = value.FirstOrDefault() ?? string.Empty;
            }
        }

        string? curlLine = lines.FirstOrDefault(l => Regex.IsMatch(l, @"(^|[\s(`|;])curl\s") && !l.TrimStart().StartsWith('#'));
        if (curlLine == null)
        {
            throw new InvalidDataException("In dit script staat geen curl-commando. Kies in Zipline de Shell Script-uploader voor bestanden.");
        }

        Match curl = Regex.Match(curlLine, @"(^|[\s(`|;])curl\s");
        List<string> tokens = Tokenize(curlLine[(curl.Index + curl.Length)..], variables);

        var config = new UploaderConfig { FileFormName = string.Empty };
        for (int i = 0; i < tokens.Count; i++)
        {
            string token = tokens[i];
            string? next = i + 1 < tokens.Count ? tokens[i + 1] : null;
            switch (token)
            {
                case "-H" or "--header" when next != null:
                    AddHeader(config, next);
                    i++;
                    break;
                case "-F" or "--form" when next != null:
                    AddFormField(config, next);
                    i++;
                    break;
                case "--url" when next != null:
                    config.RequestUrl = next;
                    i++;
                    break;
                default:
                    if (token.StartsWith("-H", StringComparison.Ordinal) && token.Length > 2)
                    {
                        AddHeader(config, token[2..]);
                    }
                    else if (token.StartsWith("-F", StringComparison.Ordinal) && token.Length > 2)
                    {
                        AddFormField(config, token[2..]);
                    }
                    else if (config.RequestUrl.Length == 0
                             && (token.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                                 || token.StartsWith("http://", StringComparison.OrdinalIgnoreCase)))
                    {
                        config.RequestUrl = token;
                    }

                    break;
            }
        }

        if (config.FileFormName.Length == 0)
        {
            throw new InvalidDataException(
                "Dit script uploadt geen bestand (misschien is het de URL-verkorter). Kies in Zipline de Shell Script-uploader voor bestanden.");
        }

        Match jq = Regex.Match(text, @"\bjq\s+(?:-\w+\s+)*['""]?\.?([A-Za-z0-9_\[\]\.]+)['""]?");
        if (jq.Success && jq.Groups[1].Value.Length > 0)
        {
            config.UrlPath = jq.Groups[1].Value;
        }

        config.Name = config.DisplayName;
        return Validate(config);
    }

    private static void AddHeader(UploaderConfig config, string header)
    {
        int colon = header.IndexOf(':');
        if (colon <= 0)
        {
            return;
        }

        string name = header[..colon].Trim();
        if (name.Equals("content-type", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        config.Headers[name] = header[(colon + 1)..].Trim();
    }

    private static void AddFormField(UploaderConfig config, string field)
    {
        int equals = field.IndexOf("=@", StringComparison.Ordinal);
        if (equals > 0)
        {
            config.FileFormName = field[..equals].Trim();
        }
    }

    /// <summary>
    /// Splitst een shellregel in woorden zoals bash dat doet (aanhalingstekens, escapes en bekende variabelen).
    /// Stopt bij een pipe, puntkomma, sluithaakje of omleiding buiten aanhalingstekens.
    /// </summary>
    private static List<string> Tokenize(string line, IReadOnlyDictionary<string, string> variables)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        bool inToken = false;
        char quote = '\0';

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (quote == '\'')
            {
                if (c == '\'')
                {
                    quote = '\0';
                }
                else
                {
                    current.Append(c);
                }

                continue;
            }

            if (c == '\\' && i + 1 < line.Length)
            {
                char escaped = line[++i];
                if (quote == '"' && escaped is not ('"' or '\\' or '$' or '`'))
                {
                    current.Append('\\');
                }

                current.Append(escaped);
                inToken = true;
                continue;
            }

            if (c == '$' && i + 1 < line.Length)
            {
                Match variable = Regex.Match(line[(i + 1)..], @"^(?:\{([A-Za-z_][A-Za-z0-9_]*)\}|([A-Za-z_][A-Za-z0-9_]*))");
                if (variable.Success)
                {
                    string name = variable.Groups[1].Success ? variable.Groups[1].Value : variable.Groups[2].Value;
                    current.Append(variables.TryGetValue(name, out string? value) ? value : "$" + variable.Value);
                    i += variable.Length;
                    inToken = true;
                    continue;
                }
            }

            if (quote == '"')
            {
                if (c == '"')
                {
                    quote = '\0';
                }
                else
                {
                    current.Append(c);
                }

                continue;
            }

            if (c is '\'' or '"')
            {
                quote = c;
                inToken = true;
                continue;
            }

            if (c is '|' or ';' or ')' or '>' or '<' or '&' or '`' or '#' && !(c == '#' && inToken))
            {
                break;
            }

            if (char.IsWhiteSpace(c))
            {
                if (inToken)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                    inToken = false;
                }

                continue;
            }

            current.Append(c);
            inToken = true;
        }

        if (inToken)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }

    private static UploaderConfig Validate(UploaderConfig config)
    {
        if (!config.IsConfigured)
        {
            throw new InvalidDataException(config.RequestUrl.Length == 0
                ? "In dit bestand staat geen server-adres."
                : $"Het server-adres is niet bruikbaar:\n{config.RequestUrl}");
        }

        if (!config.Headers.Keys.Any(k => k.Equals("authorization", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException("In dit bestand staat geen token (authorization-header).");
        }

        return config;
    }
}
