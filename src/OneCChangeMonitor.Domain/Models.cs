namespace OneCChangeMonitor.Domain;

public sealed record RepositoryProject(string Id, string Name, string LocalPath, string RemoteUrl, string DefaultBranch, IReadOnlyList<string> SourceRoots);

public sealed record CommitSummary(string Sha, string ShortSha, string Author, string Email, DateTimeOffset AuthoredAt, string Subject);

public sealed record OneCObjectReference(string SourceKind, string ObjectType, string ObjectName, string? Component, bool IsCode, bool IsSuspicious);

public sealed record ChangedFile(string Status, string Path, string? PreviousPath, OneCObjectReference? OneCObject);

public sealed record CommitDetails(CommitSummary Commit, IReadOnlyList<ChangedFile> Files);

public sealed record FileDiff(string CommitSha, string Path, string Content, bool IsBinary);
