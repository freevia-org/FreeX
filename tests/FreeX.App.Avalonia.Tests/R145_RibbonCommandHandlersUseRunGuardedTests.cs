using System.IO;
using System.Text.RegularExpressions;

using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Round 145 / finding async-hazards F2: ribbon and menu Action delegates in MainWindow.cs must not
/// fire-and-forget an async dialog method via the bare `() => _ = FooAsync()` pattern. Because the
/// returned Task is discarded, any exception thrown by FooAsync (even before its first await) is
/// captured into that discarded Task instead of propagating synchronously to
/// AvaloniaRibbonRenderer.Execute's try/catch, so it becomes an unobserved Task exception: no dialog,
/// no status-bar message, no crash -- just a silently no-op command (and a log entry nobody sees).
/// The fix routes every such handler through the existing RunGuarded(Func&lt;Task&gt;) helper
/// (MainWindow.ContextualTabs.cs), whose whole purpose (per its own doc comment) is to surface a
/// thrown exception on the status bar instead of letting it vanish as an unobserved task exception.
/// </summary>
public sealed class R145_RibbonCommandHandlersUseRunGuardedTests
{
    // Matches the hazardous pattern this finding is about: a zero-arg lambda whose body discards an
    // async call's Task via `_ = `. Deliberately does NOT match `_ = await ...Async(...)` (that form
    // is awaited, so the exception propagates normally through the enclosing async method -- it is not
    // fire-and-forget and is out of scope for this finding).
    private static readonly Regex BareFireAndForgetAsync =
        new(@"_\s*=\s*[A-Za-z_][A-Za-z0-9_]*Async\(", RegexOptions.Compiled);

    private static readonly Regex DirectAsyncClickHandler =
        new(@"\.Click\s*\+=\s*async\b", RegexOptions.Compiled);

    [Fact]
    public void AvaloniaClickHandlers_DoNotUseDirectAsyncVoidLambdas()
    {
        var sourceDirectory = Path.GetDirectoryName(
            RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"))!;
        var offenders = Directory
            .EnumerateFiles(sourceDirectory, "*.cs", SearchOption.AllDirectories)
            .SelectMany(path => File.ReadLines(path)
                .Select((line, index) => new { Path = path, Line = line, Number = index + 1 }))
            .Where(candidate => DirectAsyncClickHandler.IsMatch(candidate.Line))
            .Select(candidate => $"{Path.GetFileName(candidate.Path)}:{candidate.Number}: {candidate.Line.Trim()}")
            .ToList();

        offenders.Should().BeEmpty(
            "Avalonia Click delegates are void-returning, so direct async lambdas let faults escape " +
            "to the dispatcher; route the Task through RunGuarded instead. Offenders:\n" +
            string.Join("\n", offenders));
    }

    [Fact]
    public void ClipboardToolbarHandlers_RouteThroughRunGuarded()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        source.Should().MatchRegex(
            @"private void CopyButton_Click\(object\? sender, RoutedEventArgs e\) =>\r?\n\s*RunGuarded\(CopySelectedRangeToClipboardAsync\);");
        source.Should().MatchRegex(
            @"private void PasteButton_Click\(object\? sender, RoutedEventArgs e\) =>\r?\n\s*RunGuarded\(PasteClipboardTextAsync\);");
        source.Should().NotContain("private async void CopyButton_Click");
        source.Should().NotContain("private async void PasteButton_Click");
    }

    [Fact]
    public void MainWindow_RibbonAndMenuHandlers_DoNotBareFireAndForgetAsyncCalls()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        var offendingLines = source
            .Split('\n')
            .Where(line => BareFireAndForgetAsync.IsMatch(line) && !line.TrimStart().StartsWith("_ = await", StringComparison.Ordinal) && !Regex.IsMatch(line, @"_\s*=\s*await\b"))
            .ToList();

