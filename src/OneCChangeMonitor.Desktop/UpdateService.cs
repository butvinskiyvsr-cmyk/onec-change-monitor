using System.Net.Http.Headers;
using System.Net.Http;
using System.Text.Json;

namespace OneCChangeMonitor.Desktop;

public sealed record UpdateInfo(Version Version, string Tag, string Title, string Notes, Uri ReleaseUri, bool IsNewer);

public sealed class UpdateService
{
    public static readonly Version CurrentVersion = new(0, 2, 1);
    private static readonly Uri LatestReleaseApi = new("https://api.github.com/repos/butvinskiyvsr-cmyk/onec-change-monitor/releases/latest");
    private readonly HttpClient _client;

    public UpdateService()
    {
        _client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        _client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("OneCChangeMonitor", CurrentVersion.ToString()));
        _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public async Task<UpdateInfo> CheckAsync(CancellationToken cancellationToken)
    {
        await using var stream = await _client.GetStreamAsync(LatestReleaseApi, cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? "0.0.0";
        var normalized = tag.Trim().TrimStart('v', 'V').Split('-', '+')[0];
        if (!Version.TryParse(normalized, out var version)) throw new InvalidOperationException($"Не удалось определить версию релиза: {tag}");

        var title = root.TryGetProperty("name", out var name) ? name.GetString() : null;
        var notes = root.TryGetProperty("body", out var body) ? body.GetString() : null;
        var url = root.GetProperty("html_url").GetString() ?? throw new InvalidOperationException("GitHub не вернул адрес релиза.");
        return new UpdateInfo(version, tag, string.IsNullOrWhiteSpace(title) ? $"Версия {tag}" : title!, notes ?? string.Empty, new Uri(url), version > CurrentVersion);
    }
}
