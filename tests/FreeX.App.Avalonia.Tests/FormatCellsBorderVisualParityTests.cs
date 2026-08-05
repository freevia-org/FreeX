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
using FluentAssertions.Execution;

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
        source.Should().Contain("Margin = new Thickness(8)");
        source.Should().Contain("ColumnDefinitions = new ColumnDefinitions(\"122,190,*\")");
        source.Should().Contain("ColumnDefinitions = new ColumnDefinitions(\"80,*,*\")");
        source.Should().Contain("ListBoxItemMinHeight = 20");
        source.Should().Contain("new Setter(TemplatedControl.BackgroundProperty, selectedItemBackground)");
        source.Should().NotContain("CreateFormatCellsBorderStyleSample");
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

                    var noneButton = FindControl<Button>(viewport, "FormatCellsBorderPresetNoneButton");
                    var outlineButton = FindControl<Button>(viewport, "FormatCellsBorderPresetOutlineButton");
                    var insideButton = FindControl<Button>(viewport, "FormatCellsBorderPresetInsideButton");
                    var styleList = FindControl<ListBox>(viewport, "FormatCellsBorderStyleBox");
                    var preview = FindControl<Border>(viewport, "FormatCellsBorderPreview");
                    var topToggle = FindControl<ToggleButton>(viewport, "FormatCellsBorderTopToggle");
                    var rightToggle = FindControl<ToggleButton>(viewport, "FormatCellsBorderRightToggle");
                    var bottomToggle = FindControl<ToggleButton>(viewport, "FormatCellsBorderBottomToggle");
                    var leftToggle = FindControl<ToggleButton>(viewport, "FormatCellsBorderLeftToggle");

                    using (new AssertionScope())
                    {
                        foreach (var preset in new[] { noneButton, outlineButton, insideButton })
                        {
                            preset.Bounds.Width.Should().Be(110);
                            preset.Bounds.Height.Should().Be(28);
                        }
                        VerticalOrigin(outlineButton, viewport).Should().BeApproximately(VerticalOrigin(noneButton, viewport) + 34, 0.01);
                        VerticalOrigin(insideButton, viewport).Should().BeApproximately(VerticalOrigin(outlineButton, viewport) + 34, 0.01);

                        styleList.Bounds.Width.Should().Be(178);
                        styleList.Bounds.Height.Should().Be(124);
                        HorizontalOrigin(styleList, viewport).Should().BeApproximately(HorizontalOrigin(noneButton, viewport) + 122, 2);
                        styleList.GetVisualDescendants().OfType<ListBoxItem>().Take(6)
                            .Select(item => item.Content?.ToString())
                            .Should().Equal("None", "Thin", "Medium", "Thick", "Dashed", "Dotted");

                        topToggle.Bounds.Size.Should().Be(new Size(144, 32));
                        bottomToggle.Bounds.Size.Should().Be(new Size(144, 32));
                        leftToggle.Bounds.Size.Should().Be(new Size(50, 100));
                        rightToggle.Bounds.Size.Should().Be(new Size(50, 100));
                        preview.Bounds.Size.Should().Be(new Size(144, 100));
                        HorizontalOrigin(topToggle, viewport).Should().BeApproximately(HorizontalOrigin(preview, viewport), 0.01);
                        (VerticalOrigin(topToggle, viewport) + 32).Should().BeApproximately(VerticalOrigin(preview, viewport), 0.01);
                        (HorizontalOrigin(leftToggle, viewport) + 50).Should().BeApproximately(HorizontalOrigin(preview, viewport), 0.01);
                        (HorizontalOrigin(preview, viewport) + 144).Should().BeApproximately(HorizontalOrigin(rightToggle, viewport), 0.01);
                        VerticalOrigin(bottomToggle, viewport).Should().BeApproximately(VerticalOrigin(preview, viewport) + 100, 0.01);

                        var styleBoxes = new[] { "Top", "Right", "Bottom", "Left" }
                            .Select(edge => FindControl<ComboBox>(viewport, $"FormatCellsBorder{edge}StyleBox"))
                            .ToArray();
                        var colorBoxes = new[] { "Top", "Right", "Bottom", "Left" }
                            .Select(edge => FindControl<TextBox>(viewport, $"FormatCellsBorder{edge}ColorTextBox"))
                            .ToArray();
                        styleBoxes.Select(box => HorizontalOrigin(box, viewport)).Distinct().Should().ContainSingle();
                        colorBoxes.Select(box => HorizontalOrigin(box, viewport)).Distinct().Should().ContainSingle();
                        colorBoxes.Should().AllSatisfy(box => box.Bounds.Width.Should().Be(120));
                        for (var row = 1; row < styleBoxes.Length; row++)
                        {
                            VerticalOrigin(styleBoxes[row], viewport).Should()
                                .BeApproximately(VerticalOrigin(styleBoxes[row - 1], viewport) + 30, 0.01);
                        }
                    }

                    AssertFullyInside(probe.Dialog, "FormatCellsOkButton");
                    AssertFullyInside(probe.Dialog, "FormatCellsCancelButton");
                    var okButton = FindControl<Button>(probe.Dialog, "FormatCellsOkButton");
                    var cancelButton = FindControl<Button>(probe.Dialog, "FormatCellsCancelButton");
                    okButton.Bounds.Size.Should().Be(new Size(74, 24));
                    cancelButton.Bounds.Size.Should().Be(new Size(74, 24));
                    HorizontalOrigin(cancelButton, probe.Dialog).Should()
                        .BeApproximately(HorizontalOrigin(okButton, probe.Dialog) + 82, 0.01);
                    VerticalOrigin(cancelButton, probe.Dialog).Should()
                        .BeApproximately(VerticalOrigin(okButton, probe.Dialog), 0.01);
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

    private static string RepoFile(params string[] parts) =>
        Path.Combine(
            [Directory.GetParent(TestWorkspaceFileLocator.FindDirectoryFromBaseDirectory("src"))!.FullName, .. parts]);

    private static void AssertFullyInside(Control root, string automationId)
    {
        var control = FindControl<Control>(root, automationId);
        control.Bounds.Width.Should().BeGreaterThan(0);
        control.Bounds.Height.Should().BeGreaterThan(0);

        var origin = control.TranslatePoint(default, root);
        origin.Should().NotBeNull($"{automationId} must share the rendered dialog visual tree");
        origin!.Value.X.Should().BeGreaterThanOrEqualTo(0);
        origin.Value.Y.Should().BeGreaterThanOrEqualTo(0);
        (origin.Value.X + control.Bounds.Width).Should().BeLessThanOrEqualTo(root.Bounds.Width + 0.01);
        (origin.Value.Y + control.Bounds.Height).Should().BeLessThanOrEqualTo(root.Bounds.Height + 0.01);
    }

    private static T FindControl<T>(Control root, string automationId)
        where T : Control
    {
        var control = root.GetVisualDescendants()
            .OfType<T>()
            .FirstOrDefault(candidate => AutomationProperties.GetAutomationId(candidate) == automationId);
        control.Should().NotBeNull($"{automationId} must be present in the rendered visual tree");
        return control!;
    }

    private static double HorizontalOrigin(Control control, Visual root) =>
        control.TranslatePoint(default, root)!.Value.X;

    private static double VerticalOrigin(Control control, Visual root) =>
        control.TranslatePoint(default, root)!.Value.Y;
}
