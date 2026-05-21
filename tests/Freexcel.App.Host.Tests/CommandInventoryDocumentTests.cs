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
        if (HasCommandRows(inventory))
        {
            inventory.CommandSurfaceRows.Select(section => section.Name).Should().BeEquivalentTo(
                inventory.CommandSurfaceTabs.Select(tab => tab.Name));
            inventory.MenuToolbarRows.Select(section => section.Name).Should().BeEquivalentTo(
                inventory.MenuToolbarTabs.Select(tab => tab.Name));
        }

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
        if (!HasCommandRows(inventory))
            return;

        var doc = File.ReadAllText(WorkspaceFileLocator.Find("docs", "COMMAND_SURFACE_PARITY.md"));
        var section = inventory.CommandSurfaceRows.Single(section => section.Name == "File/Backstage");

        ExtractGeneratedBlock(doc, "command-inventory:command-surface:file-backstage").Should().Be(
            BuildCommandRows(section));
    }

    [Fact]
    public void MenuToolbarFileBackstageRows_AreGeneratedFromInventory()
    {
        var inventory = LoadInventory();
        if (!HasCommandRows(inventory))
            return;

        var doc = File.ReadAllText(WorkspaceFileLocator.Find("docs", "MENU_TOOLBAR_PARITY.md"));
        var section = inventory.MenuToolbarRows.Single(section => section.Name == "File/Backstage");

        ExtractGeneratedBlock(doc, "command-inventory:menu-toolbar:file-backstage").Should().Be(
            BuildCommandRows(section));
    }

    [Fact]
    public void QuickAccessToolbarRows_AreGeneratedFromInventory()
    {
        var inventory = LoadInventory();
        if (!HasCommandRows(inventory))
            return;

        var commandSurfaceDoc = File.ReadAllText(WorkspaceFileLocator.Find("docs", "COMMAND_SURFACE_PARITY.md"));
        var menuToolbarDoc = File.ReadAllText(WorkspaceFileLocator.Find("docs", "MENU_TOOLBAR_PARITY.md"));
        var commandSurfaceSection = inventory.CommandSurfaceRows.Single(section => section.Name == "QAT");
        var menuToolbarSection = inventory.MenuToolbarRows.Single(section => section.Name == "QAT");

        ExtractGeneratedBlock(commandSurfaceDoc, "command-inventory:command-surface:qat").Should().Be(
            BuildCommandRows(commandSurfaceSection));
        ExtractGeneratedBlock(menuToolbarDoc, "command-inventory:menu-toolbar:qat").Should().Be(
            BuildCommandRows(menuToolbarSection));
    }

    [Fact]
    public void HomeRows_AreGeneratedFromInventory()
    {
        var inventory = LoadInventory();
        if (!HasCommandRows(inventory))
            return;

        var commandSurfaceDoc = File.ReadAllText(WorkspaceFileLocator.Find("docs", "COMMAND_SURFACE_PARITY.md"));
        var menuToolbarDoc = File.ReadAllText(WorkspaceFileLocator.Find("docs", "MENU_TOOLBAR_PARITY.md"));
        var commandSurfaceSection = inventory.CommandSurfaceRows.Single(section => section.Name == "Home");
        var menuToolbarSection = inventory.MenuToolbarRows.Single(section => section.Name == "Home");

        ExtractGeneratedBlock(commandSurfaceDoc, "command-inventory:command-surface:home").Should().Be(
            BuildCommandRows(commandSurfaceSection));
        ExtractGeneratedBlock(menuToolbarDoc, "command-inventory:menu-toolbar:home").Should().Be(
            BuildCommandRows(menuToolbarSection));
    }

    [Fact]
    public void AllCommandRowSections_AreGeneratedFromInventory()
    {
        var inventory = LoadInventory();
        var commandSurfaceDoc = File.ReadAllText(WorkspaceFileLocator.Find("docs", "COMMAND_SURFACE_PARITY.md"));
        var menuToolbarDoc = File.ReadAllText(WorkspaceFileLocator.Find("docs", "MENU_TOOLBAR_PARITY.md"));

        foreach (var section in inventory.CommandSurfaceRows)
        {
            ExtractGeneratedBlock(commandSurfaceDoc, $"command-inventory:command-surface:{Slug(section.Name)}").Should().Be(
                BuildCommandRows(section));
        }

        foreach (var section in inventory.MenuToolbarRows)
        {
            ExtractGeneratedBlock(menuToolbarDoc, $"command-inventory:menu-toolbar:{Slug(section.Name)}").Should().Be(
                BuildCommandRows(section));
        }
    }

    private static CommandInventory LoadInventory()
    {
        var path = WorkspaceFileLocator.Find("docs", "COMMAND_INVENTORY.json");
        var json = File.ReadAllText(path);
        var inventory = JsonSerializer.Deserialize<CommandInventory>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Command inventory is empty.");

        return inventory with
        {
            CommandSurfaceRows = inventory.CommandSurfaceRows ?? [],
            MenuToolbarRows = inventory.MenuToolbarRows ?? []
        };
    }

    private static bool HasCommandRows(CommandInventory inventory) =>
        inventory.CommandSurfaceRows.Count > 0 && inventory.MenuToolbarRows.Count > 0;

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
        if (section.Groups is { Count: > 0 })
        {
            var groupBlocks = section.Groups.Select(group =>
            {
                var lines = new List<string>
                {
                    $"### {group.Heading}",
                    "",
                    BuildCommandTable(section.ItemColumn, group.Rows)
                };
                return string.Join("\n", lines);
            });

            return string.Join("\n\n", groupBlocks);
        }

        return BuildCommandTable(section.ItemColumn, section.Rows ?? []);
    }

    private static string BuildCommandTable(string? itemColumn, IReadOnlyList<CommandInventoryCommandRow> rows)
    {
        var itemHeader = itemColumn ?? "Command";
        var lines = new List<string>
        {
            $"| {itemHeader} | Status | Notes |",
            "|---|---|---|"
        };

        lines.AddRange(rows.Select(row => $"| {row.Name} | {row.Status} | {row.Notes} |"));
        return string.Join("\n", lines);
    }

    private static string NormalizeLineEndings(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string Slug(string text) =>
        string.Join("-", text.Split(text.Where(ch => !char.IsLetterOrDigit(ch)).Distinct().ToArray(), StringSplitOptions.RemoveEmptyEntries))
            .ToLowerInvariant();

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
        IReadOnlyList<CommandInventoryCommandRow>? Rows,
        IReadOnlyList<CommandInventoryCommandGroup>? Groups);

    private sealed record CommandInventoryCommandGroup(
        string Heading,
        IReadOnlyList<CommandInventoryCommandRow> Rows);

    private sealed record CommandInventoryCommandRow(string Name, string Status, string Notes);

    private sealed record KeyTipExpectation(string Name, string KeyTip);
}
