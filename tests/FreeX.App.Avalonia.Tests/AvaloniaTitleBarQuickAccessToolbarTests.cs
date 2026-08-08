using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.VisualTree;
using FreeX.App.Services;
using FreeX.App.Services.Ribbon;

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

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task MainWindowMovesOneQatBetweenTitleBarAndBelowRibbonHosts()
    {
        var previousEnv = Environment.GetEnvironmentVariable(AppOptionsStore.OptionsPathEnvironmentVariable);
        var tempPath = Path.Combine(Path.GetTempPath(), $"freex-qat-placement-{Guid.NewGuid():N}.json");
        try
        {
            var options = new AppOptions
            {
                QuickAccessToolbarBelowRibbon = true,
                QuickAccessToolbarCommands = [QuickAccessToolbarCommandIds.Save, QuickAccessToolbarCommandIds.Bold],
            };
            AppOptionsStore.SaveToPath(options, tempPath).Should().BeTrue();
            Environment.SetEnvironmentVariable(AppOptionsStore.OptionsPathEnvironmentVariable, tempPath);

            await Session.Dispatch(() =>
            {
                var window = new MainWindow([]);
                window.Show();
                window.Measure(new Size(1120, 720));
                window.Arrange(new Rect(0, 0, 1120, 720));
                window.UpdateLayout();

                var toolbar = window.AvaloniaQuickAccessToolbarForTest;
                var titleHost = window.AvaloniaQuickAccessTitleBarHostForTest;
                var belowHost = window.AvaloniaQuickAccessBelowRibbonHostForTest;
                titleHost.Should().NotBeNull();
                belowHost.Should().NotBeNull();
                belowHost!.Child.Should().BeSameAs(toolbar);
                belowHost.IsVisible.Should().BeTrue();
                belowHost.Height.Should().Be(30);
                titleHost!.Children.Should().NotContain(toolbar);
                var belowButtons = toolbar.Children.OfType<Button>().ToArray();
                belowButtons.Should().NotBeEmpty();
                foreach (var button in belowButtons)
                {
                    button.Foreground.Should().BeOfType<ImmutableSolidColorBrush>();
                    ((ImmutableSolidColorBrush)button.Foreground!).Color.Should().Be(Color.FromRgb(25, 31, 40));
                }

                window.SetAvaloniaQuickAccessPlacementForTest(false);
                window.UpdateLayout();

                belowHost.Child.Should().BeNull();
                belowHost.IsVisible.Should().BeFalse();
                belowHost.Height.Should().Be(0);
                titleHost.Children.Should().ContainSingle().Which.Should().BeSameAs(toolbar);
                foreach (var button in toolbar.Children.OfType<Button>())
                {
                    button.Foreground.Should().BeOfType<ImmutableSolidColorBrush>();
                    ((ImmutableSolidColorBrush)button.Foreground!).Color.Should().Be(Colors.White);
                }

                window.AllowCloseWithoutDirtyPromptForParityCapture();

                window.Close();
            }, CancellationToken.None);
        }
        finally
        {
            Environment.SetEnvironmentVariable(AppOptionsStore.OptionsPathEnvironmentVariable, previousEnv);
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }
}
