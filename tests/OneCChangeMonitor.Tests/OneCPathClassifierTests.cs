using OneCChangeMonitor.Domain;
using OneCChangeMonitor.Infrastructure;

namespace OneCChangeMonitor.Tests;

public sealed class OneCPathClassifierTests
{
    private static readonly RepositoryProject Project = new("test", "Test", "C:\\repo", "", "master", ["src/cf", "src/cfe"]);

    [Fact]
    public void ClassifiesFormModule()
    {
        var result = new OneCPathClassifier().Classify(Project, "src/cf/Documents/ЗаказПокупателя/Forms/ФормаДокумента/Ext/Form/Module.bsl");

        Assert.NotNull(result);
        Assert.Equal("Документ", result.ObjectType);
        Assert.Equal("ЗаказПокупателя", result.ObjectName);
        Assert.Equal("Форма: ФормаДокумента", result.Component);
        Assert.True(result.IsCode);
        Assert.False(result.IsSuspicious);
    }

    [Fact]
    public void MarksOrigAsSuspicious()
    {
        var result = new OneCPathClassifier().Classify(Project, "src/cf/Roles/Менеджер/Ext/Rights.xml.orig");
        Assert.NotNull(result);
        Assert.Equal("Роль", result.ObjectType);
        Assert.True(result.IsSuspicious);
    }

    [Fact]
    public void MarksBinaryFormBackupAsSuspicious()
    {
        var result = new OneCPathClassifier().Classify(Project, "src/cf/Documents/ЗаказПокупателя/Forms/ФормаДокумента/Ext/Form.bin.orig");

        Assert.NotNull(result);
        Assert.Equal("Документ", result.ObjectType);
        Assert.Equal("ЗаказПокупателя", result.ObjectName);
        Assert.True(result.IsSuspicious);
    }

    [Fact]
    public void IgnoresNonOneCPath()
    {
        Assert.Null(new OneCPathClassifier().Classify(Project, "README.md"));
    }
}
