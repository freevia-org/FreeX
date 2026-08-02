using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
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
        source.Should().Contain("dialog.MinHeight = targetHeight");
        source.Should().Contain("internal void ResizeClient(Size size) => ClientSize = size");
        source.Should().Contain("dialog.ResizeClient(targetSize)");
        source.Should().Contain("borderInsideHorizontalToggle.IsVisible = false");
        source.Should().Contain("borderInsideVerticalToggle.IsVisible = false");
        source.Should().NotContain("Children = { borderInsideHorizontalToggle, borderInsideVerticalToggle }");
    }

    [Fact]
    public void ParityCapture_RejectsStaleX11BoundsInsteadOfPaddingToRequestedSize()
    {
        var captureSource = File.ReadAllText(
            RepoFile("src", "FreeX.App.Avalonia", "MainWindow.ParityCapture.cs"));
        captureSource.Should().Contain(
            "dialog.MinHeight,");
        captureSource.Should().NotContain("ResolveParityDialogCaptureDimension");

        var action = () => MainWindow.ResolveParityDialogCaptureSize(
            requestedWidth: 620,
            requestedHeight: 540,
            minimumWidth: 620,
            minimumHeight: 596.5,
            arrangedSize: new Size(620, 540));

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*refusing to pad an undersized capture*");
        MainWindow.ResolveParityDialogCaptureSize(620, 596.5, 620, 596.5, new Size(620, 596.5))
            .Should().Be(new Size(620, 596.5));
    }

    [Fact]
    public async Task FormatCellsBorderTab_RendersEveryDetailRowAndActionButtonInsideItsViewport()
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
                    probe.TabStrip.SelectedIndex = 3;
                    probe.Dialog.ClientSize.Should().Be(new Size(620, 596.5));
                    var renderSize = MainWindow.ResolveParityDialogCaptureSize(
                        probe.Dialog.Width,
                        probe.Dialog.Height,
                        probe.Dialog.MinWidth,
                        probe.Dialog.MinHeight,
                        probe.Dialog.ClientSize);

                    probe.Dialog.Measure(renderSize);
                    probe.Dialog.Arrange(new Rect(renderSize));
                    probe.Dialog.UpdateLayout();
                    Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);

                    var viewport = probe.BorderTab.Content.Should().BeOfType<ScrollViewer>().Subject;
                    foreach (var automationId in new[]
                    {
                        "FormatCellsBorderTopStyleBox",
                        "FormatCellsBorderRightStyleBox",
                        "FormatCellsBorderBottomStyleBox",
                        "FormatCellsBorderLeftStyleBox",
                    })
                    {
                        AssertFullyInside(viewport, automationId);
                    }

                    AssertFullyInside(probe.Dialog, "FormatCellsOkButton");
                    AssertFullyInside(probe.Dialog, "FormatCellsCancelButton");
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

    private static void AssertFullyInside(Control root, string automationId)
    {
        var control = root.GetVisualDescendants()
            .OfType<Control>()
            .FirstOrDefault(candidate => AutomationProperties.GetAutomationId(candidate) == automationId);
        control.Should().NotBeNull($"{automationId} must be present in the rendered visual tree");
        control!.Bounds.Width.Should().BeGreaterThan(0);
        control.Bounds.Height.Should().BeGreaterThan(0);

        var origin = control.TranslatePoint(default, root);
        origin.Should().NotBeNull($"{automationId} must share the rendered dialog visual tree");
        origin!.Value.X.Should().BeGreaterThanOrEqualTo(0);
        origin.Value.Y.Should().BeGreaterThanOrEqualTo(0);
        (origin.Value.X + control.Bounds.Width).Should().BeLessThanOrEqualTo(root.Bounds.Width + 0.01);
        (origin.Value.Y + control.Bounds.Height).Should().BeLessThanOrEqualTo(root.Bounds.Height + 0.01);
    }
}
