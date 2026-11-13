using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Kniip.Update;

public sealed class UpdateException : Exception
{
    public UpdateException(string message, Exception? inner = null) : base(message, inner)
    {
    }
}

public sealed record ReleaseInfo(
    Version Version,
    string Tag,
    string Title,
    DateTimeOffset? Date,
    string Body,
    string HtmlUrl,
    string? InstallerUrl,
    long InstallerSize);

/// <summary>Leest de releases van kniip uit de Gitea/Forgejo-API van git.hibbit.net.</summary>
public static class UpdateChecker
{
    public const string RepositoryUrl = "https://git.hibbit.net/rogier/kniip/";
    public const string ReleasesPage = "https://git.hibbit.net/rogier/kniip/releases";
    private const string ReleasesApi = "https://git.hibbit.net/api/v1/repos/rogier/kniip/releases?limit=20";

    internal static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(10) };

    static UpdateChecker()
    {
        Client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("kniip", CurrentVersion.ToString()));
    }

    /// <summary>Versie van deze exe, altijd in drie delen (1.2.0).</summary>
    public static Version CurrentVersion { get; } = Normalize(Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(1, 0, 0));

    /// <summary>Gepubliceerde releases, nieuwste eerst; concepten en voorversies vallen af.</summary>
    public static async Task<IReadOnlyList<ReleaseInfo>> GetReleasesAsync(CancellationToken cancellationToken = default)
    {
        string body;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ReleasesApi);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            using HttpResponseMessage response = await Client.SendAsync(request, timeout.Token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                throw new UpdateException("De releases van kniip zijn niet gevonden op git.hibbit.net.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new UpdateException($"git.hibbit.net gaf een fout ({(int)response.StatusCode}).");
            }

            body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new UpdateException("git.hibbit.net reageert niet (time-out).", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new UpdateException("Kan git.hibbit.net niet bereiken. Controleer de internetverbinding.", ex);
        }

        try
        {
            return Parse(body);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            throw new UpdateException("Het antwoord van git.hibbit.net is niet te lezen.", ex);
        }
    }

    private static List<ReleaseInfo> Parse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        var releases = new List<ReleaseInfo>();
        foreach (JsonElement item in document.RootElement.EnumerateArray())
        {
            if (Bool(item, "draft") || Bool(item, "prerelease") || !TryParseVersion(String(item, "tag_name"), out Version version))
            {
                continue;
            }

            string? installerUrl = null;
            long installerSize = 0;
            if (item.TryGetProperty("assets", out JsonElement assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement asset in assets.EnumerateArray())
                {
                    string name = String(asset, "name");
                    if (name.StartsWith("Kniip-Setup-", StringComparison.OrdinalIgnoreCase)
                        && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        installerUrl = String(asset, "browser_download_url");
                        installerSize = asset.TryGetProperty("size", out JsonElement size) && size.TryGetInt64(out long bytes) ? bytes : 0;
                        break;
                    }
                }
            }

            DateTimeOffset? date = DateTimeOffset.TryParse(String(item, "published_at"), CultureInfo.InvariantCulture,
                DateTimeStyles.None, out DateTimeOffset published) ? published : null;
            string tag = String(item, "tag_name");
            string title = String(item, "name");
            releases.Add(new ReleaseInfo(version, tag, title.Length > 0 ? title : tag, date, String(item, "body"),
                String(item, "html_url") is { Length: > 0 } url ? url : ReleasesPage,
                string.IsNullOrEmpty(installerUrl) ? null : installerUrl, installerSize));
        }

        return releases.OrderByDescending(r => r.Version).ToList();
    }

    internal static bool TryParseVersion(string? tag, out Version version)
    {
        version = new Version(0, 0, 0);
        string text = (tag ?? string.Empty).Trim().TrimStart('v', 'V');
        int suffix = text.IndexOfAny(new[] { '-', '+' });
        if (suffix >= 0)
        {
            text = text[..suffix];
        }

        if (!Version.TryParse(text.Contains('.') ? text : text + ".0", out Version? parsed))
        {
            return false;
        }

        version = Normalize(parsed);
        return true;
    }

    private static Version Normalize(Version version) =>
        new(version.Major, Math.Max(version.Minor, 0), Math.Max(version.Build, 0));

    private static string String(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;

    private static bool Bool(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.True;
}
