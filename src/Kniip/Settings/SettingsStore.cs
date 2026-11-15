using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media;
using Kniip.Infrastructure;
using Kniip.Upload;

namespace Kniip.Settings;

public static class SettingsStore
{
    public static string Folder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppIdentity.Key);

    private static string FilePath => Path.Combine(Folder, "settings.json");

    /// <summary>
    /// Startpunt als er nog geen instellingen zijn: voor kniip die van voor de hernoeming van Knip+,
    /// voor de testbuild een kopie van die van de gewone kniip.
    /// </summary>
    private static string LegacyFilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppIdentity.IsTest ? "Kniip" : "KnipPlus", "settings.json");

    internal static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(), new ColorJsonConverter() }
    };

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath) && File.Exists(LegacyFilePath))
            {
                Directory.CreateDirectory(Folder);
                File.Copy(LegacyFilePath, FilePath);
            }

            if (File.Exists(FilePath))
            {
                AppSettings settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options) ?? new AppSettings();
                MigrateTemplates(settings);
                MigrateUploaders(settings);
                return settings;
            }
        }
        catch (Exception)
        {
            // Beschadigd bestand: met standaardwaarden verder.
        }

        return new AppSettings();
    }

    /// <summary>
    /// Van vóór de aparte submapvelden stonden de mappen in het naamsjabloon: alles vóór de laatste / of \
    /// gaat naar het submappatroon, zodat het pad gelijk blijft.
    /// </summary>
    private static void MigrateTemplates(AppSettings settings)
    {
        if (settings.SnipFolderTemplate is null)
        {
            (settings.SnipFolderTemplate, settings.SnipFileNameTemplate) = SplitTemplate(settings.SnipFileNameTemplate);
        }

        if (settings.VideoFolderTemplate is null)
        {
            (settings.VideoFolderTemplate, settings.VideoFileNameTemplate) = SplitTemplate(settings.VideoFileNameTemplate);
        }
    }

    /// <summary>De ene Zipline-uploader van vroeger wordt een custom uploader in de lijst, en de standaard.</summary>
    private static void MigrateUploaders(AppSettings settings)
    {
        settings.Uploaders.RemoveAll(u => u is null);
        if (settings.LegacyUploader is not { } legacy)
        {
            return;
        }

        settings.LegacyUploader = null;
        if (legacy.RequestUrl.Length == 0)
        {
            return;
        }

        var custom = new CustomUploaderDestination
        {
            Name = legacy.Name,
            RequestURL = legacy.RequestUrl,
            FileFormName = legacy.FileFormName.Length > 0 ? legacy.FileFormName : "file",
            URL = "{json:" + (legacy.UrlPath.Length > 0 ? legacy.UrlPath : "files[0].url") + "}"
        };
        foreach ((string name, string value) in legacy.Headers)
        {
            custom.Headers[name] = value;
        }

        settings.Uploaders.Insert(0, custom);
        settings.DefaultUploaderId = custom.Id;
    }

    /// <summary>Losse kopie, bijvoorbeeld om een bestemming te bewerken zonder het origineel te raken.</summary>
    public static T DeepCopy<T>(T value)
    {
        ProtectedHeadersConverter.WritePlain = true;
        try
        {
            return JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Options), Options)!;
        }
        finally
        {
            ProtectedHeadersConverter.WritePlain = false;
        }
    }

    private static (string? Folder, string? File) SplitTemplate(string? template)
    {
        int split = template?.LastIndexOfAny(new[] { '/', '\\' }) ?? -1;
        if (split < 0)
        {
            return (null, template);
        }

        string folder = template![..split].Trim();
        string file = template[(split + 1)..].Trim();
        return (folder.Length > 0 ? folder : null, file.Length > 0 ? file : null);
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, Options));
        }
        catch (Exception)
        {
            // Instellingen opslaan mag de app nooit laten crashen.
        }
    }

    /// <summary>
    /// Schrijft een backup; met <paramref name="plainHeaders"/> staan de upload-tokens en wachtwoorden leesbaar in het bestand,
    /// zodat de backup ook voor een andere Windows-gebruiker of pc bruikbaar is.
    /// </summary>
    public static void Export(AppSettings settings, string path, bool plainHeaders)
    {
        ProtectedHeadersConverter.WritePlain = plainHeaders;
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(settings, Options));
        }
        finally
        {
            ProtectedHeadersConverter.WritePlain = false;
        }
    }

    public static AppSettings Import(string path)
    {
        AppSettings settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Options)
                               ?? throw new InvalidDataException();
        MigrateTemplates(settings);
        MigrateUploaders(settings);
        return settings;
    }

    /// <summary>
    /// Slaat de uploadheaders (met de token) met DPAPI versleuteld op, alleen leesbaar voor deze Windows-gebruiker.
    /// </summary>
    public sealed class ProtectedHeadersConverter : JsonConverter<Dictionary<string, string>>
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("kniip-uploader");

        [ThreadStatic]
        internal static bool WritePlain;

        public override Dictionary<string, string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (reader.TokenType == JsonTokenType.StartObject)
            {
                // Onversleutelde backup: bij de volgende Save wordt het weer versleuteld.
                if (JsonSerializer.Deserialize<Dictionary<string, string>>(ref reader) is { } plainHeaders)
                {
                    foreach ((string name, string value) in plainHeaders)
                    {
                        result[name] = value;
                    }
                }

                return result;
            }

            if (reader.TokenType != JsonTokenType.String)
            {
                reader.Skip();
                return result;
            }

            try
            {
                byte[] plain = ProtectedData.Unprotect(Convert.FromBase64String(reader.GetString() ?? string.Empty), Entropy,
                    DataProtectionScope.CurrentUser);
                if (JsonSerializer.Deserialize<Dictionary<string, string>>(plain) is { } headers)
                {
                    foreach ((string name, string value) in headers)
                    {
                        result[name] = value;
                    }
                }
            }
            catch (Exception)
            {
                // Van een andere gebruiker of pc: de uploader moet opnieuw worden geïmporteerd.
            }

            return result;
        }

        public override void Write(Utf8JsonWriter writer, Dictionary<string, string> value, JsonSerializerOptions options)
        {
            if (WritePlain)
            {
                JsonSerializer.Serialize(writer, value);
                return;
            }

            byte[] plain = JsonSerializer.SerializeToUtf8Bytes(value);
            writer.WriteStringValue(Convert.ToBase64String(ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser)));
        }
    }

    /// <summary>
    /// Een wachtwoord of wachtzin, net als de headers met DPAPI versleuteld. Onversleuteld staat het als
    /// <c>{"plain": "..."}</c> in een backup, zodat het niet met een versleutelde waarde te verwarren is.
    /// </summary>
    public sealed class ProtectedStringConverter : JsonConverter<string>
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("kniip-uploader-secret");

        public override bool HandleNull => true;

        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.StartObject)
            {
                return JsonSerializer.Deserialize<Dictionary<string, string>>(ref reader) is { } plain
                       && plain.TryGetValue("plain", out string? value)
                    ? value
                    : string.Empty;
            }

            if (reader.TokenType != JsonTokenType.String || reader.GetString() is not { Length: > 0 } text)
            {
                reader.Skip();
                return string.Empty;
            }

            try
            {
                return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(text), Entropy, DataProtectionScope.CurrentUser));
            }
            catch (Exception)
            {
                // Van een andere gebruiker of pc: het wachtwoord moet opnieuw worden ingevuld.
                return string.Empty;
            }
        }

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        {
            if (value.Length == 0)
            {
                writer.WriteStringValue(string.Empty);
            }
            else if (ProtectedHeadersConverter.WritePlain)
            {
                writer.WriteStartObject();
                writer.WriteString("plain", value);
                writer.WriteEndObject();
            }
            else
            {
                byte[] secret = ProtectedData.Protect(Encoding.UTF8.GetBytes(value), Entropy, DataProtectionScope.CurrentUser);
                writer.WriteStringValue(Convert.ToBase64String(secret));
            }
        }
    }

    private sealed class ColorJsonConverter : JsonConverter<Color>
    {
        public override Color Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            string? text = reader.GetString();
            if (text is { Length: 9 } && text[0] == '#'
                && uint.TryParse(text.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint argb))
            {
                return Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
            }

            return Colors.Transparent;
        }

        public override void Write(Utf8JsonWriter writer, Color value, JsonSerializerOptions options) =>
            writer.WriteStringValue($"#{value.A:X2}{value.R:X2}{value.G:X2}{value.B:X2}");
    }
}
