using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed partial class UiTestCatalogInventoryTests
{
    [Fact]
    public void InventorySnapshot_MatchesSourceDerivedInventoryModel()
    {
        var snapshot = ReadInventorySnapshot();
        var inventory = ReadCommandInventory();
        var shortcutSummary = ReadShortcutSummary();
        var topLevelTabs = ReadVisibleTopLevelRibbonTabs();
        var xamlClickWiredControls = ReadMainWindowXamlClickHandlerCount();
        var worksheetContextMenuCommandCount = WorksheetContextMenuPlanner.BuildCommands()
            .Count(command => !command.IsSeparator);

        AssertSnapshotRow(
            snapshot,
            "Command surface in-scope rows",
            inventory.CommandSurfaceTabs.Sum(tab => tab.Implemented + tab.Partial),
            "From `COMMAND_INVENTORY.json`: Implemented + Partial command-surface rows.");
        AssertSnapshotRow(
            snapshot,
            "Menu/toolbar in-scope rows",
            inventory.MenuToolbarTabs.Sum(tab => tab.Implemented + tab.Partial),
            "Includes the current Draw tab menu/toolbar delta.");
        AssertSnapshotRow(
            snapshot,
            "Top-level ribbon/backstage tabs",
            topLevelTabs.Count,
            $"{string.Join(", ", topLevelTabs)}.");
        AssertSnapshotRow(
            snapshot,
            "XAML click-wired controls",
            xamlClickWiredControls,
            "`Click=\"...\"` occurrences in `MainWindow.xaml` on latest synced `origin/main`.");
        AssertSnapshotRow(
            snapshot,
            "Documented shortcut rows",
            shortcutSummary.TotalInScope,
            $"From `SHORTCUT_PARITY_MATRIX.md`: {shortcutSummary.Parity} parity, {shortcutSummary.Partial} partial.");
        AssertSnapshotRow(
            snapshot,
            "Worksheet context menu commands",
            worksheetContextMenuCommandCount,
            "From `WorksheetContextMenuPlanner.BuildCommands()`.");
    }

    [Fact]
    public void SourceInventoryModel_MatchesParityDocumentSummaries()
    {
        var inventory = ReadCommandInventory();
        var commandSurfaceSummary = ReadCommandCoverageSummary("COMMAND_SURFACE_PARITY.md");
        var menuToolbarSummary = ReadCommandCoverageSummary("MENU_TOOLBAR_PARITY.md");
        var shortcutSummary = ReadShortcutSummary();
        var shortcutRows = ReadShortcutRows();

        commandSurfaceSummary.Should().BeEquivalentTo(Summarize(inventory.CommandSurfaceTabs));
        menuToolbarSummary.Should().BeEquivalentTo(Summarize(inventory.MenuToolbarTabs));
        shortcutRows.Count(row => row.Status == "Parity").Should().Be(shortcutSummary.Parity);
        shortcutRows.Count(row => row.Status == "Partial").Should().Be(shortcutSummary.Partial);
        shortcutRows.Count(row => row.Status is "Not Implemented" or "Missing").Should().Be(shortcutSummary.NotImplemented);
        shortcutRows.Count(row => row.Status == "Excluded").Should().Be(shortcutSummary.Excluded);
        shortcutRows.Count(row => row.Status != "Excluded").Should().Be(shortcutSummary.TotalInScope);
    }

    [Fact]
    public void TopLevelTabInventory_MatchesCommandInventoryKeyTips()
    {
        var inventory = ReadCommandInventory();
        var sourceTabs = ReadVisibleTopLevelRibbonTabs();
        var keyTipTabs = inventory.KeyTips.TopLevelTabs
            .Select(tab => tab.Name == "File/Backstage" ? "File" : tab.Name)
            .ToArray();

        sourceTabs.Should().Equal(keyTipTabs);
    }

    [Fact]
    public void NextCatalogTasks_RecordSourceBasedInventoryGuardAsExisting()
    {
        var catalog = File.ReadAllText(WorkspaceFileLocator.Find("docs", "UI_TEST_CATALOG.md"));

        catalog.Should().NotContain(
            "Generate a machine-readable row list from `COMMAND_SURFACE_PARITY.md`",
            "the source-based inventory guard now exists and future work should expand it");
        catalog.Should().Contain("Expand the source-based machine-readable inventory guard");
    }

    private static IReadOnlyDictionary<string, InventorySnapshotRow> ReadInventorySnapshot()
    {
        var lines = File.ReadAllLines(WorkspaceFileLocator.Find("docs", "UI_TEST_CATALOG.md"));
        var heading = Array.IndexOf(lines, "## Inventory Snapshot");
        heading.Should().BeGreaterThanOrEqualTo(0);

        return lines
            .Skip(heading + 1)
            .SkipWhile(line => !line.StartsWith("| Source |", StringComparison.Ordinal))
            .Skip(2)
            .TakeWhile(line => line.StartsWith('|'))
            .Select(SplitMarkdownRow)
            .Where(columns => columns.Count == 3 && int.TryParse(columns[1], CultureInfo.InvariantCulture, out _))
            .ToDictionary(
                columns => columns[0],
                columns => new InventorySnapshotRow(
                    int.Parse(columns[1], CultureInfo.InvariantCulture),
                    columns[2]),
                StringComparer.Ordinal);
    }

    private static CommandInventory ReadCommandInventory()
    {
        var json = File.ReadAllText(WorkspaceFileLocator.Find("docs", "COMMAND_INVENTORY.json"));
        return JsonSerializer.Deserialize<CommandInventory>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Command inventory is empty.");
    }

    private static CommandCoverageSummary ReadCommandCoverageSummary(string fileName)
    {
        var lines = File.ReadAllLines(WorkspaceFileLocator.Find("docs", fileName));
        var total = lines
            .Select(SplitMarkdownRow)
            .Single(columns => columns.Count >= 6 && columns[0] == "**TOTAL**");

        return new CommandCoverageSummary(
            ParseBoldInt(total[1]),
            ParseBoldInt(total[2]),
            ParseBoldInt(total[3]),
            ParseBoldInt(total[4]),
            ParseBoldInt(total[5]));
    }

    private static ShortcutSummary ReadShortcutSummary()
    {
        var lines = File.ReadAllLines(WorkspaceFileLocator.Find("docs", "SHORTCUT_PARITY_MATRIX.md"));

        return new ShortcutSummary(
            ReadShortcutSummaryCount(lines, "Parity"),
            ReadShortcutSummaryCount(lines, "Partial"),
            ReadShortcutSummaryCount(lines, "Not Implemented"),
            ReadShortcutSummaryCount(lines, "Excluded"),
            ReadShortcutSummaryCount(lines, "**Total in-scope**"));
    }

    private static IReadOnlyList<ShortcutRow> ReadShortcutRows()
    {
        var lines = File.ReadAllLines(WorkspaceFileLocator.Find("docs", "SHORTCUT_PARITY_MATRIX.md"));
        var tableStart = Array.FindIndex(lines, line => line.StartsWith("| Area | Excel Shortcut |", StringComparison.Ordinal));
        tableStart.Should().BeGreaterThanOrEqualTo(0);

        return lines
            .Skip(tableStart + 2)
            .TakeWhile(line => line.StartsWith('|'))
            .Select(SplitMarkdownRow)
            .Where(columns => columns.Count >= 4)
            .Select(columns => new ShortcutRow(columns[0], columns[1], columns[2]))
            .Where(row => row.Status is "Parity" or "Partial" or "Not Implemented" or "Missing" or "Excluded")
            .ToArray();
    }

    private static IReadOnlyList<string> ReadVisibleTopLevelRibbonTabs()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        return document
            .Descendants(presentation + "TabItem")
            .Where(tab => tab.Attribute("Visibility")?.Value != "Collapsed")
            .Select(tab => tab.Attribute("Header")?.Value)
            .Where(header => !string.IsNullOrWhiteSpace(header))
            .Cast<string>()
            .ToArray();
    }

    private static int ReadMainWindowXamlClickHandlerCount()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
        return XamlClickHandler().Matches(xaml).Count;
    }

    private static int ReadShortcutSummaryCount(IReadOnlyList<string> lines, string label)
    {
        var row = lines.Single(line => line.StartsWith($"| {label} |", StringComparison.Ordinal));
        return ParseBoldInt(SplitMarkdownRow(row)[1]);
    }

    private static CommandCoverageSummary Summarize(IReadOnlyList<CommandInventoryTab> tabs) =>
        new(
            tabs.Sum(tab => tab.Implemented),
            tabs.Sum(tab => tab.Partial),
            tabs.Sum(tab => tab.NotImplemented),
            tabs.Sum(tab => tab.Deferred),
            tabs.Sum(tab => tab.Excluded));

    private static void AssertSnapshotRow(
        IReadOnlyDictionary<string, InventorySnapshotRow> snapshot,
        string source,
        int count,
        string notes)
    {
        snapshot.Should().ContainKey(source);
        snapshot[source].Should().Be(new InventorySnapshotRow(count, notes));
    }

    private static int ParseBoldInt(string text) =>
        int.Parse(text.Trim('*'), CultureInfo.InvariantCulture);

    private static IReadOnlyList<string> SplitMarkdownRow(string row) =>
        row.Trim().Trim('|').Split('|').Select(column => column.Trim()).ToArray();

    [GeneratedRegex(@"Click=""[^""]+""")]
    private static partial Regex XamlClickHandler();

    private sealed record InventorySnapshotRow(int Count, string Notes);

    private sealed record CommandInventory(
        IReadOnlyList<CommandInventoryTab> CommandSurfaceTabs,
        IReadOnlyList<CommandInventoryTab> MenuToolbarTabs,
        CommandInventoryKeyTips KeyTips);

    private sealed record CommandInventoryTab(
        string Name,
        int Implemented,
        int Partial,
        int NotImplemented,
        int Deferred,
        int Excluded);

    private sealed record CommandInventoryKeyTips(IReadOnlyList<KeyTipExpectation> TopLevelTabs);

    private sealed record KeyTipExpectation(string Name, string KeyTip);

    private sealed record CommandCoverageSummary(
        int Implemented,
        int Partial,
        int NotImplemented,
        int Deferred,
        int Excluded);

    private sealed record ShortcutSummary(
        int Parity,
        int Partial,
        int NotImplemented,
        int Excluded,
        int TotalInScope);

    private sealed record ShortcutRow(string Area, string Shortcut, string Status);
}
