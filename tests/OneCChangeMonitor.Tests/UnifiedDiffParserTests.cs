using OneCChangeMonitor.Application;

namespace OneCChangeMonitor.Tests;

public sealed class UnifiedDiffParserTests
{
    [Fact]
    public void Parse_AlignsRemovedAndAddedLines()
    {
        const string diff = "@@ -10,3 +10,4 @@\n context\n-old value\n+new value\n+extra value\n end";

        var rows = UnifiedDiffParser.Parse(diff);

        Assert.Equal(5, rows.Count);
        Assert.Equal(11, rows[2].OldNumber);
        Assert.Equal("old value", rows[2].OldText);
        Assert.Equal(11, rows[2].NewNumber);
        Assert.Equal("new value", rows[2].NewText);
        Assert.Null(rows[3].OldNumber);
        Assert.Equal(12, rows[3].NewNumber);
    }

    [Fact]
    public void Parse_MarksConflictLines()
    {
        const string diff = "@@ -1 +1 @@\n-<<<<<<< ours\n+>>>>>>> theirs";

        var row = UnifiedDiffParser.Parse(diff)[1];

        Assert.Equal(DiffLineKind.Conflict, row.OldKind);
        Assert.Equal(DiffLineKind.Conflict, row.NewKind);
    }
}
