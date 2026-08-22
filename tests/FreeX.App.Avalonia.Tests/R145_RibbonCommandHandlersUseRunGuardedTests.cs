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
