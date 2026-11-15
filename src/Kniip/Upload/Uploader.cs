using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kniip.Resources;

namespace Kniip.Upload;

public sealed class UploadException : Exception
{
    public UploadException(string message, Exception? inner = null) : base(message, inner)
    {
    }

    /// <summary>Het ruwe antwoord als de server wel antwoordde, voor het testen van een custom uploader.</summary>
    public string? Response { get; init; }
}

/// <param name="Response">Het ruwe antwoord van de server (status, headers, body), voor het testen van een custom uploader.</param>
/// <param name="DestinationChanged">De bestemming is aangepast (een nieuw vastgelegde host key) en moet worden opgeslagen.</param>
public sealed record UploadResult(string Url, string? ThumbnailUrl = null, string? DeletionUrl = null,
    string? Response = null, bool DestinationChanged = false);

public interface IUploader
{
    Task<UploadResult> UploadAsync(Stream data, string fileName, string contentType,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default);
}

public static class UploaderFactory
{
    public static IUploader Create(UploadDestination destination) => destination switch
    {
        CustomUploaderDestination custom => new CustomHttpUploader(custom),
        FtpDestination ftp => new FtpUploader(ftp),
        SshDestination ssh => new SshUploader(ssh),
        _ => throw new UploadException(Strings.Upload_NotConfigured)
    };

    public static Task<UploadResult> UploadAsync(UploadDestination destination, Stream data, string fileName, string contentType,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!destination.IsConfigured)
        {
            throw new UploadException(Strings.Upload_NotConfigured);
        }

        return Create(destination).UploadAsync(data, fileName, contentType, progress, cancellationToken);
    }
}

/// <summary>Webadres van een bestand op een FTP- of SSH-server.</summary>
internal static class RemotePaths
{
    public static string Combine(string folder, string fileName)
    {
        string trimmed = folder.Trim().Replace('\\', '/').TrimEnd('/');
        return trimmed.Length == 0 ? fileName : trimmed + "/" + fileName;
    }

    public static string PublicUrl(FileServerDestination destination, string scheme, string remotePath, string fileName)
    {
        string encoded = Uri.EscapeDataString(fileName);
        string template = destination.PublicUrl.Trim();
        if (template.Length == 0)
        {
            string path = string.Join("/", remotePath.Split('/').Select(Uri.EscapeDataString));
            string port = destination.Port > 0 && destination.Port != destination.DefaultPort ? ":" + destination.Port : string.Empty;
            return $"{scheme}://{destination.Host.Trim()}{port}/{path.TrimStart('/')}";
        }

        return template.Contains("{filename}", StringComparison.OrdinalIgnoreCase)
            ? template.Replace("{filename}", encoded, StringComparison.OrdinalIgnoreCase)
            : template.TrimEnd('/') + "/" + encoded;
    }
}
