using OneCChangeMonitor.Application;
using OneCChangeMonitor.Desktop;
using OneCChangeMonitor.Domain;

namespace OneCChangeMonitor.Tests;

public sealed class ChangeTreePresentationTests
{
    [Fact]
    public void CodeFilterKeepsAncestorsAndRemovesXmlOnlyBranches()
    {
        var files = new[]
        {
            File("src/cf/Documents/Заказ/Ext/ObjectModule.bsl", "Заказ", "Модуль", code: true),
            File("src/cf/Catalogs/Товары/Ext/Properties.xml", "Товары", "Свойства")
        };
        var items = Build(files, ChangeTreeFilter.Code);

        var file = Assert.Single(Flatten(items).Where(item => item.IsFile));
        Assert.EndsWith("ObjectModule.bsl", file.Path);
        Assert.Contains(Flatten(items), item => item.Name == "Заказ");
        Assert.DoesNotContain(Flatten(items), item => item.Name == "Товары");
    }

    [Fact]
    public void SearchFindsObjectComponentAndPathWhilePreservingAncestors()
    {
        var files = new[]
        {
            File("src/cf/Documents/Заказ/Forms/Основная/Ext/Form/Module.bsl", "Заказ", "Форма: Основная", code: true),
            File("src/cf/Documents/Счет/Ext/ObjectModule.bsl", "Счет", "Модуль", code: true)
        };

        var byObject = Build(files, ChangeTreeFilter.All, "Заказ");
        var byComponent = Build(files, ChangeTreeFilter.All, "Форма: Основная");
        var byPath = Build(files, ChangeTreeFilter.All, "ObjectModule.bsl");

        Assert.Single(Flatten(byObject).Where(item => item.IsFile));
        Assert.Single(Flatten(byComponent).Where(item => item.IsFile));
        Assert.Equal("Счет", Assert.Single(Flatten(byPath).Where(item => item.Node.Kind == ChangeTreeNodeKind.Object)).Name);
    }

    private static IReadOnlyList<ChangeTreeItem> Build(ChangedFile[] source, ChangeTreeFilter filter, string? query = null)
    {
        var commit = new CommitDetails(
            new CommitSummary("abc", "abc", "Tester", "tester@example.test", DateTimeOffset.UtcNow, "test"),
            source);
        var tree = new ChangeTreeBuilder().Build(commit);
        return ChangeTreeItem.Build(tree, source.Select(file => new ChangedFileItem(file)).ToArray(), filter, query);
    }

    private static ChangedFile File(string path, string objectName, string component, bool code = false) => new(
        "M",
        path,
        null,
        new OneCObjectReference("Основная конфигурация", path.Contains("Catalogs/") ? "Справочник" : "Документ", objectName, component, code, false),
        3,
        1);

    private static IEnumerable<ChangeTreeItem> Flatten(IEnumerable<ChangeTreeItem> items)
    {
        foreach (var item in items)
        {
            yield return item;
            foreach (var child in Flatten(item.Children)) yield return child;
        }
    }
}
