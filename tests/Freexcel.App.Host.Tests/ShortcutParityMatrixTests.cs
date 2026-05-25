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
        summary.NotImplemented.Should().Be(0, "the visible shortcut matrix should not regress to undocumented missing shortcuts");
    }

    private static CoverageSummary ReadCoverageSummary(IReadOnlyList<string> lines)
    {
        var parity = ReadSummaryCount(lines, "Parity");
        var partial = ReadSummaryCount(lines, "Partial");
        var notImplemented = ReadSummaryCount(lines, "Not Implemented");
        var excluded = ReadSummaryCount(lines, "Excluded");
        var total = ReadSummaryCount(lines, "**Total in-scope**");
        return new CoverageSummary(parity, partial, notImplemented, excluded, total);
    }

    private static int ReadSummaryCount(IReadOnlyList<string> lines, string label)
    {
        var row = lines.Single(line => line.StartsWith($"| {label} |", StringComparison.Ordinal));
        return int.Parse(SplitMarkdownRow(row)[1].Trim('*'), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static IReadOnlyList<string> SplitMarkdownRow(string row) =>
        row.Trim().Trim('|').Split('|').Select(column => column.Trim()).ToArray();

    private sealed record CoverageSummary(int Parity, int Partial, int NotImplemented, int Excluded, int TotalInScope);
}
