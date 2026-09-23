using OneCChangeMonitor.Domain;

namespace OneCChangeMonitor.Application;

public interface IProjectCatalog
{
    IReadOnlyList<RepositoryProject> GetAll();
    RepositoryProject? Find(string id);
}

public interface IGitRepositoryReader
{
    Task<IReadOnlyList<string>> GetBranchesAsync(RepositoryProject project, CancellationToken cancellationToken);
    Task<IReadOnlyList<CommitSummary>> GetCommitsAsync(RepositoryProject project, string branch, int limit, CancellationToken cancellationToken);
    Task<CommitDetails?> GetCommitAsync(RepositoryProject project, string sha, CancellationToken cancellationToken);
    Task<FileDiff> GetDiffAsync(RepositoryProject project, string sha, string path, CancellationToken cancellationToken);
    Task FetchAsync(RepositoryProject project, CancellationToken cancellationToken);
}

public interface IOneCPathClassifier
{
    OneCObjectReference? Classify(RepositoryProject project, string path);
}

public sealed class ChangeMonitorService(IProjectCatalog projects, IGitRepositoryReader git)
{
    public IReadOnlyList<RepositoryProject> GetProjects() => projects.GetAll();

    public Task<IReadOnlyList<string>> GetBranchesAsync(string projectId, CancellationToken token) =>
        git.GetBranchesAsync(GetRequiredProject(projectId), token);

    public Task<IReadOnlyList<CommitSummary>> GetCommitsAsync(string projectId, string? branch, int limit, CancellationToken token)
    {
        var project = GetRequiredProject(projectId);
        return git.GetCommitsAsync(project, string.IsNullOrWhiteSpace(branch) ? project.DefaultBranch : branch, Math.Clamp(limit, 1, 200), token);
    }

    public Task<CommitDetails?> GetCommitAsync(string projectId, string sha, CancellationToken token) => git.GetCommitAsync(GetRequiredProject(projectId), sha, token);
    public Task<FileDiff> GetDiffAsync(string projectId, string sha, string path, CancellationToken token) => git.GetDiffAsync(GetRequiredProject(projectId), sha, path, token);
    public Task FetchAsync(string projectId, CancellationToken token) => git.FetchAsync(GetRequiredProject(projectId), token);

    private RepositoryProject GetRequiredProject(string id) => projects.Find(id) ?? throw new KeyNotFoundException($"Проект '{id}' не найден.");
}
