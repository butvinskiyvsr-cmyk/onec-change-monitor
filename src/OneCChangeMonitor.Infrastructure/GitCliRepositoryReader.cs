using System.Diagnostics;
using System.Text;
using OneCChangeMonitor.Application;
using OneCChangeMonitor.Domain;

namespace OneCChangeMonitor.Infrastructure;

public sealed class GitCliRepositoryReader(IOneCPathClassifier classifier) : IGitRepositoryReader
{
    public async Task<IReadOnlyList<string>> GetBranchesAsync(RepositoryProject project, CancellationToken token)
    {
        var output = await RunGitAsync(project, token, "branch", "--format=%(refname:short)", "--all", "--no-color");
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => value.StartsWith("origin/", StringComparison.OrdinalIgnoreCase) ? value[7..] : value)
            .Where(value => !value.Equals("HEAD", StringComparison.OrdinalIgnoreCase) && !value.Equals("origin", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public async Task<IReadOnlyList<CommitSummary>> GetCommitsAsync(RepositoryProject project, string branch, int limit, CancellationToken token)
    {
        var reference = (await GetBranchStatusAsync(project, branch, token)).ActiveReference;
        var output = await RunGitAsync(project, token, "log", reference, $"-n{limit}", "--date=iso-strict", "--pretty=format:%H%x1f%h%x1f%an%x1f%ae%x1f%aI%x1f%s%x1e");
        return ParseCommits(output);
    }

    public async Task<BranchStatus> GetBranchStatusAsync(RepositoryProject project, string branch, CancellationToken token)
    {
        var localReference = await HasReferenceAsync(project, branch, token) ? branch : null;
        var remoteName = $"origin/{branch}";
        var remoteReference = await HasReferenceAsync(project, remoteName, token) ? remoteName : null;
        if (localReference is null && remoteReference is null)
            throw new InvalidOperationException($"Ветка '{branch}' не найдена.");

        var localSha = localReference is null ? null : (await RunGitAsync(project, token, "rev-parse", localReference)).Trim();
        var remoteSha = remoteReference is null ? null : (await RunGitAsync(project, token, "rev-parse", remoteReference)).Trim();
        var ahead = 0;
        var behind = 0;
        if (localReference is not null && remoteReference is not null)
        {
            var counts = (await RunGitAsync(project, token, "rev-list", "--left-right", "--count", $"{localReference}...{remoteReference}"))
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (counts.Length >= 2)
            {
                _ = int.TryParse(counts[0], out ahead);
                _ = int.TryParse(counts[1], out behind);
            }
        }

        var activeReference = remoteReference is null || (localReference is not null && ahead > 0 && behind == 0)
            ? localReference!
            : remoteReference;
        return new BranchStatus(branch, activeReference, localSha, remoteSha, ahead, behind);
    }

    public async Task<CommitDetails?> GetCommitAsync(RepositoryProject project, string sha, CancellationToken token)
    {
        ValidateSha(sha);
        var header = await RunGitAsync(project, token, "show", "-s", "--date=iso-strict", "--pretty=format:%H%x1f%h%x1f%an%x1f%ae%x1f%aI%x1f%s%x1e", sha);
        var commit = ParseCommits(header).SingleOrDefault();
        if (commit is null) return null;

        var parents = await GetParentsAsync(project, sha, token);
        var names = parents.Count == 0
            ? await RunGitAsync(project, token, "diff-tree", "--root", "--no-commit-id", "--name-status", "-r", "-M", sha)
            : await RunGitAsync(project, token, "diff", "--name-status", "-M", parents[0], sha, "--");
        var numStat = parents.Count == 0
            ? await RunGitAsync(project, token, "show", "--format=", "--numstat", sha)
            : await RunGitAsync(project, token, "diff", "--numstat", parents[0], sha, "--");
        var lineStats = ParseLineStats(numStat);
        var files = names.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ParseFile).Where(file => file is not null).Cast<ChangedFile>().ToArray();
        return new CommitDetails(commit, files, parents.Count, parents.FirstOrDefault());

        ChangedFile? ParseFile(string line)
        {
            var fields = line.Split('\t');
            if (fields.Length < 2) return null;
            var renamed = fields[0].StartsWith('R') && fields.Length >= 3;
            var path = renamed ? fields[2] : fields[1];
            lineStats.TryGetValue(path, out var stats);
            return new ChangedFile(fields[0], path, renamed ? fields[1] : null, classifier.Classify(project, path), stats.Added, stats.Deleted);
        }
    }

    public async Task<FileDiff> GetDiffAsync(RepositoryProject project, string sha, string path, CancellationToken token)
    {
        ValidateSha(sha);
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Contains("..", StringComparison.Ordinal))
            throw new ArgumentException("Некорректный путь файла.", nameof(path));
        var parents = await GetParentsAsync(project, sha, token);
        var content = parents.Count == 0
            ? await RunGitAsync(project, token, "show", "--format=", "--no-ext-diff", "--unified=6", sha, "--", path)
            : await RunGitAsync(project, token, "diff", "--no-ext-diff", "--unified=6", parents[0], sha, "--", path);
        return new FileDiff(sha, path, content, content.Contains("Binary files", StringComparison.OrdinalIgnoreCase));
    }

