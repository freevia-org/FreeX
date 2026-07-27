using Avalonia.Headless;
using System.Threading;
using FreeW.App.Avalonia;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.Shell;

namespace FreeW.App.Avalonia.Tests;

public sealed class ReadModeParityTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Fact]
    public async Task ReadMode_MatchesWpfAuthorityAndDoesNotMutateDocumentState()
    {
        await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var editor = window.Editor;
            var originalView = editor.ViewMode;
            var originalMaxWidth = editor.MaxWidth;
            var originalMargin = editor.Margin;
            var originalAlignment = editor.HorizontalAlignment;
            var originalPageColor = editor.Document.Page.BackgroundColorHex;
            window.SetReadModePaneVisibilityForTests(navigation: false, reveal: true, reviewing: false);
            var originalNavPaneVisible = window.IsNavigationPaneVisibleForTests;
            var originalRevealPaneVisible = window.IsRevealPaneVisibleForTests;
            var originalReviewingPaneVisible = window.IsReviewingPaneVisibleForTests;

            window.ApplyReadModeColumnWidthForTests("wide");
            window.ApplyReadModePageColorForTests("sepia");
            window.ToggleReadModeForTests();

            window.IsReadModeActiveForTests.Should().BeTrue();
            window.ReadModeMaxWidthForTests.Should().Be(FreeWReadModePlanner.WideColumnWidth);
            window.ReadModeBackgroundForTests.Should().Be(FreeWReadModePlanner.SepiaColorHex);
            window.IsTitleBarVisibleForTests.Should().BeFalse();
            window.IsRibbonVisibleForTests.Should().BeFalse();
            window.IsNavigationPaneVisibleForTests.Should().BeFalse();
            window.IsRevealPaneVisibleForTests.Should().BeFalse();
            window.IsReviewingPaneVisibleForTests.Should().BeFalse();
            editor.ViewMode.Should().Be(originalView);
            editor.Document.Page.BackgroundColorHex.Should().Be(originalPageColor);

            window.ToggleReadModeForTests();

            window.IsReadModeActiveForTests.Should().BeFalse();
            window.IsTitleBarVisibleForTests.Should().BeTrue();
            window.IsRibbonVisibleForTests.Should().BeTrue();
            window.IsNavigationPaneVisibleForTests.Should().Be(originalNavPaneVisible);
            window.IsRevealPaneVisibleForTests.Should().Be(originalRevealPaneVisible);
            window.IsReviewingPaneVisibleForTests.Should().Be(originalReviewingPaneVisible);
            window.ReadModeBackgroundForTests.Should().BeNull();
            editor.ViewMode.Should().Be(originalView);
            editor.MaxWidth.Should().Be(originalMaxWidth);
            editor.Margin.Should().Be(originalMargin);
            editor.HorizontalAlignment.Should().Be(originalAlignment);
            editor.Document.Page.BackgroundColorHex.Should().Be(originalPageColor);
        });
    }

    [Fact]
    public async Task ReadModeRibbonCommands_ExposeSharedOptionsAndStatefulToggle()
    {
        await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var registry = window.RibbonRegistryForTests;
            registry.Should().NotBeNull();
            foreach (var commandId in new[]
            {
                "freew.read-mode",
                "freew.read-mode-column-narrow",
                "freew.read-mode-column-default",
                "freew.read-mode-column-wide",
                "freew.read-mode-color-none",
                "freew.read-mode-color-sepia",
                "freew.read-mode-color-inverse",
            })
            {
                registry.TryGet(commandId, out _).Should().BeTrue(commandId);
            }

            registry.TryGet("freew.read-mode-column-wide", out var wide).Should().BeTrue();
            wide!.Execute(Free.Shared.Ribbon.RibbonCommandContext.Empty);
            window.ToggleReadModeForTests();
            window.ReadModeMaxWidthForTests.Should().Be(FreeWReadModePlanner.WideColumnWidth);

            registry.TryGet("freew.read-mode", out var toggle).Should().BeTrue();
            var stateful = toggle as Free.Shared.Ribbon.IRibbonStatefulCommand;
            stateful.Should().NotBeNull();
            stateful.GetState().IsChecked.Should().BeTrue();
            toggle!.Execute(Free.Shared.Ribbon.RibbonCommandContext.Empty);
            stateful.GetState().IsChecked.Should().BeFalse();
        });
    }

    private static Task OnUiThread(Action action) =>
        Session.Dispatch(action, CancellationToken.None);
}
