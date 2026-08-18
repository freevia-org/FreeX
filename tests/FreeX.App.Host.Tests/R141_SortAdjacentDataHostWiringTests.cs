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
        // overload other than SortSelectedRange(bool) -- the only overload that consults
        // SortAdjacentDataPromptResolver (SortSelectedRange(SortDialogCommandPlan), used by the
        // Custom Sort dialog, does not).
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.DataFilterCommands.cs");

        source.Should().Contain("_session.SortSelectedRange(ascending: true)");
        source.Should().Contain("_session.SortSelectedRange(ascending: false)");
    }
}
