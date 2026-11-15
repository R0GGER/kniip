using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Kniip.Resources;

namespace Kniip.Upload;

/// <summary>Voert een ShareX custom uploader uit: bouwt het verzoek en haalt de links uit het antwoord.</summary>
public sealed class CustomHttpUploader : IUploader
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(10) };

    private readonly CustomUploaderDestination _config;

    static CustomHttpUploader()
    {
        Client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("kniip", "1.0"));
    }

    public CustomHttpUploader(CustomUploaderDestination config)
    {
        _config = config;
    }

    public async Task<UploadResult> UploadAsync(Stream data, string fileName, string contentType,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!_config.IsConfigured)
        {
            throw new UploadException(Strings.Upload_NotConfigured);
        }

        var input = new SyntaxContext { FileName = fileName };
        using HttpRequestMessage request = BuildRequest(input, data, fileName, contentType, progress);

        HttpResponseMessage response;
        try
        {
            response = await Client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new UploadException(Strings.Upload_Timeout, ex);
        }
        catch (HttpRequestException ex)
        {
            throw new UploadException(string.Format(Strings.Upload_Unreachable, _config.DisplayName, ex.Message), ex);
        }

        using (response)
        {
            string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var output = new SyntaxContext
            {
                FileName = fileName,
                Response = body,
                ResponseUrl = response.RequestMessage?.RequestUri?.ToString() ?? request.RequestUri?.ToString(),
                ResponseMessage = response
            };
            string raw = DescribeResponse(response, body);

            if (!response.IsSuccessStatusCode)
            {
                string detail = CustomUploaderSyntax.Evaluate(_config.ErrorMessage, output).Trim() is { Length: > 0 } parsed
                    ? parsed
                    : ReadError(body) ?? response.ReasonPhrase ?? string.Empty;
                string hint = response.StatusCode == HttpStatusCode.Unauthorized ? "\n\n" + Strings.Upload_TokenInvalid : string.Empty;
                throw new UploadException(string.Format(Strings.Upload_Rejected, (int)response.StatusCode, detail) + hint) { Response = raw };
            }

            string url = (_config.URL.Trim().Length == 0 ? body : CustomUploaderSyntax.Evaluate(_config.URL, output)).Trim();
            if (url.Length == 0)
            {
                throw new UploadException(string.Format(Strings.Upload_NoLink, _config.URL)) { Response = raw };
            }

            return new UploadResult(url,
                NullIfEmpty(CustomUploaderSyntax.Evaluate(_config.ThumbnailURL, output)),
                NullIfEmpty(CustomUploaderSyntax.Evaluate(_config.DeletionURL, output)),
                raw);
        }
    }

    private HttpRequestMessage BuildRequest(SyntaxContext input, Stream data, string fileName, string contentType, IProgress<double>? progress)
    {
        string url = CustomUploaderSyntax.EvaluateRequest(_config.RequestURL, input).Trim();
        if (_config.Parameters.Count > 0)
        {
            string query = string.Join("&", _config.Parameters.Select(p =>
                Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(CustomUploaderSyntax.EvaluateRequest(p.Value, input))));
            url += (url.Contains('?') ? "&" : "?") + query;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new UploadException(string.Format(Strings.Upload_BadServer, url));
        }

        var request = new HttpRequestMessage(new HttpMethod(_config.RequestMethod.ToString()), uri)
        {
            Content = BuildContent(input, data, fileName, contentType, progress)
        };

        foreach ((string name, string template) in _config.Headers)
        {
            string value = CustomUploaderSyntax.EvaluateRequest(template, input);
            if (request.Content != null && IsContentHeader(name))
            {
                request.Content.Headers.Remove(name);
                request.Content.Headers.TryAddWithoutValidation(name, value);
            }
            else
            {
                request.Headers.Remove(name);
                request.Headers.TryAddWithoutValidation(name, value);
            }
        }

        return request;
    }

    private HttpContent? BuildContent(SyntaxContext input, Stream data, string fileName, string contentType, IProgress<double>? progress)
    {
        Dictionary<string, string> Arguments() =>
            _config.Arguments.ToDictionary(a => a.Key, a => CustomUploaderSyntax.EvaluateRequest(a.Value, input));

        switch (_config.Body)
        {
            case CustomBodyType.MultipartFormData:
                var form = new MultipartFormDataContent();
                foreach ((string name, string value) in Arguments())
                {
                    form.Add(new StringContent(value), name);
                }

                if (_config.FileFormName.Trim().Length > 0)
                {
                    var file = new ProgressContent(data, progress);
                    file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
                    form.Add(file, _config.FileFormName.Trim(), fileName);
                }

                return form;
            case CustomBodyType.FormURLEncoded:
                return new FormUrlEncodedContent(Arguments());
            case CustomBodyType.JSON:
                return new StringContent(EvaluateData(input, CustomUploaderSyntax.JsonEncode), Encoding.UTF8, "application/json");
            case CustomBodyType.XML:
                return new StringContent(EvaluateData(input, CustomUploaderSyntax.XmlEncode), Encoding.UTF8, "application/xml");
            case CustomBodyType.Binary:
                var binary = new ProgressContent(data, progress);
                binary.Headers.ContentType = new MediaTypeHeaderValue(contentType);
                return binary;
            default:
                return null;
        }
    }

    private string EvaluateData(SyntaxContext input, Func<string, string> encode) =>
        CustomUploaderSyntax.Evaluate(CustomUploaderSyntax.ReplaceDynamicValues(_config.Data),
            new SyntaxContext { FileName = input.FileName, Input = input.Input, Encode = encode });

    private static bool IsContentHeader(string name) =>
        name.StartsWith("Content-", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Expires", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Last-Modified", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Allow", StringComparison.OrdinalIgnoreCase);

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string DescribeResponse(HttpResponseMessage response, string body)
    {
        var text = new StringBuilder();
        text.Append("HTTP ").Append((int)response.StatusCode).Append(' ').AppendLine(response.ReasonPhrase);
        foreach (KeyValuePair<string, IEnumerable<string>> header in response.Headers.Concat(response.Content.Headers))
        {
            text.Append(header.Key).Append(": ").AppendLine(string.Join(", ", header.Value));
        }

        text.AppendLine();
        text.Append(body.Length > 20000 ? body[..20000] + "..." : body);
        return text.ToString();
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