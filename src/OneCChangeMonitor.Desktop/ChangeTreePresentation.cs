using OneCChangeMonitor.Domain;

namespace OneCChangeMonitor.Desktop;

public enum ChangeTreeFilter
{
    Code,
    Xml,
    All
}

public sealed class ChangeTreeItem
{
    private ChangeTreeItem(ChangeTreeNode node, IReadOnlyList<ChangeTreeItem> children)
    {
        Node = node;
        Children = children;
        IsExpanded = node.Kind == ChangeTreeNodeKind.Source;
    }

    public ChangeTreeNode Node { get; }
    public IReadOnlyList<ChangeTreeItem> Children { get; }
    public string Name => Node.Name;
    public string? Path => Node.Path;
    public bool IsFile => Node.Kind == ChangeTreeNodeKind.File;
    public bool IsExpanded { get; set; }
    public bool IsSelected { get; set; }
    public string KindText => Node.Kind switch
    {
        ChangeTreeNodeKind.Source => "ИСТОЧНИК",
        ChangeTreeNodeKind.MetadataType => "ТИП",
        ChangeTreeNodeKind.Object => "ОБЪЕКТ",
        ChangeTreeNodeKind.Component => "КОМПОНЕНТ",
        ChangeTreeNodeKind.File => Path?.EndsWith(".bsl", StringComparison.OrdinalIgnoreCase) == true ? "BSL" : "XML",
        _ => "ИЗМЕНЕНИЕ"
    };
    public string StatusText => Node.ChangeType switch
    {
        ChangeType.Added => "A",
        ChangeType.Modified => "M",
        ChangeType.Deleted => "D",
        ChangeType.Renamed => "R",
        ChangeType.Unchanged => "=",
        _ => "?"
    };
    public string StatsText => Node.AddedLines == 0 && Node.DeletedLines == 0
        ? string.Empty
        : $"+{Node.AddedLines} −{Node.DeletedLines}";
    public string RiskText => Node.RiskLevel ?? string.Empty;
    public string DetailText => IsFile ? Node.Description ?? Node.Path ?? string.Empty : $"{CountFiles(Node)} файл(ов)";

    public static IReadOnlyList<ChangeTreeItem> Build(
        ChangeTreeNode root,
        IReadOnlyCollection<ChangedFileItem> files,
        ChangeTreeFilter filter,
        string? query = null)
    {
        var acceptedPaths = files.Where(file => MatchesFilter(file, filter))
            .Select(file => file.Path)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var normalizedQuery = query?.Trim() ?? string.Empty;
        return root.Children
            .Select(child => BuildFiltered(child, acceptedPaths, normalizedQuery, ancestorMatches: false))
            .Where(item => item is not null)
            .Cast<ChangeTreeItem>()
            .ToArray();
    }

    private static ChangeTreeItem? BuildFiltered(ChangeTreeNode node, ISet<string> acceptedPaths, string query, bool ancestorMatches)
    {
        var matches = ancestorMatches || MatchesQuery(node, query);
        if (node.Kind == ChangeTreeNodeKind.File)
            return node.Path is not null && acceptedPaths.Contains(node.Path) && matches ? new ChangeTreeItem(node, []) : null;

        var children = node.Children
            .Select(child => BuildFiltered(child, acceptedPaths, query, matches))
            .Where(item => item is not null)
            .Cast<ChangeTreeItem>()
            .ToArray();
        return children.Length == 0 ? null : new ChangeTreeItem(node, children);
    }

    private static bool MatchesQuery(ChangeTreeNode node, string query) => string.IsNullOrWhiteSpace(query)
        || node.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
        || (node.Path?.Contains(query, StringComparison.CurrentCultureIgnoreCase) ?? false)
        || (node.Description?.Contains(query, StringComparison.CurrentCultureIgnoreCase) ?? false);

    private static bool MatchesFilter(ChangedFileItem file, ChangeTreeFilter filter) => filter switch
    {
        ChangeTreeFilter.Code => file.IsCode,
        ChangeTreeFilter.Xml => file.IsXml && !file.IsCode,
        _ => true
    };

    private static int CountFiles(ChangeTreeNode node) => node.Kind == ChangeTreeNodeKind.File
        ? 1
        : node.Children.Sum(CountFiles);
}
