using FluentAssertions;
using System.IO;

namespace Freexcel.App.Host.Tests;

public sealed class ShortcutParityMatrixTests
{
    [Fact]
    public void ShortcutParityMatrix_CoverageSummaryMatchesRows()
    {
        var matrix = File.ReadAllLines(WorkspaceFileLocator.Find("docs", "SHORTCUT_PARITY_MATRIX.md"));
        var summary = ReadCoverageSummary(matrix);
        var matrixStart = Array.FindIndex(matrix, line => line.StartsWith("| Area | Excel Shortcut |", StringComparison.Ordinal));
        matrixStart.Should().BeGreaterThanOrEqualTo(0);
        var rows = matrix
            .Skip(matrixStart)
            .TakeWhile(line => line.StartsWith('|'))
            .Where(line => !line.Contains("---", StringComparison.Ordinal))
            .Select(SplitMarkdownRow)
            .Where(columns => columns.Count >= 4 && columns[0] is not "Area" && columns[2] is "Parity" or "Partial" or "Not Implemented" or "Missing" or "Excluded")
            .ToArray();

        rows.Should().NotBeEmpty();
        rows.Count(row => row[2] == "Parity").Should().Be(summary.Parity);
        rows.Count(row => row[2] == "Partial").Should().Be(summary.Partial);
        rows.Count(row => row[2] is "Not Implemented" or "Missing").Should().Be(summary.NotImplemented);
        rows.Count(row => row[2] == "Excluded").Should().Be(summary.Excluded);
        rows.Count(row => row[2] is not "Excluded").Should().Be(summary.TotalInScope);
        summary.ParityPercent.Should().Be(ToPercent(summary.Parity, summary.TotalInScope));
        summary.PartialPercent.Should().Be(ToPercent(summary.Partial, summary.TotalInScope));
        summary.NotImplementedPercent.Should().Be(ToPercent(summary.NotImplemented, summary.TotalInScope));
        summary.NotImplemented.Should().Be(0, "the visible shortcut matrix should not regress to undocumented missing shortcuts");
    }

    [Fact]
    public void ShortcutParityMatrix_NextWorkBacklogKeepsCurrentPriorityOrder()
    {
        var matrix = File.ReadAllLines(WorkspaceFileLocator.Find("docs", "SHORTCUT_PARITY_MATRIX.md"));
        var nextWork = ReadNextWorkItems(matrix);

        nextWork.Should().HaveCount(9);
        nextWork.Should().SatisfyRespectively(
            item => item.Should().Contain("`Ctrl+P`"),
            item => item.Should().ContainAll("`Ctrl+V`", "`Ctrl+Alt+V`"),
            item => item.Should().ContainAll("`Ctrl+1`", "`Ctrl+Shift+F/P`"),
            item => item.Should().Contain("`Ctrl+Shift+F2`"),
            item => item.Should().Contain("`Alt+Down`"),
            item => item.Should().Contain("`Ctrl+Q`"),
            item => item.Should().ContainAll("ribbon keytips", "Conditional Formatting"),
            item => item.Should().Contain("`Shift+F10` / Menu key"),
            item => item.Should().Contain("F4"));
    }

    [Fact]
    public void UiTestCatalog_ShortcutInventoryCountsMatchShortcutMatrix()
    {
        var matrix = File.ReadAllLines(WorkspaceFileLocator.Find("docs", "SHORTCUT_PARITY_MATRIX.md"));
        var catalog = File.ReadAllText(WorkspaceFileLocator.Find("docs", "UI_TEST_CATALOG.md"));
        var summary = ReadCoverageSummary(matrix);

        catalog.Should().Contain(
            $"| Documented shortcut rows | {summary.TotalInScope} | From `SHORTCUT_PARITY_MATRIX.md`: {summary.Parity} parity, {summary.Partial} partial. |");
        catalog.Should().Contain($"{summary.TotalInScope} documented shortcut rows;");
    }

    private static IReadOnlyList<string> ReadNextWorkItems(IReadOnlyList<string> lines)
    {
        var sectionStart = Array.FindIndex(lines.ToArray(), line => line == "## Next Shortcut Work");
        sectionStart.Should().BeGreaterThanOrEqualTo(0);

        return lines
            .Skip(sectionStart + 1)
            .SkipWhile(string.IsNullOrWhiteSpace)
            .TakeWhile(line => !line.StartsWith("## ", StringComparison.Ordinal))
            .Where(line => char.IsDigit(line.FirstOrDefault()))
            .ToArray();
    }

    private static CoverageSummary ReadCoverageSummary(IReadOnlyList<string> lines)
    {
        var parity = ReadSummaryCount(lines, "Parity");
        var partial = ReadSummaryCount(lines, "Partial");
        var notImplemented = ReadSummaryCount(lines, "Not Implemented");
        var excluded = ReadSummaryCount(lines, "Excluded");
        var total = ReadSummaryCount(lines, "**Total in-scope**");
        var parityPercent = ReadSummaryPercent(lines, "Parity");
        var partialPercent = ReadSummaryPercent(lines, "Partial");
        var notImplementedPercent = ReadSummaryPercent(lines, "Not Implemented");
        return new CoverageSummary(
            parity,
            partial,
            notImplemented,
            excluded,
            total,
            parityPercent,
            partialPercent,
            notImplementedPercent);
    }

    private static int ReadSummaryCount(IReadOnlyList<string> lines, string label)
    {
        var row = lines.Single(line => line.StartsWith($"| {label} |", StringComparison.Ordinal));
        return int.Parse(SplitMarkdownRow(row)[1].Trim('*'), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static int ReadSummaryPercent(IReadOnlyList<string> lines, string label)
    {
        var row = lines.Single(line => line.StartsWith($"| {label} |", StringComparison.Ordinal));
        return int.Parse(
            SplitMarkdownRow(row)[2].Trim('*', '%'),
            System.Globalization.CultureInfo.InvariantCulture);
    }

    private static int ToPercent(int count, int total) =>
        total == 0 ? 0 : (int)Math.Round(count * 100.0 / total);

    private static IReadOnlyList<string> SplitMarkdownRow(string row) =>
        row.Trim().Trim('|').Split('|').Select(column => column.Trim()).ToArray();

    private sealed record CoverageSummary(
        int Parity,
        int Partial,
        int NotImplemented,
        int Excluded,
        int TotalInScope,
        int ParityPercent,
        int PartialPercent,
        int NotImplementedPercent);
}
