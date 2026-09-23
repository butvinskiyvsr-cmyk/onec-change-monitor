using System.Text.RegularExpressions;

namespace OneCChangeMonitor.Application;

public enum DiffLineKind
{
    Context,
    Added,
    Removed,
    Header,
    Conflict
}

public sealed record SideBySideDiffRow(
    int? OldNumber,
    string OldText,
    DiffLineKind OldKind,
    int? NewNumber,
    string NewText,
    DiffLineKind NewKind);

public static partial class UnifiedDiffParser
{
    public static IReadOnlyList<SideBySideDiffRow> Parse(string content)
    {
        var result = new List<SideBySideDiffRow>();
        var lines = content.Replace("\r\n", "\n").Split('\n');
        var oldLine = 0;
        var newLine = 0;
        var inHunk = false;

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (line.StartsWith("@@", StringComparison.Ordinal))
            {
                var match = HunkHeaderRegex().Match(line);
                if (match.Success)
                {
                    oldLine = int.Parse(match.Groups[1].Value);
                    newLine = int.Parse(match.Groups[2].Value);
                }

                result.Add(new SideBySideDiffRow(null, line, DiffLineKind.Header, null, line, DiffLineKind.Header));
                inHunk = true;
                continue;
            }

            if (!inHunk || line.StartsWith("---", StringComparison.Ordinal) || line.StartsWith("+++", StringComparison.Ordinal)) continue;

            if (line.StartsWith("-", StringComparison.Ordinal))
            {
                var removed = new List<string>();
                while (index < lines.Length && lines[index].StartsWith("-", StringComparison.Ordinal) && !lines[index].StartsWith("---", StringComparison.Ordinal))
                {
                    removed.Add(lines[index][1..]);
                    index++;
                }

                var added = new List<string>();
                while (index < lines.Length && lines[index].StartsWith("+", StringComparison.Ordinal) && !lines[index].StartsWith("+++", StringComparison.Ordinal))
                {
                    added.Add(lines[index][1..]);
                    index++;
                }

                index--;
                var pairedCount = Math.Max(removed.Count, added.Count);
                for (var pairIndex = 0; pairIndex < pairedCount; pairIndex++)
                {
                    var hasOld = pairIndex < removed.Count;
                    var hasNew = pairIndex < added.Count;
                    var oldText = hasOld ? removed[pairIndex] : string.Empty;
                    var newText = hasNew ? added[pairIndex] : string.Empty;
                    result.Add(new SideBySideDiffRow(
                        hasOld ? oldLine++ : null,
                        oldText,
                        IsConflict(oldText) ? DiffLineKind.Conflict : DiffLineKind.Removed,
                        hasNew ? newLine++ : null,
                        newText,
                        IsConflict(newText) ? DiffLineKind.Conflict : DiffLineKind.Added));
                }

                continue;
            }

            if (line.StartsWith("+", StringComparison.Ordinal))
            {
                var text = line[1..];
                result.Add(new SideBySideDiffRow(null, string.Empty, DiffLineKind.Context, newLine++, text, IsConflict(text) ? DiffLineKind.Conflict : DiffLineKind.Added));
                continue;
            }

            if (line.StartsWith(" ", StringComparison.Ordinal))
            {
                var text = line[1..];
                var kind = IsConflict(text) ? DiffLineKind.Conflict : DiffLineKind.Context;
                result.Add(new SideBySideDiffRow(oldLine++, text, kind, newLine++, text, kind));
                continue;
            }

            if (line.StartsWith("\\ No newline", StringComparison.Ordinal)) continue;
            result.Add(new SideBySideDiffRow(null, line, DiffLineKind.Header, null, line, DiffLineKind.Header));
        }

        return result;
    }

    private static bool IsConflict(string value) =>
        value.StartsWith("<<<<<<<", StringComparison.Ordinal) ||
        value.StartsWith("=======", StringComparison.Ordinal) ||
        value.StartsWith(">>>>>>>", StringComparison.Ordinal);

    [GeneratedRegex(@"^@@\s+-(\d+)(?:,\d+)?\s+\+(\d+)(?:,\d+)?\s+@@")]
    private static partial Regex HunkHeaderRegex();
}
