using System.IO;
using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class CommandParityStatusTests
{
    [Fact]
    public void NamedCloseoutRows_AreTrackedInCommandSurfaceParityDocument()
    {
        var doc = File.ReadAllText(WorkspaceFileLocator.Find("docs", "COMMAND_SURFACE_PARITY.md"));

        (string Command, string Status)[] expectedRows =
        [
            ("Advanced Chart Families", "Deferred"),
            ("Export to PDF/XPS", "Partial"),
            ("Cut (Ctrl+X)", "Implemented"),
            ("Copy (Ctrl+C)", "Implemented"),
            ("Paste (Ctrl+V)", "Implemented"),
            ("Paste Special (values/formulas/formats/transpose/arithmetic/link/column-widths/picture/text)", "Implemented"),
            ("Format Painter", "Implemented"),
            ("Distributed/Justify alignment", "Implemented"),
            ("Shrink to Fit", "Implemented"),
            ("Format Cells Alignment dialog", "Implemented"),
            ("Custom Number Format", "Partial"),
            ("Full Excel locale/accounting fidelity", "Partial"),
            ("AutoFit Row/Column", "Implemented"),
            ("Format Cells dialog (Ctrl+1)", "Implemented"),
            ("Flash Fill", "Partial"),
            ("Threaded Comments", "Partial"),
            ("Share", "Partial"),
            ("Split Panes", "Implemented")
        ];

        var tableRows = ParseMarkdownTableRows(doc);

        foreach (var expected in expectedRows)
        {
            tableRows.Should().Contain(
                row => row.FirstCell == expected.Command && row.Status == expected.Status,
                $"COMMAND_SURFACE_PARITY.md should contain a markdown table row for {expected.Command} with status {expected.Status}");
        }
    }

    [Fact]
    public void ScenarioManagerRepeatability_DistinguishesShowFromDialogManagementActions()
    {
        var doc = File.ReadAllText(WorkspaceFileLocator.Find("docs", "COMMAND_SURFACE_PARITY.md"));

        doc.Should().Contain("Scenario Manager Show applies the selected scenario through the repeatable command path");
        doc.Should().Contain("Scenario Manager add/edit/delete/report/list actions");
        doc.Should().Contain("Scenario Manager Show is repeatable after the first explicit apply");
        doc.Should().NotContain("| Data / What-If | Goal Seek, Scenario Manager, Forecast Sheet |");
    }

    [Fact]
    public void HelpTabRows_TrackCurrentRibbonSurface()
    {
        var doc = File.ReadAllText(WorkspaceFileLocator.Find("docs", "COMMAND_SURFACE_PARITY.md"));
        var tableRows = ParseMarkdownTableRows(doc);

        (string Command, string Status)[] expectedRows =
        [
            ("Help (opens project repo)", "Implemented"),
            ("Send Feedback (opens issue form)", "Implemented"),
            ("Copy Diagnostics", "Implemented"),
            ("Check for Updates", "Implemented"),
            ("About Freexcel", "Implemented"),
            ("Contact Support", "Excluded"),
            ("Show Training", "Excluded"),
            ("What's New", "Excluded")
        ];

        foreach (var expected in expectedRows)
        {
            tableRows.Should().Contain(
                row => row.FirstCell == expected.Command && row.Status == expected.Status,
                $"COMMAND_SURFACE_PARITY.md should track the Help tab row for {expected.Command}");
        }
    }

    private static IReadOnlyList<CommandTableRow> ParseMarkdownTableRows(string doc)
    {
        List<CommandTableRow> rows = [];
        string[]? headers = null;
        var awaitingSeparator = false;
        var inTable = false;

        foreach (var rawLine in doc.Split('\n'))
        {
            var line = rawLine.Trim();
            if (!IsMarkdownTableLine(line))
            {
                headers = null;
                awaitingSeparator = false;
                inTable = false;
                continue;
            }

            var cells = SplitMarkdownTableRow(line);
            if (awaitingSeparator)
            {
                if (IsSeparatorRow(cells))
                {
                    inTable = true;
                    awaitingSeparator = false;
                    continue;
                }

                headers = cells;
                continue;
            }

            if (!inTable)
            {
                headers = cells;
                awaitingSeparator = true;
                continue;
            }

            var status = GetStatus(headers, cells);
            if (status is not null && cells.Length > 0)
                rows.Add(new CommandTableRow(cells[0], status));
        }

        return rows;
    }

    private static bool IsMarkdownTableLine(string line) =>
        line.StartsWith('|') && line.EndsWith('|');

    private static string[] SplitMarkdownTableRow(string line) =>
        line.Trim('|')
            .Split('|')
            .Select(cell => cell.Trim())
            .ToArray();

    private static bool IsSeparatorRow(string[] cells) =>
        cells.Length > 0 && cells.All(cell => cell.Length > 0 && cell.All(ch => ch is '-' or ':' or ' '));

    private static string? GetStatus(string[]? headers, string[] cells)
    {
        if (headers is null)
            return null;

        var statusIndex = Array.FindIndex(headers, header => header == "Status");
        if (statusIndex >= 0 && statusIndex < cells.Length)
            return cells[statusIndex];

        var decisionIndex = Array.FindIndex(headers, header => header == "Freexcel Decision");
        if (decisionIndex >= 0 && decisionIndex < cells.Length)
            return ExtractStatusFromDecision(cells[decisionIndex]);

        return null;
    }

    private static string? ExtractStatusFromDecision(string decision)
    {
        string[] statuses = ["Implemented", "Partial", "Not Implemented", "Deferred", "Excluded"];
        return statuses.FirstOrDefault(status => decision == status || decision.StartsWith(status + " "));
    }

    private sealed record CommandTableRow(string FirstCell, string Status);
}
