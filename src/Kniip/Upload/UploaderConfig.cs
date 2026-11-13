using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Kniip.Settings;

namespace Kniip.Upload;

/// <summary>Bestemming voor uploads, geïmporteerd uit een ShareX- of shellscript-uploader van Zipline.</summary>
public sealed class UploaderConfig
{
    public string Name { get; set; } = string.Empty;

    public string RequestUrl { get; set; } = string.Empty;

    /// <summary>Bevat de token (authorization); staat daarom versleuteld in settings.json.</summary>
    [JsonConverter(typeof(SettingsStore.ProtectedHeadersConverter))]
    public Dictionary<string, string> Headers { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public string FileFormName { get; set; } = "file";

    /// <summary>Pad naar de link in het JSON-antwoord, bijvoorbeeld <c>files[0].url</c>.</summary>
    public string UrlPath { get; set; } = "files[0].url";

    [JsonIgnore]
    public bool IsConfigured => Uri.TryCreate(RequestUrl, UriKind.Absolute, out Uri? uri)
                                && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    [JsonIgnore]
    public string DisplayName => Uri.TryCreate(RequestUrl, UriKind.Absolute, out Uri? uri) ? uri.Authority : RequestUrl;
}
