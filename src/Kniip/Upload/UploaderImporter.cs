using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Kniip.Resources;

namespace Kniip.Upload;

/// <summary>
/// Leest ShareX custom uploaders (.sxcu, ook het formaat van vóór ShareX 13) en de Shell Script-uploader van Zipline (.sh) in.
/// Het script wordt nooit uitgevoerd; alleen de gegevens uit het curl-commando worden overgenomen.
/// </summary>
public static class UploaderImporter
{
    public static CustomUploaderDestination FromFile(string path)
    {
        string text = File.ReadAllText(path);
        return Path.GetExtension(path).Equals(".sxcu", StringComparison.OrdinalIgnoreCase) || text.TrimStart().StartsWith('{')
            ? FromSxcu(text)
            : FromShellScript(text);
    }

    // ---------------------------------------------------------------- ShareX

    private static readonly string[] LegacyFunctions =
        { "json", "xml", "regex", "response", "responseurl", "header", "filename", "input", "random", "select", "prompt", "base64" };

    public static CustomUploaderDestination FromSxcu(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(Strings.Upload_InvalidShareX + "\n\n" + ex.Message);
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException(Strings.Upload_InvalidShareX);
            }

            string? destinationType = GetString(root, "DestinationType");
            if (destinationType is { Length: > 0 }
                && !destinationType.Contains("ImageUploader", StringComparison.OrdinalIgnoreCase)
                && !destinationType.Contains("FileUploader", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(string.Format(Strings.Upload_NotFileUploader, destinationType));
            }

            string? methodText = GetString(root, "RequestMethod") ?? GetString(root, "RequestType");
            CustomRequestMethod method = CustomRequestMethod.POST;
            if (methodText is { Length: > 0 } && !Enum.TryParse(methodText.Trim(), ignoreCase: true, out method))
            {
                throw new InvalidDataException(string.Format(Strings.Upload_UnsupportedMethod, methodText));
            }

            var config = new CustomUploaderDestination
            {
                Version = GetString(root, "Version") ?? string.Empty,
                DestinationType = destinationType is { Length: > 0 } ? destinationType : "ImageUploader, FileUploader",
                RequestMethod = method,
                RequestURL = GetString(root, "RequestURL")?.Trim() ?? string.Empty,
                FileFormName = GetString(root, "FileFormName")?.Trim() ?? string.Empty,
                Data = GetString(root, "Data") ?? string.Empty,
                URL = GetString(root, "URL") ?? string.Empty,
                ThumbnailURL = GetString(root, "ThumbnailURL") ?? string.Empty,
                DeletionURL = GetString(root, "DeletionURL") ?? string.Empty,
                ErrorMessage = GetString(root, "ErrorMessage") ?? string.Empty
            };
            ReadDictionary(root, "Parameters", config.Parameters);
            ReadDictionary(root, "Headers", config.Headers);
            ReadDictionary(root, "Arguments", config.Arguments);

            string? body = GetString(root, "Body");
            if (body is { Length: > 0 })
            {
                if (!Enum.TryParse(body.Trim(), ignoreCase: true, out CustomBodyType bodyType))
                {
                    throw new InvalidDataException(string.Format(Strings.Upload_UnsupportedBody, body));
                }

                config.Body = bodyType;
            }
            else if (config.FileFormName.Length > 0)
            {
                config.Body = CustomBodyType.MultipartFormData;
            }
            else if (method == CustomRequestMethod.GET)
            {
                // Vóór ShareX 13 werden de argumenten bij GET als querystring verstuurd.
                foreach ((string argument, string value) in config.Arguments)
                {
                    config.Parameters[argument] = value;
                }

                config.Arguments.Clear();
                config.Body = CustomBodyType.None;
            }
            else
            {
                config.Body = config.Arguments.Count > 0 ? CustomBodyType.FormURLEncoded : CustomBodyType.None;
            }

            if (IsLegacy(config))
            {
                ConvertLegacy(root, config);
            }

            if (config.Body == CustomBodyType.MultipartFormData && config.FileFormName.Length == 0)
            {
                config.FileFormName = "file";
            }

            config.Version = "17.0.0";
            config.Name = GetString(root, "Name") is { Length: > 0 } name ? name : config.DisplayName;
            return Validate(config);
        }
    }

    private static void ReadDictionary(JsonElement root, string name, Dictionary<string, string> target)
    {
        if (TryGetProperty(root, name, out JsonElement values) && values.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in values.EnumerateObject())
            {
                target[property.Name] = property.Value.ValueKind == JsonValueKind.String
                    ? property.Value.GetString() ?? string.Empty
                    : property.Value.GetRawText();
            }
        }
    }

    private static IEnumerable<string> Templates(CustomUploaderDestination config) =>
        new[] { config.RequestURL, config.Data, config.URL, config.ThumbnailURL, config.DeletionURL, config.ErrorMessage }
            .Concat(config.Parameters.Values).Concat(config.Headers.Values).Concat(config.Arguments.Values);

    /// <summary>
    /// Vóór ShareX 13 was de syntax <c>$json:pad$</c> in plaats van <c>{json:pad}</c>. Zonder versienummer
    /// beslist de syntax die in het bestand staat.
    /// </summary>
    private static bool IsLegacy(CustomUploaderDestination config)
    {
        if (Version.TryParse(config.Version, out Version? version))
        {
            return version.Major < 13;
        }

        string all = string.Join("\n", Templates(config));
        string names = string.Join("|", LegacyFunctions);
        return Regex.IsMatch(all, @"\$(" + names + @")(:[^$]*)?\$", RegexOptions.IgnoreCase)
               && !Regex.IsMatch(all, @"\{(" + string.Join("|", CustomUploaderSyntax.Functions) + @")[:}]", RegexOptions.IgnoreCase);
    }

    private static void ConvertLegacy(JsonElement root, CustomUploaderDestination config)
    {
        var regexList = new List<string>();
        if (TryGetProperty(root, "RegexList", out JsonElement list) && list.ValueKind == JsonValueKind.Array)
        {
            regexList.AddRange(list.EnumerateArray().Select(r => r.ValueKind == JsonValueKind.String ? r.GetString() ?? string.Empty : string.Empty));
        }

        string Convert(string text) => ConvertLegacySyntax(text, regexList);

        config.RequestURL = Convert(config.RequestURL);
        config.Data = Convert(config.Data);
        config.URL = Convert(config.URL);
        config.ThumbnailURL = Convert(config.ThumbnailURL);
        config.DeletionURL = Convert(config.DeletionURL);
        config.ErrorMessage = Convert(config.ErrorMessage);
        foreach (Dictionary<string, string> values in new[] { config.Parameters, config.Headers, config.Arguments })
        {
            foreach (string key in values.Keys.ToList())
            {
                values[key] = Convert(values[key]);
            }
        }

        if (config.URL.Length == 0 && GetString(root, "ResponseType") is { } responseType)
        {
            if (responseType.Equals("RedirectionURL", StringComparison.OrdinalIgnoreCase))
            {
                config.URL = "{responseurl}";
            }
            else if (responseType.Equals("LocationHeader", StringComparison.OrdinalIgnoreCase))
            {
                config.URL = "{header:Location}";
            }
        }
    }

    /// <summary>Zet <c>$naam:args$</c> om naar <c>{naam:args}</c>; <c>$regex:1,2$</c> verwijst naar de RegexList.</summary>
    internal static string ConvertLegacySyntax(string text, IReadOnlyList<string> regexList)
    {
        if (text.Length == 0)
        {
            return text;
        }

        var result = new StringBuilder();
        int last = 0;
        string pattern = @"\$(" + string.Join("|", LegacyFunctions) + @")(?::([^$]*))?\$";
        foreach (Match match in Regex.Matches(text, pattern, RegexOptions.IgnoreCase))
        {
            result.Append(EscapeLegacyLiteral(text[last..match.Index]));
            last = match.Index + match.Length;

            string name = match.Groups[1].Value.ToLowerInvariant();
            string args = match.Groups[2].Success ? match.Groups[2].Value : string.Empty;
            if (name == "prompt")
            {
                name = "inputbox";
            }

            if (name == "regex")
            {
                string[] parts = args.Split(new[] { ',', '|' }, 2);
                if (int.TryParse(parts[0].Trim(), out int index) && index >= 1 && index <= regexList.Count)
                {
                    string group = parts.Length > 1 ? "|" + parts[1].Trim() : string.Empty;
                    result.Append("{regex:").Append(CustomUploaderSyntax.EscapeLiteral(regexList[index - 1])).Append(group).Append('}');
                }

                continue;
            }

            result.Append('{').Append(name);
            if (args.Length > 0)
            {
                result.Append(':').Append(EscapeLegacyLiteral(args));
            }

            result.Append('}');
        }

        result.Append(EscapeLegacyLiteral(text[last..]));
        return result.ToString();
    }

    /// <summary>Accolades en backslashes waren vroeger gewone tekst; het sluisteken bleef ook in de argumenten een scheidingsteken.</summary>
    private static string EscapeLegacyLiteral(string text) =>
        Regex.Replace(text, @"[{}\\]", m => "\\" + m.Value);

    // ---------------------------------------------------------------- Export

    public static string ToSxcu(CustomUploaderDestination config)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
               {
                   Indented = true,
                   Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
               }))
        {
            writer.WriteStartObject();
            writer.WriteString("Version", "17.0.0");
            writer.WriteString("Name", config.Name);
            writer.WriteString("DestinationType", config.DestinationType);
            writer.WriteString("RequestMethod", config.RequestMethod.ToString());
            writer.WriteString("RequestURL", config.RequestURL);
            WriteDictionary(writer, "Parameters", config.Parameters);
            WriteDictionary(writer, "Headers", config.Headers);
            writer.WriteString("Body", config.Body.ToString());
            if (config.Body is CustomBodyType.MultipartFormData or CustomBodyType.FormURLEncoded)
            {
                WriteDictionary(writer, "Arguments", config.Arguments);
            }

            if (config.Body == CustomBodyType.MultipartFormData)
            {
                writer.WriteString("FileFormName", config.FileFormName);
            }

            if (config.Body is CustomBodyType.JSON or CustomBodyType.XML)
            {
                writer.WriteString("Data", config.Data);
            }

            WriteIfSet(writer, "URL", config.URL);
            WriteIfSet(writer, "ThumbnailURL", config.ThumbnailURL);
            WriteIfSet(writer, "DeletionURL", config.DeletionURL);
            WriteIfSet(writer, "ErrorMessage", config.ErrorMessage);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteDictionary(Utf8JsonWriter writer, string name, Dictionary<string, string> values)
    {
        if (values.Count == 0)
        {
            return;
        }

        writer.WriteStartObject(name);
        foreach ((string key, string value) in values)
        {
            writer.WriteString(key, value);
        }

        writer.WriteEndObject();
    }

    private static void WriteIfSet(Utf8JsonWriter writer, string name, string value)
    {
        if (value.Length > 0)
        {
            writer.WriteString(name, value);
        }
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

    public static CustomUploaderDestination FromShellScript(string script)
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
            throw new InvalidDataException(Strings.Upload_NoCurl);
        }

        Match curl = Regex.Match(curlLine, @"(^|[\s(`|;])curl\s");
        List<string> tokens = Tokenize(curlLine[(curl.Index + curl.Length)..], variables);

        var config = new CustomUploaderDestination { FileFormName = string.Empty, URL = "{json:files[0].url}" };
        for (int i = 0; i < tokens.Count; i++)
        {
            string token = tokens[i];
            string? next = i + 1 < tokens.Count ? tokens[i + 1] : null;
            switch (token)
            {
                case "-X" or "--request" when next != null:
                    if (Enum.TryParse(next.Trim(), ignoreCase: true, out CustomRequestMethod method))
                    {
                        config.RequestMethod = method;
                    }

                    i++;
                    break;
                case "-H" or "--header" when next != null:
                    AddHeader(config, next);
                    i++;
                    break;
                case "-F" or "--form" when next != null:
                    AddFormField(config, next);
                    i++;
                    break;
                case "--url" when next != null:
                    config.RequestURL = next;
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
                    else if (config.RequestURL.Length == 0
                             && (token.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                                 || token.StartsWith("http://", StringComparison.OrdinalIgnoreCase)))
                    {
                        config.RequestURL = token;
                    }

                    break;
            }
        }

        if (config.FileFormName.Length == 0)
        {
            throw new InvalidDataException(
                Strings.Upload_ScriptNotFileUploader);
        }

        Match jq = Regex.Match(text, @"\bjq\s+(?:-\w+\s+)*['""]?\.?([A-Za-z0-9_\[\]\.]+)['""]?");
        if (jq.Success && jq.Groups[1].Value.Length > 0)
        {
            config.URL = "{json:" + jq.Groups[1].Value + "}";
        }

        config.Name = config.DisplayName;
        return Validate(config);
    }

    private static void AddHeader(CustomUploaderDestination config, string header)
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

        config.Headers[name] = CustomUploaderSyntax.EscapeLiteral(header[(colon + 1)..].Trim());
    }

    private static void AddFormField(CustomUploaderDestination config, string field)
    {
        int equals = field.IndexOf('=');
        if (equals <= 0)
        {
            return;
        }

        string name = field[..equals].Trim();
        string value = field[(equals + 1)..];
        if (value.StartsWith('@'))
        {
            config.FileFormName = name;
        }
        else
        {
            config.Arguments[name] = CustomUploaderSyntax.EscapeLiteral(value);
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

    private static CustomUploaderDestination Validate(CustomUploaderDestination config)
    {
        if (!config.IsConfigured)
        {
            throw new InvalidDataException(config.RequestURL.Length == 0
                ? Strings.Upload_NoServer
                : string.Format(Strings.Upload_BadServer, config.RequestURL));
        }

        return config;
    }
}
