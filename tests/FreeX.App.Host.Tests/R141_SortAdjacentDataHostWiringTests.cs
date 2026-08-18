using FluentAssertions;

namespace FreeX.App.Host.Tests;

/// <summary>
/// R141-services-sort-adjacent-data-1 built <c>WorkbookSession.SortAdjacentDataPromptResolver</c> and
/// <c>WorkbookSession.SortSelectedRange(bool)</c>'s consultation of it (mirroring the pre-existing
/// <c>DataValidationPromptResolver</c> seam), but never wired a resolver into the WPF host -- the
/// property stayed null in production, so a real user selecting only part of a wider table and
/// clicking ribbon Sort Ascending/Descending still got the silent, unwarned, record-scrambling sort
/// the original finding described.
///
/// The WPF host has no headless-constructible <c>MainWindow</c> (it is a real STA <c>Window</c>), so
/// unlike the Avalonia shell's <c>R141_SortAdjacentDataHostWiringTests</c> -- which drives the actual
/// resolver through a real <c>MainWindow</c> instance -- these are source-contract tests: they read
/// the shipped host source text and assert the exact wiring line is present, following the same
/// pattern <c>DataValidationDialogTests.DataValidationViolationMessages_UseOwnedMainWindowMessageHelper</c>
/// already uses to guard the WPF host's <c>DataValidationPromptResolver</c> wiring. Deleting the
/// wire-up line (or routing the ribbon Sort buttons through some other session call that skips the
/// resolver) fails these tests even though they never execute the resolver at runtime.
/// </summary>
public sealed class R141_SortAdjacentDataHostWiringTests
{
    [Fact]
    public void SortAdjacentDataPromptResolver_IsWiredInConfigureWorkbookSessionRendererAdapters()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.Editing.cs");

        source.Should().Contain("private void ConfigureWorkbookSessionRendererAdapters()");
        source.Should().Contain("_session.SortAdjacentDataPromptResolver = ResolveSortAdjacentDataPrompt;");
        source.Should().Contain("private UserMessageResult ResolveSortAdjacentDataPrompt(SortAdjacentDataPromptRequest request)");
        source.Should().Contain("ShowOwnedSynchronousPrompt(FreeXSynchronousPromptCatalog.ForSortAdjacentData())");
    }

    [Fact]
    public void ConfigureWorkbookSessionRendererAdapters_IsCalledOnEverySessionReplacement()
    {
        // Mirrors DataValidationPromptResolver's own wiring: both resolvers are set by the same
        // method, so proving it runs on every session (re)assignment covers Sort's wiring too.
        var lifecycleSource = DialogSourceTestSupport.ReadHostSources("MainWindow.WorkbookLifecycle.cs");
        var xamlCsSource = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml.cs");

        lifecycleSource.Should().Contain("ConfigureWorkbookSessionRendererAdapters();");
        xamlCsSource.Should().Contain("ConfigureWorkbookSessionRendererAdapters();");
    }

    [Fact]
    public void SortAscendingAndDescendingButtons_RouteThroughTheResolverConsultingSessionOverload()
    {
        // Guards against a future refactor that points the ribbon Sort buttons at a session
        // overload other than SortSelectedRange(bool) -- the overload SortSelectedRange(bool)
        // consults SortAdjacentDataPromptResolver directly; SortSelectedRange(SortDialogCommandPlan)
        // (the single-arg overload) still does not and must not (see its own remarks) -- the Custom
        // Sort dialog now resolves the same warning itself, up front, via
        // ResolveSortRangeAfterAdjacentDataPrompt, before ever building the dialog (R142-services-
        // sort-customdialog-1, verified below).
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.DataFilterCommands.cs");

        source.Should().Contain("_session.SortSelectedRange(ascending: true)");
        source.Should().Contain("_session.SortSelectedRange(ascending: false)");
    }

    [Fact]
    public void SortCustomButton_ResolvesSortWarningBeforeBuildingDialogAndExecutesViaTwoArgOverload()
    {
        // R142-services-sort-customdialog-1: the Custom Sort dialog path previously never consulted
        // SortAdjacentDataPromptResolver at all -- selecting a proper subset of a wider table and
        // using Custom Sort silently sorted just the selected columns, scrambling records, exactly
        // like the ribbon-button bug R141 fixed for Quick Sort. Fixed by resolving the same warning
        // up front (before the dialog's column/row/color/icon choices are built from the winning
        // range) and executing against that resolved range via the two-arg overload.
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.DataFilterCommands.cs");

        source.Should().Contain("var range = _session.ResolveSortRangeAfterAdjacentDataPrompt(rawRange);");
        source.Should().Contain("_session.SortSelectedRange(sortPlan, range)");
    }
}
