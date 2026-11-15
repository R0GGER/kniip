using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Kniip.Settings;

namespace Kniip.Upload;

/// <summary>Een plek waar Uploaden naartoe gaat: een HTTP-uploader in ShareX-formaat, FTP, SFTP of SCP.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(CustomUploaderDestination), "custom")]
[JsonDerivedType(typeof(FtpDestination), "ftp")]
[JsonDerivedType(typeof(SftpDestination), "sftp")]
[JsonDerivedType(typeof(ScpDestination), "scp")]
public abstract class UploadDestination
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    [JsonIgnore]
    public abstract bool IsConfigured { get; }

    /// <summary>Server waar de upload naartoe gaat, voor meldingen als "Uploaden naar ...".</summary>
    [JsonIgnore]
    public abstract string DisplayName { get; }

    [JsonIgnore]
    public abstract string TypeName { get; }

    [JsonIgnore]
    public string Title => Name.Length > 0 ? Name : DisplayName;

    /// <summary>Bevat deze bestemming een token of wachtwoord.</summary>
    [JsonIgnore]
    public abstract bool HasSecrets { get; }
}

public enum CustomRequestMethod
{
    GET,
    POST,
    PUT,
    PATCH,
    DELETE
}

public enum CustomBodyType
{
    None,
    MultipartFormData,
    FormURLEncoded,
    JSON,
    XML,
    Binary
}

/// <summary>
/// HTTP-uploader volgens het ShareX custom-uploaderformaat (https://getsharex.com/docs/custom-uploader).
/// URL, ThumbnailURL, DeletionURL en ErrorMessage zijn sjablonen met de ShareX-syntax, zie <see cref="CustomUploaderSyntax"/>.
/// </summary>
public sealed class CustomUploaderDestination : UploadDestination
{
    public string Version { get; set; } = "17.0.0";

    public string DestinationType { get; set; } = "ImageUploader, FileUploader";

    public CustomRequestMethod RequestMethod { get; set; } = CustomRequestMethod.POST;

    public string RequestURL { get; set; } = string.Empty;

    /// <summary>Komen als querystring achter de RequestURL; kunnen een token bevatten en staan daarom versleuteld.</summary>
    [JsonConverter(typeof(SettingsStore.ProtectedHeadersConverter))]
    public Dictionary<string, string> Parameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonConverter(typeof(SettingsStore.ProtectedHeadersConverter))]
    public Dictionary<string, string> Headers { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public CustomBodyType Body { get; set; } = CustomBodyType.MultipartFormData;

    /// <summary>Formuliervelden bij MultipartFormData en FormURLEncoded.</summary>
    [JsonConverter(typeof(SettingsStore.ProtectedHeadersConverter))]
    public Dictionary<string, string> Arguments { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public string FileFormName { get; set; } = "file";

    /// <summary>Body bij JSON en XML.</summary>
    public string Data { get; set; } = string.Empty;

    public string URL { get; set; } = string.Empty;

    public string ThumbnailURL { get; set; } = string.Empty;

    public string DeletionURL { get; set; } = string.Empty;

    public string ErrorMessage { get; set; } = string.Empty;

    [JsonIgnore]
    public override bool IsConfigured => RequestURL.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                                         || RequestURL.StartsWith("http://", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public override string DisplayName => Uri.TryCreate(RequestURL, UriKind.Absolute, out Uri? uri) ? uri.Authority : RequestURL;

    [JsonIgnore]
    public override string TypeName => "Custom";

    [JsonIgnore]
    public override bool HasSecrets => Headers.Count > 0 || Parameters.Count > 0 || Arguments.Count > 0;
}

/// <summary>Gemeenschappelijk voor FTP, SFTP en SCP: een map op een server en het webadres waar die map te zien is.</summary>
public abstract class FileServerDestination : UploadDestination
{
    public string Host { get; set; } = string.Empty;

    public int Port { get; set; }

    public string Username { get; set; } = string.Empty;

    [JsonConverter(typeof(SettingsStore.ProtectedStringConverter))]
    public string Password { get; set; } = string.Empty;

    public string RemoteFolder { get; set; } = string.Empty;

    /// <summary>Webadres van het bestand, bijvoorbeeld <c>https://cdn.example.com/shots/{filename}</c>; zonder {filename} komt de naam erachter.</summary>
    public string PublicUrl { get; set; } = string.Empty;

    [JsonIgnore]
    public override bool IsConfigured => Host.Trim().Length > 0;

    [JsonIgnore]
    public override string DisplayName => Host;

    [JsonIgnore]
    public abstract int DefaultPort { get; }

    [JsonIgnore]
    public int EffectivePort => Port > 0 ? Port : DefaultPort;

    [JsonIgnore]
    public override bool HasSecrets => Password.Length > 0;
}

public enum FtpEncryption
{
    None,
    Explicit,
    Implicit
}

public sealed class FtpDestination : FileServerDestination
{
    public FtpEncryption Encryption { get; set; } = FtpEncryption.Explicit;

    public bool Passive { get; set; } = true;

    [JsonIgnore]
    public override string TypeName => Encryption == FtpEncryption.None ? "FTP" : "FTPS";

    [JsonIgnore]
    public override int DefaultPort => Encryption == FtpEncryption.Implicit ? 990 : 21;
}

public abstract class SshDestination : FileServerDestination
{
    /// <summary>Leeg betekent aanmelden met het wachtwoord.</summary>
    public string PrivateKeyPath { get; set; } = string.Empty;

    [JsonConverter(typeof(SettingsStore.ProtectedStringConverter))]
    public string Passphrase { get; set; } = string.Empty;

    /// <summary>SHA256-vingerafdruk van de server, vastgelegd bij de eerste verbinding.</summary>
    public string HostKeyFingerprint { get; set; } = string.Empty;

    [JsonIgnore]
    public override int DefaultPort => 22;

    [JsonIgnore]
    public override bool HasSecrets => base.HasSecrets || Passphrase.Length > 0;
}

public sealed class SftpDestination : SshDestination
{
    [JsonIgnore]
    public override string TypeName => "SFTP";
}

public sealed class ScpDestination : SshDestination
{
    [JsonIgnore]
    public override string TypeName => "SCP";
}

/// <summary>Zipline-uploader van vóór de lijst met bestemmingen; wordt bij het laden omgezet.</summary>
public sealed class LegacyUploaderConfig
{
    public string Name { get; set; } = string.Empty;

    public string RequestUrl { get; set; } = string.Empty;

    [JsonConverter(typeof(SettingsStore.ProtectedHeadersConverter))]
    public Dictionary<string, string> Headers { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public string FileFormName { get; set; } = "file";

    public string UrlPath { get; set; } = "files[0].url";
}
