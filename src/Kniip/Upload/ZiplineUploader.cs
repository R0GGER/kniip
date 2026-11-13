using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Kniip.Upload;

public sealed class UploadException : Exception
{
    public UploadException(string message, Exception? inner = null) : base(message, inner)
    {
    }
}

/// <summary>Uploadt een bestand naar de ingestelde Zipline-server en geeft de link terug.</summary>
public static class ZiplineUploader
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(10) };

    static ZiplineUploader()
    {
        Client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("kniip", "1.0"));
    }

    public static async Task<string> UploadAsync(UploaderConfig config, Stream data, string fileName, string contentType,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!config.IsConfigured)
        {
            throw new UploadException("Er is nog geen uploadbestemming ingesteld (Instellingen > Uploaden).");
        }

        var file = new ProgressContent(data, progress);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        using var form = new MultipartFormDataContent { { file, config.FileFormName, fileName } };
        using var request = new HttpRequestMessage(HttpMethod.Post, config.RequestUrl) { Content = form };
        foreach ((string name, string value) in config.Headers)
        {
            if (!request.Headers.TryAddWithoutValidation(name, value))
            {
                form.Headers.TryAddWithoutValidation(name, value);
            }
        }

        HttpResponseMessage response;
        try
        {
            response = await Client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new UploadException("De server reageert niet (time-out).", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new UploadException($"Kan {config.DisplayName} niet bereiken.\n\n{ex.Message}", ex);
        }

        using (response)
        {
            string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                string detail = ReadError(body) ?? response.ReasonPhrase ?? string.Empty;
                string hint = response.StatusCode == HttpStatusCode.Unauthorized ? "\n\nDe token is ongeldig of verlopen; importeer de uploader opnieuw." : string.Empty;
                throw new UploadException($"De server weigerde de upload ({(int)response.StatusCode}): {detail}{hint}");
            }

            return ReadUrl(body, config.UrlPath)
                   ?? throw new UploadException($"De upload is gelukt, maar er staat geen link in het antwoord op '{config.UrlPath}'.");
        }
    }

    private static string? ReadError(string body)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (string name in new[] { "error", "message" })
                {
                    if (document.RootElement.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String)
                    {
                        return value.GetString();
                    }
                }
            }
        }
        catch (JsonException)
        {
        }

        return body.Length is > 0 and < 200 ? body.Trim() : null;
    }

    /// <summary>Leest een pad als <c>files[0].url</c> uit een JSON-antwoord.</summary>
    internal static string? ReadUrl(string body, string path)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return null;
        }

        using (document)
        {
            JsonElement current = document.RootElement;
            foreach (Match part in Regex.Matches(path, @"([^.\[\]]+)|\[(\d+)\]"))
            {
                if (part.Groups[2].Success)
                {
                    int index = int.Parse(part.Groups[2].Value);
                    if (current.ValueKind != JsonValueKind.Array || index >= current.GetArrayLength())
                    {
                        return null;
                    }

                    current = current[index];
                }
                else if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(part.Groups[1].Value, out current))
                {
                    return null;
                }
            }

            return current.ValueKind == JsonValueKind.String ? current.GetString() : null;
        }
    }

    /// <summary>Stuurt een stream in blokken mee en meldt hoeveel er al verzonden is.</summary>
    private sealed class ProgressContent : HttpContent
    {
        private readonly Stream _source;
        private readonly IProgress<double>? _progress;

        public ProgressContent(Stream source, IProgress<double>? progress)
        {
            _source = source;
            _progress = progress;
        }

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            await SerializeToStreamAsync(stream, context, CancellationToken.None).ConfigureAwait(false);

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
        {
            var buffer = new byte[81920];
            long total = _source.CanSeek ? _source.Length : -1;
            long sent = 0;
            int read;
            while ((read = await _source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await stream.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                sent += read;
                if (total > 0)
                {
                    _progress?.Report((double)sent / total);
                }
            }
        }

        protected override bool TryComputeLength(out long length)
        {
            length = _source.CanSeek ? _source.Length - _source.Position : -1;
            return _source.CanSeek;
        }
    }
}
