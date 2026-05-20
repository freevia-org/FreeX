using System.IO;
using System.Text.Json;
using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class CommandInventoryDocumentTests
{
    [Fact]
    public void CommandSurfaceCoverageSummary_IsGeneratedFromInventory()
    {
        var inventory = LoadInventory();
        var doc = File.ReadAllText(WorkspaceFileLocator.Find("docs", "COMMAND_SURFACE_PARITY.md"));

        ExtractGeneratedBlock(doc, "command-inventory:coverage-summary").Should().Be(
            BuildCoverageSummary(inventory.CommandSurfaceTabs, boldCoverageHeader: true));
    }

    [Fact]
    public void MenuToolbarCoverageSummary_IsGeneratedFromInventory()
    {
        var inventory = LoadInventory();
        var doc = File.ReadAllText(WorkspaceFileLocator.Find("docs", "MENU_TOOLBAR_PARITY.md"));

        ExtractGeneratedBlock(doc, "command-inventory:coverage-summary").Should().Be(
            BuildCoverageSummary(inventory.MenuToolbarTabs, boldCoverageHeader: false));
    }

    [Fact]
    public void CommandInventory_DefinesCurrentSchemaAndTopLevelKeyTipExpectations()
    {
        var inventory = LoadInventory();

        inventory.SchemaVersion.Should().Be(1);
        inventory.CommandSurfaceRows.Should().Contain(section => section.Name == "File/Backstage");
        inventory.CommandSurfaceRows.Should().Contain(section => section.Name == "QAT");
        inventory.MenuToolbarRows.Should().Contain(section => section.Name == "File/Backstage");
        inventory.MenuToolbarRows.Should().Contain(section => section.Name == "QAT");
        inventory.KeyTips.TopLevelTabs.Should().ContainEquivalentOf(new KeyTipExpectation("Home", "H"));
        inventory.KeyTips.TopLevelTabs.Should().ContainEquivalentOf(new KeyTipExpectation("Insert", "N"));
        inventory.KeyTips.TopLevelTabs.Should().ContainEquivalentOf(new KeyTipExpectation("Formulas", "M"));
        inventory.KeyTips.TopLevelTabs.Should().ContainEquivalentOf(new KeyTipExpectation("Data", "A"));
        inventory.KeyTips.TopLevelTabs.Should().ContainEquivalentOf(new KeyTipExpectation("View", "W"));
    }

    [Fact]
    public void CommandSurfaceFileBackstageRows_AreGeneratedFromInventory()
    {
        var inventory = LoadInventory();
        var doc = File.ReadAllText(WorkspaceFileLocator.Find("docs", "COMMAND_SURFACE_PARITY.md"));
        var section = inventory.CommandSurfaceRows.Single(section => section.Name == "File/Backstage");

        ExtractGeneratedBlock(doc, "command-inventory:command-surface:file-backstage").Should().Be(
            BuildCommandRows(section));
    }

    [Fact]
    public void MenuToolbarFileBackstageRows_AreGeneratedFromInventory()
    {
        var inventory = LoadInventory();
        var doc = File.ReadAllText(WorkspaceFileLocator.Find("docs", "MENU_TOOLBAR_PARITY.md"));
        var section = inventory.MenuToolbarRows.Single(section => section.Name == "File/Backstage");

        ExtractGeneratedBlock(doc, "command-inventory:menu-toolbar:file-backstage").Should().Be(
            BuildCommandRows(section));
    }

    [Fact]
    public void QuickAccessToolbarRows_AreGeneratedFromInventory()
    {
        var inventory = LoadInventory();
        var commandSurfaceDoc = File.ReadAllText(WorkspaceFileLocator.Find("docs", "COMMAND_SURFACE_PARITY.md"));
        var menuToolbarDoc = File.ReadAllText(WorkspaceFileLocator.Find("docs", "MENU_TOOLBAR_PARITY.md"));
        var commandSurfaceSection = inventory.CommandSurfaceRows.Single(section => section.Name == "QAT");
        var menuToolbarSection = inventory.MenuToolbarRows.Single(section => section.Name == "QAT");

        ExtractGeneratedBlock(commandSurfaceDoc, "command-inventory:command-surface:qat").Should().Be(
            BuildCommandRows(commandSurfaceSection));
        ExtractGeneratedBlock(menuToolbarDoc, "command-inventory:menu-toolbar:qat").Should().Be(
            BuildCommandRows(menuToolbarSection));
    }

    private static CommandInventory LoadInventory()
    {
        var path = WorkspaceFileLocator.Find("docs", "COMMAND_INVENTORY.json");
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<CommandInventory>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Command inventory is empty.");
    }

    private static string ExtractGeneratedBlock(string doc, string marker)
    {
        var startMarker = $"<!-- {marker}:start -->";
        var endMarker = $"<!-- {marker}:end -->";
        var start = doc.IndexOf(startMarker, StringComparison.Ordinal);
        var end = doc.IndexOf(endMarker, StringComparison.Ordinal);

        start.Should().BeGreaterThanOrEqualTo(0, $"the document should contain {startMarker}");
        end.Should().BeGreaterThan(start, $"the document should contain {endMarker} after {startMarker}");

        return NormalizeLineEndings(doc[(start + startMarker.Length)..end].Trim());
    }

    private static string BuildCoverageSummary(IReadOnlyList<CommandInventoryTab> tabs, bool boldCoverageHeader)
    {
        var total = new CommandInventoryTab(
            "TOTAL",
            tabs.Sum(tab => tab.Implemented),
            tabs.Sum(tab => tab.Partial),
            tabs.Sum(tab => tab.NotImplemented),
            tabs.Sum(tab => tab.Deferred),
            tabs.Sum(tab => tab.Excluded));

        var coverageHeader = boldCoverageHeader ? "**Coverage**" : "Coverage";
        var lines = new List<string>
        {
            $"| Tab | Implemented | Partial | Not Implemented | Deferred | Excluded | {coverageHeader} |",
            "|---|---:|---:|---:|---:|---:|---:|"
        };

        foreach (var tab in tabs)
            lines.Add(BuildCoverageRow(tab, boldLabel: false));

        lines.Add(BuildCoverageRow(total, boldLabel: true));
        return string.Join("\n", lines);
    }

    private static string BuildCoverageRow(CommandInventoryTab tab, bool boldLabel)
    {
        var label = boldLabel ? $"**{tab.Name}**" : tab.Name;
        var implemented = boldLabel ? $"**{tab.Implemented}**" : tab.Implemented.ToString();
        var partial = boldLabel ? $"**{tab.Partial}**" : tab.Partial.ToString();
        var notImplemented = boldLabel ? $"**{tab.NotImplemented}**" : tab.NotImplemented.ToString();
        var deferred = boldLabel ? $"**{tab.Deferred}**" : tab.Deferred.ToString();
        var excluded = boldLabel ? $"**{tab.Excluded}**" : tab.Excluded.ToString();
        return $"| {label} | {implemented} | {partial} | {notImplemented} | {deferred} | {excluded} | **{tab.CoveragePercent}%** |";
    }

    private static string BuildCommandRows(CommandInventoryCommandSection section)
    {
        var itemHeader = section.ItemColumn ?? "Command";
        var lines = new List<string>
        {
            $"| {itemHeader} | Status | Notes |",
            "|---|---|---|"
        };

        lines.AddRange(section.Rows.Select(row => $"| {row.Name} | {row.Status} | {row.Notes} |"));
        return string.Join("\n", lines);
    }

    private static string NormalizeLineEndings(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal);

    private sealed record CommandInventory(
        int SchemaVersion,
        IReadOnlyList<CommandInventoryTab> CommandSurfaceTabs,
        IReadOnlyList<CommandInventoryTab> MenuToolbarTabs,
        IReadOnlyList<CommandInventoryCommandSection> CommandSurfaceRows,
        IReadOnlyList<CommandInventoryCommandSection> MenuToolbarRows,
        CommandInventoryKeyTips KeyTips);

    private sealed record CommandInventoryTab(
        string Name,
        int Implemented,
        int Partial,
        int NotImplemented,
        int Deferred,
        int Excluded)
    {
        public int CoveragePercent =>
            Implemented + Partial + NotImplemented == 0
                ? 100
                : (int)Math.Round((Implemented + Partial) * 100.0 / (Implemented + Partial + NotImplemented));
    }

    private sealed record CommandInventoryKeyTips(IReadOnlyList<KeyTipExpectation> TopLevelTabs);

    private sealed record CommandInventoryCommandSection(
        string Name,
        string? ItemColumn,
        IReadOnlyList<CommandInventoryCommandRow> Rows);

    private sealed record CommandInventoryCommandRow(string Name, string Status, string Notes);

    private sealed record KeyTipExpectation(string Name, string KeyTip);
}
