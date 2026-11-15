using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kniip.Resources;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace Kniip.Upload;

/// <summary>
/// Uploadt via SFTP of SCP. De host key van de server wordt bij de eerste verbinding na bevestiging
/// vastgelegd; daarna weigert kniip een server met een andere sleutel.
/// </summary>
public sealed class SshUploader : IUploader
{
    private readonly SshDestination _destination;
    private string? _learnedFingerprint;
    private string? _rejectedFingerprint;
    private bool _declined;

    public SshUploader(SshDestination destination)
    {
        _destination = destination;
    }

    public async Task<UploadResult> UploadAsync(Stream data, string fileName, string contentType,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        string remotePath = RemotePaths.Combine(_destination.RemoteFolder, fileName);
        ConnectionInfo connection = CreateConnection();
        long total = data.CanSeek ? data.Length - data.Position : -1;

        try
        {
            if (_destination is ScpDestination)
            {
                await UploadScpAsync(connection, data, remotePath, total, progress, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await UploadSftpAsync(connection, data, remotePath, total, progress, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not (UploadException or OperationCanceledException))
        {
            throw Translate(ex);
        }

        bool changed = false;
        if (_learnedFingerprint != null)
        {
            _destination.HostKeyFingerprint = _learnedFingerprint;
            changed = true;
        }

        string scheme = _destination is ScpDestination ? "scp" : "sftp";
        return new UploadResult(RemotePaths.PublicUrl(_destination, scheme, remotePath, fileName), DestinationChanged: changed);
    }

    private async Task UploadSftpAsync(ConnectionInfo connection, Stream data, string remotePath, long total,
        IProgress<double>? progress, CancellationToken cancellationToken)
    {
        using var client = new SftpClient(connection);
        client.HostKeyReceived += OnHostKeyReceived;
        await client.ConnectAsync(cancellationToken).ConfigureAwait(false);

        string folder = remotePath.Contains('/') ? remotePath[..remotePath.LastIndexOf('/')] : string.Empty;
        string current = folder.StartsWith('/') ? "/" : string.Empty;
        foreach (string part in folder.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            current = current.Length == 0 || current.EndsWith('/') ? current + part : current + "/" + part;
            if (!await client.ExistsAsync(current, cancellationToken).ConfigureAwait(false))
            {
                await client.CreateDirectoryAsync(current, cancellationToken).ConfigureAwait(false);
            }
        }

        IProgress<UploadFileProgressReport>? sftpProgress = progress == null || total <= 0
            ? null
            : new InlineProgress<UploadFileProgressReport>(r => progress.Report(Math.Clamp((double)r.TotalBytesUploaded / total, 0, 1)));
        await client.UploadFileAsync(data, remotePath, sftpProgress, cancellationToken).ConfigureAwait(false);
        client.Disconnect();
    }

    private async Task UploadScpAsync(ConnectionInfo connection, Stream data, string remotePath, long total,
        IProgress<double>? progress, CancellationToken cancellationToken)
    {
        string folder = remotePath.Contains('/') ? remotePath[..remotePath.LastIndexOf('/')] : string.Empty;
        if (folder.Length > 0)
        {
            // SCP kan zelf geen mappen maken.
            using var shell = new SshClient(connection);
            shell.HostKeyReceived += OnHostKeyReceived;
            await shell.ConnectAsync(cancellationToken).ConfigureAwait(false);
            using SshCommand command = shell.RunCommand("mkdir -p -- '" + folder.Replace("'", "'\\''") + "'");
            shell.Disconnect();
        }

        using var client = new ScpClient(connection, RemotePathTransformation.ShellQuote);
        client.HostKeyReceived += OnHostKeyReceived;
        if (progress != null)
        {
            client.Uploading += (_, e) =>
            {
                long size = e.Size > 0 ? e.Size : total;
                if (size > 0)
                {
                    progress.Report(Math.Clamp((double)e.Uploaded / size, 0, 1));
                }
            };
        }

        await client.ConnectAsync(cancellationToken).ConfigureAwait(false);
        using (cancellationToken.Register(() => client.Disconnect()))
        {
            await Task.Run(() => client.Upload(data, remotePath), CancellationToken.None).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        client.Disconnect();
    }

    private ConnectionInfo CreateConnection()
    {
        string user = _destination.Username.Trim();
        var methods = new List<AuthenticationMethod>();
        if (_destination.PrivateKeyPath.Trim() is { Length: > 0 } keyPath)
        {
            PrivateKeyFile key;
            try
            {
                key = _destination.Passphrase.Length > 0 ? new PrivateKeyFile(keyPath, _destination.Passphrase) : new PrivateKeyFile(keyPath);
            }
            catch (Exception ex) when (ex is SshException or IOException or UnauthorizedAccessException or ArgumentException)
            {
                throw new UploadException(string.Format(Strings.Upload_PrivateKeyInvalid, keyPath, ex.Message), ex);
            }

            methods.Add(new PrivateKeyAuthenticationMethod(user, key));
        }

        if (_destination.Password.Length > 0)
        {
            methods.Add(new PasswordAuthenticationMethod(user, _destination.Password));
            var interactive = new KeyboardInteractiveAuthenticationMethod(user);
            interactive.AuthenticationPrompt += (_, e) =>
            {
                foreach (AuthenticationPrompt prompt in e.Prompts.Where(p => !p.IsEchoed))
                {
                    prompt.Response = _destination.Password;
                }
            };
            methods.Add(interactive);
        }

        if (methods.Count == 0)
        {
            methods.Add(new NoneAuthenticationMethod(user));
        }

        return new ConnectionInfo(_destination.Host.Trim(), _destination.EffectivePort, user, methods.ToArray())
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
    }

    private void OnHostKeyReceived(object? sender, HostKeyEventArgs e)
    {
        string fingerprint = "SHA256:" + e.FingerPrintSHA256;
        string? known = _learnedFingerprint ?? (_destination.HostKeyFingerprint.Length > 0 ? _destination.HostKeyFingerprint : null);
        if (known != null)
        {
            e.CanTrust = string.Equals(known, fingerprint, StringComparison.Ordinal);
            if (!e.CanTrust)
            {
                _rejectedFingerprint = fingerprint;
            }

            return;
        }

        e.CanTrust = UploadPrompts.ConfirmHostKey(_destination.Host.Trim(), e.HostKeyName, fingerprint);
        if (e.CanTrust)
        {
            _learnedFingerprint = fingerprint;
        }
        else
        {
            _declined = true;
        }
    }

    private UploadException Translate(Exception ex)
    {
        if (_declined)
        {
            return new UploadException(Strings.Upload_HostKeyDeclined, ex);
        }

        if (_rejectedFingerprint != null)
        {
            return new UploadException(string.Format(Strings.Upload_HostKeyChanged, _destination.DisplayName,
                _destination.HostKeyFingerprint, _rejectedFingerprint), ex);
        }

        return ex switch
        {
            SshAuthenticationException => new UploadException(string.Format(Strings.Upload_LoginFailed, _destination.DisplayName, ex.Message), ex),
            Renci.SshNet.Common.SftpPathNotFoundException or Renci.SshNet.Common.SftpPermissionDeniedException or ScpException =>
                new UploadException(string.Format(Strings.Upload_ServerFailed, _destination.DisplayName, ex.Message), ex),
            _ => new UploadException(string.Format(Strings.Upload_Unreachable, _destination.DisplayName, ex.Message), ex)
        };
    }
}
