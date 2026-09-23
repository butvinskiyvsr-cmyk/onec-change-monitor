using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using OneCChangeMonitor.Application;
using OneCChangeMonitor.Domain;

namespace OneCChangeMonitor.Desktop;

public sealed class DesktopSettings
{
    public List<ProjectSettings> Projects { get; init; } = [];
    public bool CheckForUpdatesOnStartup { get; set; } = true;

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
    public string Author => Source.Author;
    public string Meta => $"{Source.Author} · {Source.AuthoredAt.LocalDateTime:g}";
    public string RelativeTime => FormatRelativeTime(Source.AuthoredAt);

    private static string FormatRelativeTime(DateTimeOffset value)
    {
        var local = value.LocalDateTime;
        var elapsed = DateTime.Now - local;
        if (elapsed.TotalMinutes < 2) return "сейчас";
        if (elapsed.TotalHours < 1) return $"{Math.Max(2, (int)elapsed.TotalMinutes)} мин";
        if (local.Date == DateTime.Today) return local.ToString("HH:mm");
        if (local.Date == DateTime.Today.AddDays(-1)) return $"вчера {local:HH:mm}";
        return local.ToString("dd.MM.yy");
    }
}

public sealed record ChangedFileItem(ChangedFile Source)
{
    public string Status => Source.Status.Length > 1 ? Source.Status[..1] : Source.Status;
    public string Path => Source.Path;
    public string Title => Source.OneCObject is null
        ? Source.Path
        : $"{Source.OneCObject.ObjectType}.{Source.OneCObject.ObjectName}";
    public string Subtitle => Source.OneCObject?.Component ?? Source.Path;
    public string KindGlyph => Source.OneCObject?.IsCode == true ? "</>" : "XML";
    public bool IsCode => Source.OneCObject?.IsCode == true || Path.EndsWith(".bsl", StringComparison.OrdinalIgnoreCase);
    public bool IsXml => Path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase);
}

public sealed record ObjectListItem(string Title, string Description);

public sealed record QualityFinding(string Title, string Description, string Severity);

public sealed class DiffDisplayRow
{
    private static readonly Brush NormalBackground = Freeze(Color.FromRgb(251, 252, 254));
    private static readonly Brush AddedBackgroundBrush = Freeze(Color.FromRgb(226, 246, 235));
    private static readonly Brush RemovedBackgroundBrush = Freeze(Color.FromRgb(253, 231, 233));
    private static readonly Brush HeaderBackgroundBrush = Freeze(Color.FromRgb(232, 241, 255));
    private static readonly Brush ConflictBackgroundBrush = Freeze(Color.FromRgb(255, 240, 205));
    private static readonly Brush NormalForeground = Freeze(Color.FromRgb(35, 44, 58));
    private static readonly Brush MutedForeground = Freeze(Color.FromRgb(82, 103, 128));
    private static readonly Brush AddedForegroundBrush = Freeze(Color.FromRgb(20, 108, 67));
    private static readonly Brush RemovedForegroundBrush = Freeze(Color.FromRgb(165, 50, 58));
    private static readonly Brush ConflictForegroundBrush = Freeze(Color.FromRgb(133, 81, 0));

    public DiffDisplayRow(SideBySideDiffRow source, bool wrapText = true)
    {
        OldNumber = source.OldNumber?.ToString() ?? string.Empty;
        NewNumber = source.NewNumber?.ToString() ?? string.Empty;
        OldText = source.OldText;
        NewText = source.NewText;
        (OldBackground, OldForeground) = GetColors(source.OldKind);
        (NewBackground, NewForeground) = GetColors(source.NewKind);
        TextWrapping = wrapText ? TextWrapping.Wrap : TextWrapping.NoWrap;
    }

    public string OldNumber { get; }
    public string NewNumber { get; }
    public string OldText { get; }
    public string NewText { get; }
    public Brush OldBackground { get; }
    public Brush NewBackground { get; }
    public Brush OldForeground { get; }
    public Brush NewForeground { get; }
    public TextWrapping TextWrapping { get; }

    private static (Brush Background, Brush Foreground) GetColors(DiffLineKind kind) => kind switch
    {
        DiffLineKind.Added => (AddedBackgroundBrush, AddedForegroundBrush),
        DiffLineKind.Removed => (RemovedBackgroundBrush, RemovedForegroundBrush),
        DiffLineKind.Header => (HeaderBackgroundBrush, MutedForeground),
        DiffLineKind.Conflict => (ConflictBackgroundBrush, ConflictForegroundBrush),
        _ => (NormalBackground, NormalForeground)
    };

    private static Brush Freeze(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}

public sealed class SingleDiffDisplayRow
{
    public SingleDiffDisplayRow(int? number, string text, DiffLineKind kind, bool wrapText)
    {
        Number = number?.ToString() ?? string.Empty;
        Text = text;
        TextWrapping = wrapText ? TextWrapping.Wrap : TextWrapping.NoWrap;
        (Background, Foreground) = DiffDisplayRowColors.Get(kind);
    }

    public string Number { get; }
    public string Text { get; }
    public Brush Background { get; }
    public Brush Foreground { get; }
    public TextWrapping TextWrapping { get; }
}

internal static class DiffDisplayRowColors
{
    private static readonly Brush NormalBackground = Freeze(Color.FromRgb(251, 252, 254));
    private static readonly Brush AddedBackground = Freeze(Color.FromRgb(226, 246, 235));
    private static readonly Brush RemovedBackground = Freeze(Color.FromRgb(253, 231, 233));
    private static readonly Brush HeaderBackground = Freeze(Color.FromRgb(232, 241, 255));
    private static readonly Brush ConflictBackground = Freeze(Color.FromRgb(255, 240, 205));
    private static readonly Brush NormalForeground = Freeze(Color.FromRgb(35, 44, 58));
    private static readonly Brush MutedForeground = Freeze(Color.FromRgb(82, 103, 128));
    private static readonly Brush AddedForeground = Freeze(Color.FromRgb(20, 108, 67));
    private static readonly Brush RemovedForeground = Freeze(Color.FromRgb(165, 50, 58));
    private static readonly Brush ConflictForeground = Freeze(Color.FromRgb(133, 81, 0));

    public static (Brush Background, Brush Foreground) Get(DiffLineKind kind) => kind switch
    {
        DiffLineKind.Added => (AddedBackground, AddedForeground),
        DiffLineKind.Removed => (RemovedBackground, RemovedForeground),
        DiffLineKind.Header => (HeaderBackground, MutedForeground),
        DiffLineKind.Conflict => (ConflictBackground, ConflictForeground),
        _ => (NormalBackground, NormalForeground)
    };

    private static Brush Freeze(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