    public async Task FetchAsync(RepositoryProject project, CancellationToken token) => _ = await RunGitAsync(project, token, "fetch", "--prune", "origin");

    private static IReadOnlyList<CommitSummary> ParseCommits(string output) => output
        .Split('\x1e', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(record => record.Split('\x1f')).Where(fields => fields.Length >= 6 && DateTimeOffset.TryParse(fields[4], out _))
        .Select(fields => new CommitSummary(fields[0], fields[1], fields[2], fields[3], DateTimeOffset.Parse(fields[4]), fields[5])).ToArray();

    private static async Task<bool> HasReferenceAsync(RepositoryProject project, string reference, CancellationToken token)
    {
        var result = await RunGitProcessAsync(project, token, "rev-parse", "--verify", "--quiet", reference);
        return result.ExitCode == 0;
    }

    private static async Task<IReadOnlyList<string>> GetParentsAsync(RepositoryProject project, string sha, CancellationToken token)
    {
        var output = await RunGitAsync(project, token, "rev-list", "--parents", "-n", "1", sha);
        return output.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Skip(1).ToArray();
    }

    private static IReadOnlyDictionary<string, (int? Added, int? Deleted)> ParseLineStats(string output)
    {
        var result = new Dictionary<string, (int?, int?)>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var fields = line.Split('\t');
            if (fields.Length < 3) continue;
            var added = int.TryParse(fields[0], out var addedValue) ? (int?)addedValue : null;
            var deleted = int.TryParse(fields[1], out var deletedValue) ? (int?)deletedValue : null;
            result[fields[^1]] = (added, deleted);
        }
        return result;
    }

    private static void ValidateSha(string sha)
    {
        if (sha.Length is < 7 or > 64 || sha.Any(character => !Uri.IsHexDigit(character))) throw new ArgumentException("Некорректный идентификатор коммита.", nameof(sha));
    }

    private static async Task<string> RunGitAsync(RepositoryProject project, CancellationToken token, params string[] arguments)
    {
        var result = await RunGitProcessAsync(project, token, arguments);
        if (result.ExitCode != 0) throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.Error) ? "Команда Git завершилась с ошибкой." : result.Error.Trim());
        return result.Output;
    }

    private static async Task<GitResult> RunGitProcessAsync(RepositoryProject project, CancellationToken token, params string[] arguments)
    {
        if (!Directory.Exists(Path.Combine(project.LocalPath, ".git"))) throw new DirectoryNotFoundException($"Git-репозиторий не найден: {project.LocalPath}");
        var info = new ProcessStartInfo("git")
        {
            WorkingDirectory = project.LocalPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        info.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        info.ArgumentList.Add("-c");
        info.ArgumentList.Add("core.quotepath=false");
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Не удалось запустить git.");
        var outputTask = process.StandardOutput.ReadToEndAsync(token);
        var errorTask = process.StandardError.ReadToEndAsync(token);
        await process.WaitForExitAsync(token);
        return new GitResult(process.ExitCode, await outputTask, await errorTask);
    }

    private sealed record GitResult(int ExitCode, string Output, string Error);
}
