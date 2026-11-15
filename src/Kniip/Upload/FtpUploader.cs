using System;
using System.IO;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using FluentFTP;
using FluentFTP.Exceptions;
using Kniip.Resources;

namespace Kniip.Upload;

/// <summary>Uploadt naar een FTP- of FTPS-server en geeft het publieke webadres van het bestand terug.</summary>
public sealed class FtpUploader : IUploader
{
    private readonly FtpDestination _destination;

    public FtpUploader(FtpDestination destination)
    {
        _destination = destination;
    }

    public async Task<UploadResult> UploadAsync(Stream data, string fileName, string contentType,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        var config = new FtpConfig
        {
            EncryptionMode = _destination.Encryption switch
            {
                FtpEncryption.Implicit => FtpEncryptionMode.Implicit,
                FtpEncryption.Explicit => FtpEncryptionMode.Explicit,
                _ => FtpEncryptionMode.None
            },
            DataConnectionType = _destination.Passive ? FtpDataConnectionType.AutoPassive : FtpDataConnectionType.AutoActive,
            ConnectTimeout = 20000,
            ReadTimeout = 30000,
            DataConnectionConnectTimeout = 20000,
            DataConnectionReadTimeout = 30000
        };
        string user = _destination.Username.Trim().Length > 0 ? _destination.Username.Trim() : "anonymous";
        string remotePath = RemotePaths.Combine(_destination.RemoteFolder, fileName);

        using var client = new AsyncFtpClient(_destination.Host.Trim(), user, _destination.Password, _destination.EffectivePort, config);
        try
        {
            await client.Connect(cancellationToken).ConfigureAwait(false);
            IProgress<FtpProgress>? ftpProgress = progress == null
                ? null
                : new InlineProgress<FtpProgress>(p => progress.Report(Math.Clamp(p.Progress / 100, 0, 1)));
            FtpStatus status = await client.UploadStream(data, remotePath, FtpRemoteExists.Overwrite, createRemoteDir: true,
                ftpProgress, cancellationToken).ConfigureAwait(false);
            if (status == FtpStatus.Failed)
            {
                throw new UploadException(string.Format(Strings.Upload_ServerFailed, _destination.DisplayName, remotePath));
            }

            await client.Disconnect(cancellationToken).ConfigureAwait(false);
        }
        catch (FtpAuthenticationException ex)
        {
            throw new UploadException(string.Format(Strings.Upload_LoginFailed, _destination.DisplayName, ex.Message), ex);
        }
        catch (Exception ex) when (ex is FtpSecurityNotAvailableException or AuthenticationException)
        {
            throw new UploadException(string.Format(Strings.Upload_FtpTlsFailed, _destination.DisplayName, ex.Message), ex);
        }
        catch (FtpCommandException ex)
        {
            throw new UploadException(string.Format(Strings.Upload_ServerFailed, _destination.DisplayName, ex.Message), ex);
        }
        catch (Exception ex) when (ex is not (UploadException or OperationCanceledException))
        {
            throw new UploadException(string.Format(Strings.Upload_Unreachable, _destination.DisplayName, ex.InnerException?.Message ?? ex.Message), ex);
        }

        return new UploadResult(RemotePaths.PublicUrl(_destination, _destination.Encryption == FtpEncryption.None ? "ftp" : "ftps", remotePath, fileName));
    }
}

/// <summary>Voortgang direct doorgeven, zonder via de SynchronizationContext te lopen.</summary>
internal sealed class InlineProgress<T> : IProgress<T>
{
    private readonly Action<T> _report;

    public InlineProgress(Action<T> report)
    {
        _report = report;
    }

    public void Report(T value) => _report(value);
}
