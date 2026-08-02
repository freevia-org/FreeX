using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FreeX.App.Avalonia;
using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Structural parity checks for the Format Cells Border tab. The WPF authority uses the shared
/// circular-chevron expander for the individual-edge section and does not expose separate inside
/// horizontal/vertical preview buttons.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class FormatCellsBorderVisualParityTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public void FormatCellsBorderSource_UsesWpfExpanderAndKeepsInsideControlsOutOfTheVisualTree()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("var borderDetailsExpander = new Expander");
        source.Should().Contain("IsExpanded = true");
        source.Should().Contain("Margin = new Thickness(0, 8, 0, 0)");
        source.Should().Contain("AvaloniaCompactDialogChrome.ApplyWpfExpander(borderDetailsExpander)");
        source.Should().Contain("borderInsideHorizontalToggle.IsVisible = false");
        source.Should().Contain("borderInsideVerticalToggle.IsVisible = false");
        source.Should().NotContain("Children = { borderInsideHorizontalToggle, borderInsideVerticalToggle }");
    }

    [Fact]
    public async Task FormatCellsBorderTab_ContainsExpandedDetailsExpander_WithoutInsideButtons()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                window.Show();
                window.Measure(new Size(1120, 720));
                window.Arrange(new Rect(0, 0, 1120, 720));
                window.UpdateLayout();

                var dialogTask = window.ShowFormatCellsInputDialogAsync(probe =>
                {
                    var borderBody = ((ScrollViewer)probe.BorderTab.Content!).Content as Panel;
                    borderBody.Should().NotBeNull();

                    var expander = borderBody!.Children.OfType<Expander>().Single();
                    expander.IsExpanded.Should().BeTrue();
                    expander.Header?.ToString().Should().Contain("Individual border details");
                    expander.Content.Should().BeOfType<Border>();
                    ((Border)expander.Content!).Margin.Top.Should().Be(8);

                    probe.BorderTab.GetVisualDescendants()
                        .OfType<ToggleButton>()
                        .Where(button =>
                            (AutomationProperties.GetAutomationId(button) ?? string.Empty)
                                .Contains("Inside", StringComparison.Ordinal))
                        .Should().BeEmpty();

                    probe.CancelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                });

                await dialogTask;
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    private static string RepoFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src")))
            directory = directory.Parent;

        directory.Should().NotBeNull("the test must run inside the repository checkout");
        return Path.Combine(new[] { directory!.FullName }.Concat(parts).ToArray());
    }
}
