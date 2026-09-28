using OneCChangeMonitor.Domain;
using OneCChangeMonitor.Infrastructure;

namespace OneCChangeMonitor.Tests;

public sealed class FileSystemConfigurationObjectIndexTests
{
    [Fact]
    public async Task GroupsFilesIntoConfigurationObjectsAndBuildsHistoryPaths()
    {
        var root = Path.Combine(Path.GetTempPath(), "configscope-index-" + Guid.NewGuid().ToString("N"));
        try
        {
            Write(root, "src/cf/Documents/Заказ/Ext/ObjectModule.bsl", "Процедура Тест()\nКонецПроцедуры");
            Write(root, "src/cf/Documents/Заказ.xml", "<MetaDataObject />");
            Write(root, "src/cf/Catalogs/Товары.xml", "<MetaDataObject />");
            var project = new RepositoryProject("test", "Test", root, string.Empty, "master", ["src/cf"]);

            var objects = await new FileSystemConfigurationObjectIndex(new OneCPathClassifier())
                .BuildAsync(project, CancellationToken.None);

            Assert.Equal(2, objects.Count);
            var order = Assert.Single(objects.Where(item => item.Name == "Заказ"));
            Assert.Equal("Документ", order.ObjectType);
            Assert.Equal(2, order.Files.Count);
            Assert.Contains("Метаданные", order.Components);
            Assert.Contains("Модуль или свойства объекта", order.Components);
            Assert.Contains("src/cf/Documents/Заказ", order.HistoryPaths);
            Assert.Contains("src/cf/Documents/Заказ.xml", order.HistoryPaths);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SupportsConfigurationExportedDirectlyIntoSrcFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), "configscope-direct-src-" + Guid.NewGuid().ToString("N"));
        try
        {
            Write(root, "src/Configuration.xml", "<MetaDataObject />");
            Write(root, "src/Documents/Заказ.xml", "<MetaDataObject />");
            var project = new RepositoryProject("test", "Test", root, string.Empty, "master", ["src"]);

            var item = Assert.Single(await new FileSystemConfigurationObjectIndex(new OneCPathClassifier())
                .BuildAsync(project, CancellationToken.None));

            Assert.Equal("Конфигурация", item.SourceKind);
            Assert.Equal("Документ", item.ObjectType);
            Assert.Equal("Заказ", item.Name);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static void Write(string root, string relativePath, string content)
    {
        var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
