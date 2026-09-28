using OneCChangeMonitor.Application;
using OneCChangeMonitor.Domain;

namespace OneCChangeMonitor.Tests;

public sealed class ChangeTreeBuilderTests
{
    private readonly ChangeTreeBuilder _builder = new();

    [Fact]
    public void GroupsFilesByObjectAndSeparatesFormFromObjectModule()
    {
        var commit = CreateCommit(
            File("M", "src/cf/Documents/ЗаказПокупателя/Forms/ФормаДокумента/Ext/Form.xml", "Документ", "ЗаказПокупателя", "Форма: ФормаДокумента", added: 24, deleted: 8),
            File("M", "src/cf/Documents/ЗаказПокупателя/Forms/ФормаДокумента/Ext/Form/Module.bsl", "Документ", "ЗаказПокупателя", "Форма: ФормаДокумента", code: true, added: 18, deleted: 4),
            File("M", "src/cf/Documents/ЗаказПокупателя/Ext/ObjectModule.bsl", "Документ", "ЗаказПокупателя", "Модуль или свойства объекта", code: true, added: 12, deleted: 3));

        var tree = _builder.Build(commit);
        var source = Assert.Single(tree.Children);
        var metadataType = Assert.Single(source.Children);
        var objectNode = Assert.Single(metadataType.Children);

        Assert.Equal("ЗаказПокупателя", objectNode.Name);
        Assert.Equal(2, objectNode.Children.Count);
        Assert.Contains(objectNode.Children, node => node.Name == "Форма: ФормаДокумента" && node.Children.Count == 2);
        Assert.Contains(objectNode.Children, node => node.Name == "Модуль объекта" && node.Children.Count == 1);
        Assert.Equal(54, objectNode.AddedLines);
        Assert.Equal(15, objectNode.DeletedLines);
    }

    [Fact]
    public void PlacesUnrecognizedFilesIntoOtherFilesSource()
    {
        var tree = _builder.Build(CreateCommit(new ChangedFile("M", "README.md", null, null, 2, 1)));

        var other = Assert.Single(tree.Children);
        var file = Assert.Single(other.Children);
        Assert.Equal("Прочие файлы", other.Name);
        Assert.Equal(ChangeTreeNodeKind.File, file.Kind);
        Assert.Equal("README.md", file.Name);
        Assert.Equal(2, tree.AddedLines);
        Assert.Equal(1, tree.DeletedLines);
    }

    [Fact]
    public void PreservesUnknownMetadataTypeAndAggregatesMaximumRisk()
    {
        var commit = CreateCommit(
            File("M", "src/cf/CustomThings/Эксперимент/Ext/Module.bsl", "CustomThings", "Эксперимент", code: true, added: 3),
            File("M", "src/cf/Roles/Менеджер/Ext/Rights.xml", "Роль", "Менеджер", added: 10, deleted: 2),
            File("A", "src/cf/Roles/Менеджер/Ext/Rights.xml.orig", "Роль", "Менеджер", suspicious: true, added: 15));

        var tree = _builder.Build(commit);
        var source = Assert.Single(tree.Children);

        Assert.Contains(source.Children, node => node.Name == "CustomThings");
        var role = source.Children.Single(node => node.Name == "Роль").Children.Single();
        Assert.Equal("Высокий риск", role.RiskLevel);
        Assert.Equal("Высокий риск", tree.RiskLevel);
        Assert.Equal(28, tree.AddedLines);
        Assert.Equal(2, tree.DeletedLines);
    }

    [Theory]
    [InlineData("A", ChangeType.Added)]
    [InlineData("M", ChangeType.Modified)]
    [InlineData("D", ChangeType.Deleted)]
    [InlineData("R100", ChangeType.Renamed)]
    [InlineData("?", ChangeType.Unknown)]
    public void MapsGitStatuses(string status, ChangeType expected)
    {
        var previousPath = status.StartsWith('R') ? "src/cf/Documents/Старое.xml" : null;
        var changedFile = File(status, "src/cf/Documents/Новое.xml", "Документ", "Новое") with { PreviousPath = previousPath };

        var tree = _builder.Build(CreateCommit(changedFile));
        var file = Descendants(tree).Single(node => node.Kind == ChangeTreeNodeKind.File);

        Assert.Equal(expected, file.ChangeType);
        if (expected == ChangeType.Renamed) Assert.Contains("Старое.xml", file.Description);
    }

    [Fact]
    public void MergeCommitKeepsTreeAndComparisonDescription()
    {
        var commit = CreateCommit(
            [File("M", "src/cf/Documents/Заказ/Ext/ObjectModule.bsl", "Документ", "Заказ", code: true, added: 5)],
            parentCount: 2);

        var tree = _builder.Build(commit);

        Assert.Contains("первым родителем", tree.Description);
        Assert.Single(Descendants(tree).Where(node => node.Kind == ChangeTreeNodeKind.Object));
        Assert.Equal(5, tree.AddedLines);
    }

    private static IEnumerable<ChangeTreeNode> Descendants(ChangeTreeNode node) =>
        node.Children.SelectMany(child => new[] { child }.Concat(Descendants(child)));

    private static CommitDetails CreateCommit(params ChangedFile[] files) => CreateCommit(files, 1);

    private static CommitDetails CreateCommit(IReadOnlyList<ChangedFile> files, int parentCount) => new(
        new CommitSummary("0123456789abcdef", "0123456", "Tester", "test@example.invalid", DateTimeOffset.UtcNow, "Test commit"),
        files,
        parentCount,
        parentCount > 0 ? "parent" : null);

    private static ChangedFile File(
        string status,
        string path,
        string objectType,
        string objectName,
        string? component = null,
        bool code = false,
        bool suspicious = false,
        int added = 0,
        int deleted = 0) => new(
            status,
            path,
            null,
            new OneCObjectReference("Конфигурация", objectType, objectName, component, code, suspicious),
            added,
            deleted);
}
