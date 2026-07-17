using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class AvaloniaTitleBarQuickAccessToolbarTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task MainWindowHostsVisibleInteractiveQatInsideSharedDraggableTitleBar()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            window.Show();
            window.Measure(new Size(1120, 720));
            window.Arrange(new Rect(0, 0, 1120, 720));
            window.UpdateLayout();

            window.ExtendClientAreaToDecorationsHint.Should().BeTrue();
            window.ExtendClientAreaTitleBarHeightHint.Should().Be(34);
            window.HasWindowIconForTest.Should().BeTrue();

            var titleBar = window.GetVisualDescendants()
                .OfType<Border>()
                .Single(control => AutomationProperties.GetAutomationId(control) == "SisterAppTitleBar");
            WindowDecorationProperties.GetElementRole(titleBar)
                .Should().Be(WindowDecorationsElementRole.TitleBar);
            var titleText = window.GetVisualDescendants()
                .OfType<TextBlock>()
                .Single(control => AutomationProperties.GetAutomationId(control) == "SisterAppTitleText");
            titleText.Text.Should().Be(window.Title);

            var host = window.AvaloniaQuickAccessTitleBarHostForTest;
            host.Should().NotBeNull();
            host!.IsVisible.Should().BeTrue();
            WindowDecorationProperties.GetElementRole(host)
                .Should().Be(WindowDecorationsElementRole.User);
            host.Children.Should().ContainSingle()
                .Which.Should().BeSameAs(window.AvaloniaQuickAccessToolbarForTest);

            var toolbar = window.AvaloniaQuickAccessToolbarForTest;
            toolbar.Children.Should().NotBeEmpty();
            WindowDecorationProperties.GetElementRole(toolbar)
                .Should().Be(WindowDecorationsElementRole.User);

            var anchors = toolbar.Children.OfType<Button>().ToArray();
            anchors.Should().NotBeEmpty();
            anchors.Should().OnlyContain(button => button.ContextMenu != null);
            anchors.Should().OnlyContain(button =>
                WindowDecorationProperties.GetElementRole(button) == WindowDecorationsElementRole.User);

            window.Close();
        }, CancellationToken.None);
    }
}
