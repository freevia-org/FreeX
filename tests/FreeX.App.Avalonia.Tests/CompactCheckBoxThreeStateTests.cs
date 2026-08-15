using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.VisualTree;
using FluentAssertions;
using Free.Shared.Shell;
using Free.Shared.Shell.Avalonia;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// The shared compact check-box owns three-state ("partial selection") painting for FreeX, FreeW and
/// FreeP. Its indeterminate state must show only the indeterminate bar -- the tick belongs to the
/// checked state alone.
///
/// This is regression coverage for a real defect in the shared template: the tick's IsVisible was
/// bound straight to CheckBox.IsChecked, which is bool?. In the indeterminate state that binding has
/// no bool to produce, so Avalonia fell back to IsVisibleProperty's default of true and painted the
/// tick on top of the indeterminate bar. FreeX's pivot filter dialog had worked around it with a
/// private local template, which re-localized chrome the dedup campaign had deliberately shared.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class CompactCheckBoxThreeStateTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public Task IndeterminateCheckBox_ShowsOnlyTheIndeterminateMark() =>
        Session.Dispatch(
            () =>
            {
                var (tick, bar) = BuildMarks(isChecked: null);

                bar.IsVisible.Should().BeTrue("the indeterminate bar is the partial-selection mark");
                tick.IsVisible.Should().BeFalse(
                    "a partially selected check box must not also paint the checked tick");
            },
            CancellationToken.None);

    [Fact]
    public Task CheckedCheckBox_ShowsOnlyTheTick() =>
        Session.Dispatch(
            () =>
            {
                var (tick, bar) = BuildMarks(isChecked: true);

                tick.IsVisible.Should().BeTrue();
                bar.IsVisible.Should().BeFalse();
            },
            CancellationToken.None);

    [Fact]
    public Task UncheckedCheckBox_ShowsNeitherMark() =>
        Session.Dispatch(
            () =>
            {
                var (tick, bar) = BuildMarks(isChecked: false);

                tick.IsVisible.Should().BeFalse();
                bar.IsVisible.Should().BeFalse();
            },
            CancellationToken.None);

    /// <summary>
    /// Realizes the shared compact template and returns its two marks. The tick is the only Path in
    /// the template; the indeterminate bar is the small solid Border carrying the mark size from
    /// CompactDialogVisualTokens, which distinguishes it from the indicator frame around it.
    /// </summary>
    private static (Control Tick, Control Bar) BuildMarks(bool? isChecked)
    {
        var checkBox = new CheckBox { IsThreeState = true };
        AvaloniaCompactDialogChrome.ApplyCompactCheckBox(
            checkBox,
            new AvaloniaCompactDialogChromeStyle("Segoe UI"));

        var root = new Window { Content = checkBox };
        root.Show();
        checkBox.IsChecked = isChecked;
        root.UpdateLayout();

        var tick = checkBox.GetVisualDescendants()
            .OfType<global::Avalonia.Controls.Shapes.Path>()
            .Single();
        var bar = checkBox.GetVisualDescendants()
            .OfType<Border>()
            .Single(b =>
                b.Width == CompactDialogVisualTokens.CheckBoxIndeterminateMarkWidth &&
                b.Height == CompactDialogVisualTokens.CheckBoxIndeterminateMarkHeight);

        return (tick, bar);
    }
}
