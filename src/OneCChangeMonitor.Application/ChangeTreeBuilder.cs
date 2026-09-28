using OneCChangeMonitor.Domain;

namespace OneCChangeMonitor.Application;

public sealed class ChangeTreeBuilder : IChangeTreeBuilder
{
    private static readonly IReadOnlyDictionary<string, int> RiskRanks = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["Информация"] = 1,
        ["Требует проверки"] = 2,
        ["Средний риск"] = 3,
        ["Высокий риск"] = 4,
        ["Критический"] = 5
    };

    public ChangeTreeNode Build(CommitDetails commit)
    {
        ArgumentNullException.ThrowIfNull(commit);
        var recognized = commit.Files.Where(file => file.OneCObject is not null).ToArray();
        var sources = recognized
            .GroupBy(file => file.OneCObject!.SourceKind, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.CurrentCultureIgnoreCase)
            .Select(group => BuildSource(commit.Commit.Sha, group.Key, group))
            .ToList();

        var otherFiles = commit.Files.Where(file => file.OneCObject is null).ToArray();
        if (otherFiles.Length > 0) sources.Add(BuildOtherFiles(commit.Commit.Sha, otherFiles));

        return Aggregate(
            $"commit:{commit.Commit.Sha}",
            "Изменения коммита",
            ChangeTreeNodeKind.Source,
            null,
            commit.ParentCount > 1 ? "Merge-коммит, сравнение с первым родителем" : commit.Commit.Subject,
            sources);
    }

    private static ChangeTreeNode BuildSource(string commitSha, string sourceName, IEnumerable<ChangedFile> files)
    {
        var sourceId = $"commit:{commitSha}/source:{sourceName}";
        var metadataTypes = files
            .GroupBy(file => file.OneCObject!.ObjectType, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.CurrentCultureIgnoreCase)
            .Select(group => BuildMetadataType(sourceId, group.Key, group))
            .ToArray();
        return Aggregate(sourceId, sourceName, ChangeTreeNodeKind.Source, null, null, metadataTypes);
    }

    private static ChangeTreeNode BuildMetadataType(string parentId, string metadataType, IEnumerable<ChangedFile> files)
    {
        var typeId = $"{parentId}/type:{metadataType}";
        var objects = files
            .GroupBy(file => file.OneCObject!.ObjectName, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.CurrentCultureIgnoreCase)
            .Select(group => BuildObject(typeId, group.Key, group))
            .ToArray();
        return Aggregate(typeId, metadataType, ChangeTreeNodeKind.MetadataType, null, null, objects);
    }

    private static ChangeTreeNode BuildObject(string parentId, string objectName, IEnumerable<ChangedFile> files)
    {
        var objectId = $"{parentId}/object:{objectName}";
        var materialized = files.ToArray();
        var components = materialized
            .GroupBy(GetComponentName, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.CurrentCultureIgnoreCase)
            .Select(group => BuildComponent(objectId, group.Key, group))
            .ToArray();
        return Aggregate(objectId, objectName, ChangeTreeNodeKind.Object, null, $"Файлов: {materialized.Length}", components);
    }

    private static ChangeTreeNode BuildComponent(string parentId, string componentName, IEnumerable<ChangedFile> files)
    {
        var componentId = $"{parentId}/component:{componentName}";
        var children = files
            .OrderBy(file => file.Path, StringComparer.CurrentCultureIgnoreCase)
            .Select(file => BuildFile(componentId, file))
            .ToArray();
        return Aggregate(componentId, componentName, ChangeTreeNodeKind.Component, null, null, children);
    }

    private static ChangeTreeNode BuildOtherFiles(string commitSha, IEnumerable<ChangedFile> files)
    {
        var sourceId = $"commit:{commitSha}/source:other";
        var children = files
            .OrderBy(file => file.Path, StringComparer.CurrentCultureIgnoreCase)
            .Select(file => BuildFile(sourceId, file))
            .ToArray();
        return Aggregate(sourceId, "Прочие файлы", ChangeTreeNodeKind.Source, null, null, children);
    }

    private static ChangeTreeNode BuildFile(string parentId, ChangedFile file)
    {
        var changeType = ParseChangeType(file.Status);
        var description = changeType == ChangeType.Renamed && !string.IsNullOrWhiteSpace(file.PreviousPath)
            ? $"Переименован из {file.PreviousPath}"
            : file.Path;
        return new ChangeTreeNode(
            $"{parentId}/file:{file.Path}",
            Path.GetFileName(file.Path),
            ChangeTreeNodeKind.File,
            changeType,
            file.Path,
            description,
            file.AddedLines ?? 0,
            file.DeletedLines ?? 0,
            GetFileRisk(file),
            []);
    }

    private static ChangeTreeNode Aggregate(
        string id,
        string name,
        ChangeTreeNodeKind kind,
        string? path,
        string? description,
        IReadOnlyList<ChangeTreeNode> children) => new(
            id,
            name,
            kind,
            AggregateChangeType(children),
            path,
            description,
            children.Sum(child => child.AddedLines),
            children.Sum(child => child.DeletedLines),
            MaxRisk(children.Select(child => child.RiskLevel)),
            children);

    private static ChangeType AggregateChangeType(IReadOnlyList<ChangeTreeNode> children)
    {
        if (children.Count == 0) return ChangeType.Unknown;
        var distinct = children.Select(child => child.ChangeType).Distinct().ToArray();
        return distinct.Length == 1 ? distinct[0] : ChangeType.Modified;
    }

    private static ChangeType ParseChangeType(string status)
    {
        if (string.IsNullOrWhiteSpace(status)) return ChangeType.Unknown;
        return char.ToUpperInvariant(status[0]) switch
        {
            'A' => ChangeType.Added,
            'M' => ChangeType.Modified,
            'D' => ChangeType.Deleted,
            'R' => ChangeType.Renamed,
            'U' => ChangeType.Unchanged,
            _ => ChangeType.Unknown
        };
    }

    private static string GetComponentName(ChangedFile file)
    {
        var normalized = file.Path.Replace('\\', '/');
        if (file.OneCObject?.Component?.StartsWith("Форма:", StringComparison.OrdinalIgnoreCase) == true)
            return file.OneCObject.Component;
        if (normalized.EndsWith("/ObjectModule.bsl", StringComparison.OrdinalIgnoreCase)) return "Модуль объекта";
        if (normalized.EndsWith("/ManagerModule.bsl", StringComparison.OrdinalIgnoreCase)) return "Модуль менеджера";
        if (normalized.EndsWith("/RecordSetModule.bsl", StringComparison.OrdinalIgnoreCase)) return "Модуль набора записей";
        if (normalized.EndsWith("/CommandModule.bsl", StringComparison.OrdinalIgnoreCase)) return "Модуль команды";
        if (normalized.EndsWith("/Rights.xml", StringComparison.OrdinalIgnoreCase)) return "Права";
        if (file.OneCObject?.IsCode == true) return "Модули";
        return file.OneCObject?.Component ?? "Свойства объекта";
    }

    private static string? GetFileRisk(ChangedFile file)
    {
        if (file.OneCObject?.IsSuspicious == true) return "Высокий риск";
        if (file.Path.Contains("/Roles/", StringComparison.OrdinalIgnoreCase) || file.Path.EndsWith("Rights.xml", StringComparison.OrdinalIgnoreCase))
            return "Высокий риск";
        return null;
    }

    private static string? MaxRisk(IEnumerable<string?> risks) => risks
        .Where(risk => !string.IsNullOrWhiteSpace(risk))
        .OrderByDescending(risk => RiskRanks.TryGetValue(risk!, out var rank) ? rank : 0)
        .FirstOrDefault();
}
