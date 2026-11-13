using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Kniip.Update;

/// <summary>Downloadt de installer van een release en start die stil.</summary>
public static class UpdateInstaller
{
    public static string DownloadFolder => Path.Combine(Path.GetTempPath(), "kniip-update");

    public static async Task<string> DownloadAsync(ReleaseInfo release, IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (release.InstallerUrl is not { } url)
        {
            throw new UpdateException($"Release {release.Tag} heeft geen installer.");
        }

        Directory.CreateDirectory(DownloadFolder);
        string path = Path.Combine(DownloadFolder, $"Kniip-Setup-{release.Version}.exe");
        string partial = path + ".download";
        try
        {
            using (HttpResponseMessage response = await UpdateChecker.Client
                       .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
            {
                if (!response.IsSuccessStatusCode)
                {
                    throw new UpdateException($"Downloaden is mislukt ({(int)response.StatusCode}).");
                }

                long total = response.Content.Headers.ContentLength ?? release.InstallerSize;
                await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using var target = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
                var buffer = new byte[81920];
                long received = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    received += read;
                    if (total > 0)
                    {
                        progress?.Report(Math.Min(1, (double)received / total));
                    }
                }

                if (release.InstallerSize > 0 && received != release.InstallerSize)
                {
                    throw new UpdateException("De download is onvolledig; probeer het opnieuw.");
                }
            }

            File.Move(partial, path, overwrite: true);
            return path;
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new UpdateException("git.hibbit.net reageert niet (time-out).", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new UpdateException("Downloaden is mislukt. Controleer de internetverbinding.", ex);
        }
        finally
        {
            TryDelete(partial);
        }
    }

    /// <summary>Start de installer zonder vensters; die sluit kniip en start het daarna opnieuw.</summary>
    public static void Start(string installerPath) =>
        Process.Start(new ProcessStartInfo(installerPath)
        {
            Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS",
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(installerPath) ?? string.Empty,
        });

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
