using System.IO;
using System.Text.Json;
using OneCChangeMonitor.Domain;

namespace OneCChangeMonitor.Desktop;

public sealed class DesktopSettings
{
    public List<ProjectSettings> Projects { get; init; } = [];

    public static string UserSettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OneCChangeMonitor",
        "settings.json");

    public static async Task<DesktopSettings?> TryLoadAsync(CancellationToken cancellationToken = default)
    {
        var candidates = new[]
        {
            UserSettingsPath,
            Path.Combine(AppContext.BaseDirectory, "appsettings.Local.json"),
            Path.Combine(Environment.CurrentDirectory, "src", "OneCChangeMonitor.Desktop", "appsettings.Local.json")
        };
        var path = candidates.FirstOrDefault(File.Exists);
        if (path is null) return null;

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<DesktopSettings>(stream, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }, cancellationToken) ?? new DesktopSettings();
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(UserSettingsPath)!);
        await using var stream = File.Create(UserSettingsPath);
        await JsonSerializer.SerializeAsync(stream, this, new JsonSerializerOptions { WriteIndented = true }, cancellationToken);
    }
}

public sealed class ProjectSettings
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string LocalPath { get; init; } = string.Empty;
    public string RemoteUrl { get; init; } = string.Empty;
    public string DefaultBranch { get; init; } = "master";
    public string[] SourceRoots { get; init; } = [];

    public RepositoryProject ToDomain() => new(
        Id,
        Name,
        Path.GetFullPath(LocalPath),
        RemoteUrl,
        DefaultBranch,
        SourceRoots.Length == 0 ? ["src/cf", "src/cfe", "src/epf", "src/erf"] : SourceRoots);
}

public sealed record CommitItem(CommitSummary Source)
{
    public string Sha => Source.Sha;
    public string ShortSha => Source.ShortSha;
    public string Subject => Source.Subject;
    public string Meta => $"{Source.Author} · {Source.AuthoredAt.LocalDateTime:g}";
}

public sealed record ChangedFileItem(ChangedFile Source)
{
    public string Status => Source.Status.Length > 1 ? Source.Status[..1] : Source.Status;
    public string Path => Source.Path;
    public string Title => Source.OneCObject is null
        ? Source.Path
        : $"{Source.OneCObject.ObjectType}.{Source.OneCObject.ObjectName}";
    public string Subtitle => Source.OneCObject?.Component ?? Source.Path;
}