        offendingLines.Should().BeEmpty(
            "every ribbon/menu Action handler in MainWindow.cs must route its async dialog call through " +
            "RunGuarded(...) so a thrown exception is surfaced on the status bar instead of becoming an " +
            "unobserved Task exception (finding async-hazards F2). Offending lines:\n" +
            string.Join("\n", offendingLines.Select(l => l.Trim())));
    }

    // Round 176 / finding sweep114-F1: the two Facts above only ever read MainWindow.cs and
    // MainWindow.ContextualTabs.cs, but the RunGuarded(...) handler-dictionary pattern this guard
    // polices is used across ~130 MainWindow partial files (dozens of which use RunGuarded directly,
    // e.g. MainWindow.RibbonMenuWires.cs). A bare `_ = FooAsync()` handler introduced in any of those
    // other files left both Facts green -- the scan never read the file containing the violation. This
    // Fact widens coverage to every MainWindow partial class file, found by scanning the project
    // directory tree rather than by a hand-maintained file list, so a newly created partial file (e.g.
    // one added under a feature subfolder like Charts/MainWindow.Charts.cs) is covered automatically
    // the day it is created, with no test edit required.
    [Fact]
    public void AllMainWindowPartialFiles_DoNotBareFireAndForgetAsyncCalls()
    {
        var avaloniaProjectDir = RepoFile("src", "FreeX.App.Avalonia");

        var partialFiles = Directory
            .GetFiles(avaloniaProjectDir, "MainWindow*.cs", SearchOption.AllDirectories)
            .Where(file => File.ReadAllText(file).Contains("partial class MainWindow"))
            .OrderBy(file => file, StringComparer.Ordinal)
            .ToList();

        // Guard the guard: if the derived file set were ever accidentally narrowed (e.g. by a filter
        // typo), this scan would silently cover nothing and report false-green, same failure mode as
        // the finding itself. Assert it actually finds the known MainWindow partial-class population.
        partialFiles.Should().HaveCountGreaterThan(100,
            "the derived MainWindow-partial file set should find the whole ~130-file population " +
            "(including nested ones like Charts/MainWindow.Charts.cs), not a hand-picked subset");

        var offendingLines = partialFiles
            .SelectMany(file => File.ReadAllLines(file)
                .Select((line, index) => (file, line, number: index + 1))
                .Where(entry => BareFireAndForgetAsync.IsMatch(entry.line) && !Regex.IsMatch(entry.line, @"_\s*=\s*await\b")))
            .Select(entry => $"{Path.GetFileName(entry.file)}:{entry.number}: {entry.line.Trim()}")
            .ToList();

        offendingLines.Should().BeEmpty(
            "every ribbon/menu Action handler in every MainWindow partial file must route its async " +
            "dialog call through RunGuarded(...) so a thrown exception is surfaced on the status bar " +
            "instead of becoming an unobserved Task exception (finding async-hazards F2 / sweep114-F1). " +
            "Offending lines:\n" + string.Join("\n", offendingLines));
    }

    [Fact]
    public void FormatCellsRibbonCommand_RoutesThroughRunGuarded()
    {
        // The finding's headline reproduction: Ctrl+1 / Home > Cells > Format > Format Cells, wired at
        // the ["Format Cells"] entry. Before the fix this read `() => _ = ShowFormatCellsDialogAsync()`.
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("[\"Format Cells\"] = () => RunGuarded(() => ShowFormatCellsDialogAsync())");
        source.Should().NotContain("[\"Format Cells\"] = () => _ = ShowFormatCellsDialogAsync()");

        // The finding also names these four duplicated Format-Cells-dialog launchers explicitly.
        source.Should().Contain("[\"More Borders\"] = () => RunGuarded(() => ShowFormatCellsDialogAsync())");
        source.Should().Contain("[\"Format\"] = () => RunGuarded(() => ShowFormatCellsDialogAsync())");
        source.Should().Contain("[\"More Accounting Formats\"] = () => RunGuarded(() => ShowFormatCellsDialogAsync())");
        source.Should().Contain("RunGuarded(() => ShowFormatCellsDialogAsync());");
    }

    [Fact]
    public void AdjacentAlreadyGuardedContextualTabHandlers_AreUnaffected()
    {
        // Sibling/no-regression check: MainWindow.ContextualTabs.cs already used RunGuarded correctly
        // before this fix (e.g. PivotChart Change Type/Options, Shape Effects) -- this file is owned by
        // a different fix wave, so it must remain untouched by the MainWindow.cs-scoped fix here.
        var contextualTabsSource = File.ReadAllText(
            RepoFile("src", "FreeX.App.Avalonia", "MainWindow.ContextualTabs.cs"));

        contextualTabsSource.Should().Contain(
            "[FreeXRibbonCommandIds.PivotChartInsert] = InsertPivotChart");
        contextualTabsSource.Should().Contain(
            "[FreeXRibbonCommandIds.PivotChartChangeType] = () => RunGuarded(ChangeActivePivotChartTypeAsync)");
        contextualTabsSource.Should().Contain(
            "[\"PivotChart Options\"] = () => RunGuarded(OpenPivotChartOptionsAsync)");
        contextualTabsSource.Should().Contain(
            "[\"Shape Effects\"] = () => RunGuarded(OpenShapeEffectsDialogAsync)");

        var offendingLines = contextualTabsSource
            .Split('\n')
            .Where(line => BareFireAndForgetAsync.IsMatch(line) && !Regex.IsMatch(line, @"_\s*=\s*await\b"))
            .ToList();

        offendingLines.Should().BeEmpty(
            "MainWindow.ContextualTabs.cs already followed the RunGuarded contract before this fix and " +
            "must not have regressed into the bare fire-and-forget pattern.");
    }

    private static string RepoFile(params string[] parts) =>
        Path.Combine([TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx"), .. parts]);
}
