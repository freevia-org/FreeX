using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class UiTestCatalogInventoryTests
{
    [Fact]
    public void InventorySnapshot_MatchesSourceDerivedInventoryModel()
    {
        var snapshot = ReadInventorySnapshot();
        var inventory = ReadCommandInventory();
        var shortcutSummary = ReadShortcutSummary();
        var topLevelTabs = ReadVisibleTopLevelRibbonTabs();
        var contextualTabs = ReadContextualRibbonTabs();
        var dialogTypeNames = ReadDialogTypeNames();
        var xamlClickWiredControls = ReadMainWindowXamlClickHandlerCount();
        var xamlAutomationIds = ReadMainWindowXamlAutomationIdCount();
        var ribbonKeyTipMetadata = ReadMainWindowXamlRibbonKeyTipCount();
        var keyboardShortcutUsages = ReadKeyboardShortcutUsageCounts();
        var screenshotToolScripts = ReadDocumentedScreenshotToolScripts();
        var uiEvidenceScreenshotCount = ReadUiEvidenceScreenshotCount();
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
            "Contextual ribbon tab declarations",
            contextualTabs.Count,
            $"{string.Join(", ", contextualTabs)} from collapsed `MainWindow.xaml` tab declarations.");
        AssertSnapshotRow(
            snapshot,
            "Dialog source classes",
            dialogTypeNames.Count,
            "Unique `*Dialog` class/x:Class names in `src/FreeX.App.Host`.");
        AssertSnapshotRow(
            snapshot,
            "XAML click-wired controls",
            xamlClickWiredControls,
            "`Click=\"...\"` occurrences in `MainWindow.xaml` on latest synced `origin/main`.");
        AssertSnapshotRow(
            snapshot,
            "Explicit UIA automation ids",
            xamlAutomationIds,
            "`AutomationProperties.AutomationId=\"...\"` declarations in `MainWindow.xaml`.");
        AssertSnapshotRow(
            snapshot,
            "Ribbon keytip metadata declarations",
            ribbonKeyTipMetadata,
            "`RibbonTooltip.KeyTip=\"...\"` declarations in `MainWindow.xaml`.");
        AssertSnapshotRow(
            snapshot,
            "Keyboard command shortcut usages",
            keyboardShortcutUsages.MatcherRules,
            $"{keyboardShortcutUsages.MatcherRules} matcher rules / {keyboardShortcutUsages.DispatcherTargets} dispatcher targets");
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
        AssertSnapshotRow(
            snapshot,
            "Screenshot tool scripts",
            screenshotToolScripts.Count,
            $"{string.Join(", ", screenshotToolScripts.Select(script => $"`tools/{script}`"))} documented and present.");
        AssertSnapshotRow(
            snapshot,
            "Existing UI evidence screenshots",
            uiEvidenceScreenshotCount,
            "Historical PNG evidence artifacts were removed during documentation cleanup; append new evidence paths to the relevant row.");
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
        catalog.Should().Contain("Continue expanding the source-based machine-readable inventory guard");
    }

    [Fact]
    public void ScreenshotHarnessCatalogRow_DocumentsInAppRibbonTourPath()
    {
        var catalog = File.ReadAllText(WorkspaceFileLocator.Find("docs", "UI_TEST_CATALOG.md"));
        var row = catalog
            .Split(Environment.NewLine)
            .Single(line => line.StartsWith("| UI-CMD-HARNESS-001 |", StringComparison.Ordinal));
        var plannedCaptureCount = RibbonScreenshotTourPlanner.DefaultTabs.Count *
                                  RibbonScreenshotTourPlanner.DefaultWidths.Count;

        row.Should().Contain("FREEX_SS_TOUR=1");
        row.Should().Contain("FREEX_SS_TOUR_BURST=1");
        row.Should().Contain("FREEX_SS_TOUR_TABS");
        row.Should().Contain("FREEX_SS_TOUR_WIDTHS");
        row.Should().Contain($"{plannedCaptureCount} planned captures");
        row.Should().Contain($"{plannedCaptureCount * RibbonScreenshotTourPlanner.BurstPhases.Count} burst-phase captures");
        row.Should().Contain("ribbon_screenshot_tour_manifest.json");
        row.Should().Contain("resize breakpoint");
        row.Should().Contain("deletes only the currently requested plan");
        row.Should().Contain("max/1100/900/750 by tab matrix");
        row.Should().Contain("36 captures each");
        row.Should().Contain("excel_<WidthLabel>_<RibbonTab>.png");
        row.Should().Contain("ribbon_<WidthLabel>_<RibbonTab>.png");
        row.Should().Contain("PairKey");
        row.Should().Contain("ribbon:<WidthLabel>:<TabFileName>");
        row.Should().Contain("counterpart file names");
    }

    [Theory]
    [InlineData("screenshot_excel.ps1")]
    [InlineData("screenshot_ribbon.ps1")]
    public void ScreenshotScripts_DefineForegroundOwnershipGuard(string scriptName)
    {
        var script = ReadScreenshotToolScript(scriptName);

        script.Should().Contain("GetForegroundWindow");
        script.Should().Contain("function Assert-ForegroundWindowOwnership");
        script.Should().Contain("GetWindowThreadProcessId($foreground");
        script.Should().Contain("GetWindowText($foreground");
    }

    [Fact]
    public void ExcelScreenshotScript_ChecksForegroundOwnershipBeforeEveryGlobalInput()
    {
        var lines = File.ReadAllLines(WorkspaceFileLocator.Find("tools", "screenshot_excel.ps1"));

        for (var index = 0; index < lines.Length; index++)
        {
            if (!GlobalInputCall().IsMatch(lines[index]))
                continue;

            PreviousExecutableLine(lines, index).Should().Contain(
                "Assert-ForegroundWindowOwnership",
                $"global input on line {index + 1} must re-check foreground process and title immediately before sending input");
        }
    }

    [Fact]
    public void ExcelScreenshotScript_DoesNotSwallowGlobalInputFailures()
    {
        var script = ReadScreenshotToolScript("screenshot_excel.ps1");

        script.Should().NotContain("catch {}", "foreground guard failures must abort and discard invalid screenshots");
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
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        return document
            .Descendants(presentation + "TabItem")
            .Where(tab => tab.Attribute("Visibility")?.Value != "Collapsed")
            .Select(tab => tab.Attribute("Header")?.Value)
            .Where(header => !string.IsNullOrWhiteSpace(header))
            .Cast<string>()
            .Select(header => LocalizedXamlTestSupport.ResolveLocalizedValue(header) ?? header)
            .ToArray();
    }

    private static IReadOnlyList<string> ReadContextualRibbonTabs()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        return document
            .Descendants(presentation + "TabItem")
            .Where(tab => tab.Attribute("Visibility")?.Value == "Collapsed")
            .Select(tab => tab.Attribute("Header")?.Value)
            .Where(header => !string.IsNullOrWhiteSpace(header))
            .Cast<string>()
            .Select(header => LocalizedXamlTestSupport.ResolveLocalizedValue(header) ?? header)
            .ToArray();
    }

    private static IReadOnlyList<string> ReadDialogTypeNames()
    {
        var hostDirectory = Path.GetDirectoryName(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"))
            ?? throw new DirectoryNotFoundException("Could not locate FreeX.App.Host.");
        var dialogNames = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var sourceFile in Directory.EnumerateFiles(hostDirectory, "*.cs", SearchOption.TopDirectoryOnly))
        {
            var source = File.ReadAllText(sourceFile);
            foreach (Match match in DialogClassDeclaration().Matches(source))
            {
                dialogNames.Add(match.Groups["name"].Value);
            }
        }

        foreach (var xamlFile in Directory.EnumerateFiles(hostDirectory, "*.xaml", SearchOption.TopDirectoryOnly))
        {
            var xaml = File.ReadAllText(xamlFile);
            foreach (Match match in DialogXamlClassDeclaration().Matches(xaml))
            {
                dialogNames.Add(match.Groups["name"].Value.Split('.').Last());
            }
        }

        return dialogNames.ToArray();
    }

    private static int ReadMainWindowXamlClickHandlerCount()
        => RibbonXamlCatalogSnapshotReader.ReadMainWindowSnapshot().ClickHandlerCount;

    private static int ReadMainWindowXamlAutomationIdCount()
        => RibbonXamlCatalogSnapshotReader.ReadMainWindowSnapshot().AutomationIdCount;

    private static int ReadMainWindowXamlRibbonKeyTipCount()
        => RibbonXamlCatalogSnapshotReader.ReadMainWindowSnapshot().RibbonKeyTipCount;

    private static KeyboardShortcutUsageCounts ReadKeyboardShortcutUsageCounts()
    {
        var matcher = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "KeyboardShortcutMatcher.CommandRules.cs"));
        var dispatcher = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.KeyboardCommands.cs"));

        return new KeyboardShortcutUsageCounts(
            CommandShortcutRuleDeclaration().Matches(matcher).Count,
            KeyboardCommandDispatcherRegistration().Matches(dispatcher).Count);
    }

    private static IReadOnlyList<string> ReadDocumentedScreenshotToolScripts()
    {
        var catalog = File.ReadAllText(WorkspaceFileLocator.Find("docs", "UI_TEST_CATALOG.md"));
        var scripts = ScreenshotToolPath()
            .Matches(catalog)
            .Select(match => match.Groups["script"].Value)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        foreach (var script in scripts)
        {
            File.Exists(WorkspaceFileLocator.Find("tools", script)).Should().BeTrue();
        }

        return scripts;
    }

    private static string ReadScreenshotToolScript(string scriptName) =>
        File.ReadAllText(WorkspaceFileLocator.Find("tools", scriptName));

    private static string PreviousExecutableLine(IReadOnlyList<string> lines, int index)
    {
        for (var previous = index - 1; previous >= 0; previous--)
        {
            var line = lines[previous].Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            return line;
        }

        return string.Empty;
    }

    private static int ReadUiEvidenceScreenshotCount()
    {
        var docsDirectory = Path.GetDirectoryName(WorkspaceFileLocator.Find("docs", "README.md"))
            ?? throw new DirectoryNotFoundException("Could not locate docs.");
        var artifactDirectory = Path.Combine(docsDirectory, "ui-test-artifacts");

        return Directory
            .EnumerateFiles(artifactDirectory, "*.png", SearchOption.TopDirectoryOnly)
            .Count();
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

    [GeneratedRegex(@"\bnew\(KeyboardCommandShortcut\.")]
    private static partial Regex CommandShortcutRuleDeclaration();

    [GeneratedRegex(@"_keyboardCommandDispatcher\.Register\(KeyboardCommandShortcut\.")]
    private static partial Regex KeyboardCommandDispatcherRegistration();

    [GeneratedRegex(@"\bclass\s+(?<name>[A-Za-z0-9_]*Dialog)\b")]
    private static partial Regex DialogClassDeclaration();

    [GeneratedRegex(@"x:Class=""(?<name>[A-Za-z0-9_.]*Dialog)""")]
    private static partial Regex DialogXamlClassDeclaration();

    [GeneratedRegex(@"`tools/(?<script>screenshot_(?:excel|ribbon)\.ps1)`")]
    private static partial Regex ScreenshotToolPath();

    [GeneratedRegex(@"\[System\.Windows\.Forms\.SendKeys\]::SendWait\(|\[Clicker\]::mouse_event\(")]
    private static partial Regex GlobalInputCall();

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

    private sealed record KeyboardShortcutUsageCounts(int MatcherRules, int DispatcherTargets);
}
