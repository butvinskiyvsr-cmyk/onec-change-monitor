namespace OneCChangeMonitor.Domain;

public sealed record RepositoryProject(string Id, string Name, string LocalPath, string RemoteUrl, string DefaultBranch, IReadOnlyList<string> SourceRoots);

public sealed record CommitSummary(string Sha, string ShortSha, string Author, string Email, DateTimeOffset AuthoredAt, string Subject);

public sealed record OneCObjectReference(string SourceKind, string ObjectType, string ObjectName, string? Component, bool IsCode, bool IsSuspicious);

public sealed record ChangedFile(
    string Status,
    string Path,
    string? PreviousPath,
    OneCObjectReference? OneCObject,
    int? AddedLines = null,
    int? DeletedLines = null);

public sealed record CommitDetails(
    CommitSummary Commit,
    IReadOnlyList<ChangedFile> Files,
    int ParentCount = 0,
    string? ComparedTo = null);

public sealed record FileDiff(string CommitSha, string Path, string Content, bool IsBinary);

public sealed record BranchStatus(
    string Branch,
    string ActiveReference,
    string? LocalSha,
    string? RemoteSha,
    int Ahead,
    int Behind)
{
    public string State => LocalSha is null ? "только удалённая ветка"
        : RemoteSha is null ? "только локальная ветка"
        : Ahead == 0 && Behind == 0 ? "актуальна"
        : Ahead == 0 ? $"локальная отстаёт на {Behind}"
        : Behind == 0 ? $"локальная опережает на {Ahead}"
        : $"ветки разошлись: +{Ahead} / −{Behind}";
}
