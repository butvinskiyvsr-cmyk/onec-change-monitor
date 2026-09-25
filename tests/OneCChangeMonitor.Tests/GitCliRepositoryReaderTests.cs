using System.Diagnostics;
using System.Text;
using OneCChangeMonitor.Domain;
using OneCChangeMonitor.Infrastructure;

namespace OneCChangeMonitor.Tests;

public sealed class GitCliRepositoryReaderTests
{
    [Fact]
    public async Task MergeCommitIsComparedWithFirstParent()
    {
        using var repository = TestRepository.Create();
        repository.Write("README.md", "base");
        repository.Git("add", ".");
        repository.Git("commit", "-m", "initial");
        repository.Git("checkout", "-b", "Develop");
        const string path = "src/cf/Documents/ЗаказПокупателя/Ext/ObjectModule.bsl";
        repository.Write(path, "Процедура Проверка()\nКонецПроцедуры");
        repository.Git("add", ".");
        repository.Git("commit", "-m", "feature");
        repository.Git("checkout", "master");
        repository.Write("master.txt", "master change");
        repository.Git("add", ".");
        repository.Git("commit", "-m", "master change");
        repository.Git("merge", "--no-ff", "Develop", "-m", "Merge branch 'Develop'");

        var reader = new GitCliRepositoryReader(new OneCPathClassifier());
        var project = repository.AsProject();
        var sha = repository.Git("rev-parse", "HEAD").Trim();

        var details = await reader.GetCommitAsync(project, sha, CancellationToken.None);
        var diff = await reader.GetDiffAsync(project, sha, path, CancellationToken.None);

        Assert.NotNull(details);
        Assert.Equal(2, details.ParentCount);
        var file = Assert.Single(details.Files);
        Assert.Equal(path, file.Path);
        Assert.True(file.AddedLines > 0);
        Assert.Contains("Процедура Проверка", diff.Content);
    }

    [Fact]
    public async Task RemoteBranchIsUsedAfterFetchWhenLocalBranchIsBehind()
    {
        using var remote = TestRepository.Create(bare: true);
        using var publisher = TestRepository.Create();
        publisher.Write("README.md", "one");
        publisher.Git("add", ".");
        publisher.Git("commit", "-m", "initial");
        publisher.Git("remote", "add", "origin", remote.Path);
        publisher.Git("push", "-u", "origin", "master");

        using var consumer = TestRepository.Clone(remote.Path);
        publisher.Write("README.md", "two");
        publisher.Git("add", ".");
        publisher.Git("commit", "-m", "remote update");
        publisher.Git("push", "origin", "master");

        var reader = new GitCliRepositoryReader(new OneCPathClassifier());
        var project = consumer.AsProject(remote.Path);
        await reader.FetchAsync(project, CancellationToken.None);

        var status = await reader.GetBranchStatusAsync(project, "master", CancellationToken.None);
        var commits = await reader.GetCommitsAsync(project, "master", 10, CancellationToken.None);

        Assert.Equal(0, status.Ahead);
        Assert.Equal(1, status.Behind);
        Assert.Equal("origin/master", status.ActiveReference);
        Assert.Equal("remote update", commits[0].Subject);
    }

    private sealed class TestRepository : IDisposable
    {
        private TestRepository(string path) => Path = path;

        public string Path { get; }

        public static TestRepository Create(bool bare = false)
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "configscope-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            Run(path, "init", bare ? ["--bare", "--initial-branch=master"] : ["--initial-branch=master"]);
            var repository = new TestRepository(path);
            if (!bare) repository.ConfigureIdentity();
            return repository;
        }

        public static TestRepository Clone(string source)
        {
            var parent = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "configscope-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(parent);
            var path = System.IO.Path.Combine(parent, "clone");
            Run(parent, "clone", [source, path]);
            var repository = new TestRepository(path);
            repository.ConfigureIdentity();
            return repository;
        }

        public RepositoryProject AsProject(string remoteUrl = "") =>
            new("test", "Test", Path, remoteUrl, "master", ["src/cf"]);

        public void Write(string relativePath, string content)
        {
            var target = System.IO.Path.Combine(Path, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
            File.WriteAllText(target, content, new UTF8Encoding(false));
        }

        public string Git(params string[] arguments) => Run(Path, arguments[0], arguments[1..]);

        public void Dispose()
        {
            try { Directory.Delete(System.IO.Path.GetDirectoryName(Path) is { } parent && System.IO.Path.GetFileName(Path) == "clone" ? parent : Path, true); }
            catch { }
        }

        private void ConfigureIdentity()
        {
            Git("config", "user.name", "ConfigScope Tests");
            Git("config", "user.email", "configscope@example.invalid");
        }

        private static string Run(string workingDirectory, string command, IReadOnlyList<string> arguments)
        {
            var start = new ProcessStartInfo("git")
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            start.ArgumentList.Add(command);
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Git не запущен.");
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0) throw new InvalidOperationException($"git {command}: {error}");
            return output;
        }
    }
}
