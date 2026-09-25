using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OneCChangeMonitor.Desktop;

public sealed record UpdateInfo(
    Version Version,
    string Tag,
    string Title,
    string Notes,
    Uri ReleaseUri,
    Uri? InstallerUri,
    Uri? ChecksumUri,
    bool IsNewer)
{
    public bool CanInstall => IsNewer && InstallerUri is not null && ChecksumUri is not null;
}

public sealed class UpdateService
{
    public static readonly Version CurrentVersion = new(0, 3, 2);
    private static readonly Uri LatestReleaseApi = new("https://api.github.com/repos/butvinskiyvsr-cmyk/onec-change-monitor/releases/latest");
    private readonly HttpClient _client;
    private readonly string _updatesRoot;

    public UpdateService() : this(new HttpClient { Timeout = TimeSpan.FromMinutes(10) })
    {
    }

    public UpdateService(HttpClient client, string? updatesRoot = null)
    {
        _client = client;
        _client.Timeout = TimeSpan.FromMinutes(10);
        _client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ConfigScope", CurrentVersion.ToString()));
        _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _updatesRoot = updatesRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OneCChangeMonitor",
            "updates");
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
        var expectedInstallerName = $"ConfigScope-Setup-{version}.exe";
        var installerUri = FindAsset(root, expectedInstallerName);
        var checksumUri = FindAsset(root, expectedInstallerName + ".sha256");
        return new UpdateInfo(
            version,
            tag,
            string.IsNullOrWhiteSpace(title) ? $"Версия {tag}" : title!,
            notes ?? string.Empty,
            new Uri(url),
            installerUri,
            checksumUri,
            version > CurrentVersion);
    }

    public async Task<string> DownloadInstallerAsync(UpdateInfo update, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        if (!update.IsNewer) throw new InvalidOperationException("Установлена актуальная версия ConfigScope.");
        if (update.InstallerUri is null || update.ChecksumUri is null)
            throw new InvalidOperationException("В релизе отсутствует установщик или его контрольная сумма.");

        var updateDirectory = Path.Combine(_updatesRoot, update.Version.ToString());
        Directory.CreateDirectory(updateDirectory);
        var installerPath = Path.Combine(updateDirectory, $"ConfigScope-Setup-{update.Version}.exe");
        var temporaryPath = installerPath + ".download";

        var checksumText = await _client.GetStringAsync(update.ChecksumUri, cancellationToken);
        var expectedHash = ParseChecksum(checksumText);
        using (var response = await _client.GetAsync(update.InstallerUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
        {
            response.EnsureSuccessStatusCode();
            var totalLength = response.Content.Headers.ContentLength;
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var destination = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
            var buffer = new byte[81920];
            long downloaded = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                downloaded += read;
                if (totalLength is > 0) progress?.Report((double)downloaded / totalLength.Value);
            }
        }

        bool checksumMatches;
        await using (var installer = File.OpenRead(temporaryPath))
        {
            var actualHash = await SHA256.HashDataAsync(installer, cancellationToken);
            checksumMatches = CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
        }
        if (!checksumMatches)
        {
            File.Delete(temporaryPath);
            throw new InvalidOperationException("Контрольная сумма установщика не совпала. Обновление отменено.");
        }

        File.Move(temporaryPath, installerPath, true);
        progress?.Report(1);
        return installerPath;
    }

    public static void ScheduleInstallerAfterExit(string installerPath)
    {
        if (!File.Exists(installerPath)) throw new FileNotFoundException("Установщик обновления не найден.", installerPath);
        var escapedPath = installerPath.Replace("'", "''", StringComparison.Ordinal);
        var script = $"$process = Get-Process -Id {Environment.ProcessId} -ErrorAction SilentlyContinue; " +
                     "$process | ForEach-Object {{ $_.WaitForExit() }}; " +
                     $"Start-Process -FilePath '{escapedPath}' -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/CLOSEAPPLICATIONS') -WindowStyle Hidden";
        var encodedScript = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var startInfo = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-WindowStyle");
        startInfo.ArgumentList.Add("Hidden");
        startInfo.ArgumentList.Add("-EncodedCommand");
        startInfo.ArgumentList.Add(encodedScript);
        _ = Process.Start(startInfo) ?? throw new InvalidOperationException("Не удалось подготовить установку обновления.");
    }

    private static Uri? FindAsset(JsonElement release, string expectedName)
    {
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return null;
        foreach (var asset in assets.EnumerateArray())
        {
            if (!asset.TryGetProperty("name", out var name) || !string.Equals(name.GetString(), expectedName, StringComparison.OrdinalIgnoreCase)) continue;
            if (asset.TryGetProperty("browser_download_url", out var url) && Uri.TryCreate(url.GetString(), UriKind.Absolute, out var uri)) return uri;
        }
        return null;
    }

    private static byte[] ParseChecksum(string value)
    {
        var hash = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        if (hash is null || hash.Length != 64 || hash.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidOperationException("Файл контрольной суммы имеет неверный формат.");
        return Convert.FromHexString(hash);
    }
}
