using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class CrossTargetCommandMatrixDocumentTests
{
    [Fact]
    public void CrossTargetMatrix_DocumentsRequiredTargetColumnsAndRepresentativeCommands()
    {
        var doc = System.IO.File.ReadAllText(WorkspaceFileLocator.Find(
            "docs",
            "parity",
            "subagent-cross-target-command-matrix-2026-06-08.md"));

        var header = SplitMarkdownRow(MatrixRows(doc).First());
        header.Should().ContainInOrder(
            "Single cell",
            "Range",
            "Whole row/column",
            "Table",
            "Filtered rows",
            "Hidden row/column",
            "Protected sheet",
            "Object target");

        string[] requiredCommandSubsets =
        [
            "Paste and Paste Special",
            "Sort A-Z/Z-A",
            "Filter / AutoFilter dropdown",
            "Insert/Delete cells, rows, columns, sheets",
            "Hide/Unhide row/column and AutoFit row/column",
            "Clear All/Formats/Contents/Comments/Hyperlinks",
            "Font, fill, borders, number format, alignment, merge, wrap",
            "Conditional Formatting and Data Validation",
            "Formula auditing, error checking, evaluate formula, and Watch Window",
            "Format as Table",
            "Insert chart",
            "Insert picture/shape/text box",
            "PivotTable insert/refresh",
            "Page setup, print area",
            "Protect Sheet/Workbook"
        ];

        var commandColumn = MatrixRows(doc)
            .Skip(2)
            .Select(row => SplitMarkdownRow(row)[1])
            .ToArray();

        foreach (var commandSubset in requiredCommandSubsets)
        {
            commandColumn.Should().Contain(
                command => command.Contains(commandSubset, StringComparison.Ordinal),
                $"the cross-target matrix should keep a representative row for {commandSubset}");
        }
    }

    [Fact]
    public void CrossTargetMatrix_PrioritizedNextValidationKeepsHighestRiskAxesFirst()
    {
        var doc = System.IO.File.ReadAllText(WorkspaceFileLocator.Find(
            "docs",
            "parity",
            "subagent-cross-target-command-matrix-2026-06-08.md"));

        var priorities = PriorityRows(doc).ToArray();

        priorities.Should().HaveCount(10);
        priorities[0].Should().ContainAll("Paste/Paste Special", "filtered rows", "table data-body ranges", "hidden rows/columns");
        priorities[1].Should().ContainAll("AutoFilter flyout", "table header", "protected sheet");
        priorities[2].Should().ContainAll("Sort", "filtered table", "hidden rows", "protected sheet");
        priorities[3].Should().ContainAll("Insert/Delete/Hide/Unhide/AutoFit", "hidden boundaries");
        priorities.Should().Contain(row => row.Contains("Formula auditing", StringComparison.Ordinal));
        priorities.Should().Contain(row => row.Contains("Watch Window", StringComparison.Ordinal));
        priorities.Should().Contain(row => row.Contains("object", StringComparison.OrdinalIgnoreCase));
        priorities.Should().Contain(row => row.Contains("PivotTable", StringComparison.Ordinal));
        priorities.Should().Contain(row => row.Contains("Page Layout", StringComparison.Ordinal));
    }

    private static IReadOnlyList<string> MatrixRows(string doc) =>
        ReadTableRows(doc, "| Priority | Command subset |");

    private static IReadOnlyList<string> PriorityRows(string doc) =>
        ReadTableRows(doc, "| Rank | Command/target pair |")
            .Skip(2)
            .Select(row => SplitMarkdownRow(row)[1] + " " + SplitMarkdownRow(row)[2])
            .ToArray();

    private static IReadOnlyList<string> ReadTableRows(string doc, string headerPrefix)
    {
        var lines = doc.Split('\n').Select(line => line.TrimEnd('\r')).ToArray();
        var start = Array.FindIndex(lines, line => line.StartsWith(headerPrefix, StringComparison.Ordinal));
        start.Should().BeGreaterThanOrEqualTo(0, $"the document should contain a table starting with {headerPrefix}");

        return lines
            .Skip(start)
            .TakeWhile(line => line.StartsWith('|'))
            .ToArray();
    }

    private static IReadOnlyList<string> SplitMarkdownRow(string row) =>
        row.Trim().Trim('|').Split('|').Select(column => column.Trim()).ToArray();
}
