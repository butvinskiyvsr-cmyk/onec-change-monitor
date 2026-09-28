using OneCChangeMonitor.Application;
using OneCChangeMonitor.Domain;

namespace OneCChangeMonitor.Infrastructure;

public sealed class FileSystemConfigurationObjectIndex(IOneCPathClassifier classifier) : IConfigurationObjectIndex
{
    public Task<IReadOnlyList<ConfigurationObject>> BuildAsync(RepositoryProject project, CancellationToken cancellationToken) =>
        Task.Run(() => Build(project, cancellationToken), cancellationToken);

    private IReadOnlyList<ConfigurationObject> Build(RepositoryProject project, CancellationToken token)
    {
        var entries = new List<(string Path, OneCObjectReference Reference)>();
        foreach (var sourceRoot in project.SourceRoots)
        {
            token.ThrowIfCancellationRequested();
            var normalizedRoot = sourceRoot.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            var absoluteRoot = Path.GetFullPath(Path.Combine(project.LocalPath, normalizedRoot));
            if (!Directory.Exists(absoluteRoot)) continue;

            foreach (var absolutePath in Directory.EnumerateFiles(absoluteRoot, "*", SearchOption.AllDirectories))
            {
                token.ThrowIfCancellationRequested();
                var relativePath = Path.GetRelativePath(project.LocalPath, absolutePath).Replace('\\', '/');
                var reference = classifier.Classify(project, relativePath);
                if (reference is not null) entries.Add((relativePath, reference));
            }
        }

        return entries
            .GroupBy(entry => (entry.Reference.SourceKind, entry.Reference.ObjectType, entry.Reference.ObjectName))
            .Select(group =>
            {
                var files = group.Select(entry => entry.Path).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.CurrentCultureIgnoreCase).ToArray();
                var components = group.Select(entry => GetComponent(entry.Path, entry.Reference))
                    .Distinct(StringComparer.CurrentCultureIgnoreCase).Order(StringComparer.CurrentCultureIgnoreCase).ToArray();
                var first = files[0];
                var parts = first.Split('/');
                var objectIndex = Array.FindIndex(parts, part =>
                    string.Equals(Path.GetFileNameWithoutExtension(part), group.Key.ObjectName, StringComparison.OrdinalIgnoreCase));
                var basePath = objectIndex >= 0
                    ? string.Join('/', parts.Take(objectIndex).Append(group.Key.ObjectName))
                    : first;
                return new ConfigurationObject(
                    group.Key.SourceKind,
                    group.Key.ObjectType,
                    group.Key.ObjectName,
                    components,
                    files,
                    [basePath, basePath + ".xml"]);
            })
            .OrderBy(item => item.SourceKind, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.ObjectType, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private static string GetComponent(string path, OneCObjectReference reference)
    {
        if (!string.IsNullOrWhiteSpace(reference.Component)) return reference.Component;
        if (path.EndsWith(".bsl", StringComparison.OrdinalIgnoreCase)) return "Модуль";
        if (path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) return "Метаданные";
        return "Файлы объекта";
    }
}
