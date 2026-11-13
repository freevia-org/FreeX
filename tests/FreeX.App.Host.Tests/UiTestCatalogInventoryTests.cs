using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class UiTestCatalogInventoryTests
{







    private static string FindCatalogRow(IEnumerable<string> lines, string rowId) =>
        lines.Single(line => line.TrimStart().StartsWith($"| {rowId} |", StringComparison.Ordinal));
















    [Theory]
    [InlineData("screenshot_excel.ps1")]
    [InlineData("screenshot_ribbon.ps1")]
    public void ScreenshotScripts_DefineForegroundOwnershipGuard(string scriptName)
    {
        var script = ReadScreenshotToolScript(scriptName);

        // The foreground-ownership guard function itself lives only in the shared
        // tools/ScreenshotCaptureSupport.ps1 (dot-sourced below) since "Centralize screenshot
        // foreground helpers" (9540d1f960); Test-ToolScripts.ps1 forbids scenario scripts from
        // redeclaring it. This test asserts the script wires into that shared guard rather than
        // requiring a local `function Assert-ForegroundWindowOwnership` declaration.
        script.Should().Contain("GetForegroundWindow");
        script.Should().Contain("ScreenshotCaptureSupport.ps1");
        script.Should().Contain("Assert-ForegroundWindowOwnership");
        script.Should().Contain("GetWindowThreadProcessId($foreground");
        script.Should().Contain("GetWindowText($foreground");
    }

    [Fact]
    public void ExcelScreenshotScript_ChecksForegroundOwnershipBeforeEveryGlobalInput()
    {
        var lines = WorkspaceFileLocator.ReadAllLines("tools", "screenshot_excel.ps1");

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



    private static CommandCoverageSummary ReadCommandCoverageSummary(string fileName)
    {
        var lines = WorkspaceFileLocator.ReadAllLines("docs", fileName);
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



    private static IReadOnlyList<string> ReadVisibleTopLevelRibbonTabs()
        => FreeXRibbon.Build().VisibleTabs
            .Select(tab => FreeXRibbonTabPresentationCatalog.GetRequired(tab.Id).EnglishFallback)
            .ToArray();

    private static IReadOnlyList<string> ReadContextualRibbonTabs()
        => FreeXRibbon.Build().ContextualTabs
            .Select(tab => FreeXRibbonTabPresentationCatalog.GetRequired(tab.Id).EnglishFallback)
            .ToArray();

    private static string BuildInventorySnapshotNote(IReadOnlyList<CommandInventoryTab> tabs, string surface)
    {
        var implemented = tabs.Sum(tab => tab.Implemented);
        var partial = tabs.Sum(tab => tab.Partial);
        var total = tabs.Sum(tab => tab.Implemented + tab.Partial + tab.NotImplemented + tab.Deferred + tab.Excluded);
        var excluded = tabs.Sum(tab => tab.Excluded);
        return $"From `parity/command-inventory.json`: Implemented + Partial {surface} rows ({implemented} Implemented + {partial} Partial of {total} total, {excluded} Excluded).";
    }

    private static IReadOnlyList<string> ReadDialogTypeNames()
    {
        var hostDirectory = DialogSourceTestSupport.FindHostSourceDirectory("MainWindow.xaml");
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
        var matcher = DialogSourceTestSupport.ReadHostSources("KeyboardShortcutMatcher.CommandRules.cs");
        var dispatcher = DialogSourceTestSupport.ReadHostSources("MainWindow.KeyboardCommands.cs");

        return new KeyboardShortcutUsageCounts(
            CommandShortcutRuleDeclaration().Matches(matcher).Count,
            KeyboardCommandDispatcherRegistration().Matches(dispatcher).Count);
    }


    private static string ReadScreenshotToolScript(string scriptName) =>
        WorkspaceFileLocator.ReadAllText("tools", scriptName);

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


    private static int CountWorksheetContextMenuActionCommands(IEnumerable<WorksheetContextMenuCommand> commands)
    {
        var count = 0;
        foreach (var command in commands)
        {
            if (!command.IsSeparator && command.Action != WorksheetContextMenuAction.None)
                count++;

            count += CountWorksheetContextMenuActionCommands(command.Children);
        }

        return count;
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
