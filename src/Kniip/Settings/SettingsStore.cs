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

    private static readonly JsonSerializerOptions Options = new()
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
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options) ?? new AppSettings();
            }
        }
        catch (Exception)
        {
            // Beschadigd bestand: met standaardwaarden verder.
        }

        return new AppSettings();
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
    /// Slaat de uploadheaders (met de token) met DPAPI versleuteld op, alleen leesbaar voor deze Windows-gebruiker.
    /// </summary>
    public sealed class ProtectedHeadersConverter : JsonConverter<Dictionary<string, string>>
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("kniip-uploader");

        public override Dictionary<string, string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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
            byte[] plain = JsonSerializer.SerializeToUtf8Bytes(value);
            writer.WriteStringValue(Convert.ToBase64String(ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser)));
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
