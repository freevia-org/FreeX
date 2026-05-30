using System.IO;
using System.Text.Json;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

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
    public void CommandSurfaceTabCoverageCallouts_MatchInventoryTabCounts()
    {
        var inventory = LoadInventory();
        var doc = File.ReadAllLines(WorkspaceFileLocator.Find("docs", "COMMAND_SURFACE_PARITY.md"));

        var callouts = doc
            .Where(line => line.StartsWith("> **Tab coverage:", StringComparison.Ordinal))
            .Select(NormalizeLineEndings)
            .ToArray();

        callouts.Should().Equal(inventory.CommandSurfaceTabs.Select(BuildTabCoverageCallout));
    }

    [Fact]
    public void DrawTab_MenuToolbarDelta_IsExplicitlyTrackedInInventory()
    {
        var inventory = LoadInventory();
        var commandSurfaceDraw = inventory.CommandSurfaceTabs.Single(tab => tab.Name == "Draw");
        var menuToolbarDraw = inventory.MenuToolbarTabs.Single(tab => tab.Name == "Draw");

        menuToolbarDraw.Implemented.Should().Be(commandSurfaceDraw.Implemented + 1);
        menuToolbarDraw.Partial.Should().Be(commandSurfaceDraw.Partial);
        menuToolbarDraw.NotImplemented.Should().Be(commandSurfaceDraw.NotImplemented);
        menuToolbarDraw.Deferred.Should().Be(commandSurfaceDraw.Deferred);
        menuToolbarDraw.Excluded.Should().Be(commandSurfaceDraw.Excluded);
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
    public void UiTestCatalog_CommandInventoryCountsMatchInventory()
    {
        var inventory = LoadInventory();
        var catalog = File.ReadAllText(WorkspaceFileLocator.Find("docs", "UI_TEST_CATALOG.md"));
        var commandSurfaceInScope = inventory.CommandSurfaceTabs.Sum(tab => tab.Implemented + tab.Partial);
        var menuToolbarInScope = inventory.MenuToolbarTabs.Sum(tab => tab.Implemented + tab.Partial);

        catalog.Should().Contain(
            $"| Command surface in-scope rows | {commandSurfaceInScope} | From `COMMAND_INVENTORY.json`: Implemented + Partial command-surface rows. |");
        catalog.Should().Contain(
            $"| Menu/toolbar in-scope rows | {menuToolbarInScope} | Includes the current Draw tab menu/toolbar delta. |");
    }

    [Fact]
    public void CommandInventory_DefinesCurrentSchemaAndTopLevelKeyTipExpectations()
    {
        var inventory = LoadInventory();

        inventory.SchemaVersion.Should().Be(1);
        AssertCommandRows(inventory.CommandSurfaceRows, inventory.CommandSurfaceTabs, "Command");
        AssertCommandRows(inventory.MenuToolbarRows, inventory.MenuToolbarTabs, "Item");

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

    [Fact]
    public void HomeRows_AreGeneratedFromInventory()
    {
        var inventory = LoadInventory();
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

    private static void AssertCommandRows(
        IReadOnlyList<CommandInventoryCommandSection> sections,
        IReadOnlyList<CommandInventoryTab> tabs,
        string expectedItemColumn)
    {
        var allowedStatuses = new HashSet<string>(StringComparer.Ordinal)
        {
            "Implemented",
            "Partial",
            "Not Implemented",
            "Deferred",
            "Excluded"
        };

        sections.Should().NotBeEmpty();
        sections.Select(section => section.Name).Should().Equal(tabs.Select(tab => tab.Name));

        foreach (var section in sections)
        {
            section.ItemColumn.Should().Be(expectedItemColumn);
            var hasRows = section.Rows is { Count: > 0 };
            var hasGroups = section.Groups is { Count: > 0 };
            hasRows.Should().NotBe(hasGroups, $"{section.Name} should use either flat rows or grouped rows, not both");

            var rows = GetRows(section).ToArray();
            rows.Should().NotBeEmpty($"{section.Name} should have explicit command rows");
            rows.Select(row => row.Name).Should().OnlyHaveUniqueItems($"{section.Name} command rows should be unambiguous");
            rows.Should().OnlyContain(row => !string.IsNullOrWhiteSpace(row.Name));
            rows.Should().OnlyContain(row => allowedStatuses.Contains(row.Status), $"{section.Name} should use a known command status");
            rows.Where(row => row.Status is "Partial" or "Deferred")
                .Should()
                .OnlyContain(row => !string.IsNullOrWhiteSpace(row.Notes), $"{section.Name} partial/deferred rows should explain the remaining gap");

            foreach (var group in section.Groups ?? [])
            {
                group.Heading.Should().NotBeNullOrWhiteSpace();
                group.Rows.Should().NotBeEmpty($"{section.Name}/{group.Heading} should have command rows");
            }
        }
    }

    private static IEnumerable<CommandInventoryCommandRow> GetRows(CommandInventoryCommandSection section) =>
        section.Groups is { Count: > 0 }
            ? section.Groups.SelectMany(group => group.Rows)
            : section.Rows ?? [];

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

    private static string BuildTabCoverageCallout(CommandInventoryTab tab)
    {
        var inScope = tab.Implemented + tab.Partial + tab.NotImplemented;
        var qualifiers = new List<string>();
        if (tab.Deferred > 0)
            qualifiers.Add($"{tab.Deferred} Deferred");
        if (tab.Excluded > 0)
            qualifiers.Add($"{tab.Excluded} Excluded");

        var qualifierText = qualifiers.Count == 0
            ? string.Empty
            : $" ({string.Join(", ", qualifiers)})";

        return $"> **Tab coverage: {tab.Implemented} Implemented + {tab.Partial} Partial = {tab.CoveragePercent}% of {inScope} in-scope commands{qualifierText}**";
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
