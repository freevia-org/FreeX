using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using FreeP.App.Compositor;

namespace FreeP.App.Avalonia.Tests;

public sealed class SingleTargetZoomDialogTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreePHeadlessApp).Assembly);

    private static readonly IReadOnlyList<(string Id, string DisplayName)> Options =
    [
        ("first", "First target"),
        ("second", "Second target"),
    ];

    [Fact]
    public async Task Renderer_preserves_kind_specific_copy_selection_automation_and_visual_metrics()
    {
        await Session.Dispatch(() =>
        {
            AssertDialog(
                ZoomTargetDialogKind.Slide,
                "Insert Slide Zoom",
                "Target slide:",
                "FreeP.ZoomTarget.Slide");
            AssertDialog(
                ZoomTargetDialogKind.Section,
                "Insert Section Zoom",
                "Target section:",
                "FreeP.ZoomTarget.Section");
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Renderer_rejects_non_single_target_kinds()
    {
        await Session.Dispatch(() =>
        {
            var act = () => new SingleTargetZoomDialog(ZoomTargetDialogKind.Summary, Options);

            act.Should().Throw<ArgumentOutOfRangeException>();
        }, CancellationToken.None);
    }

    private static void AssertDialog(
        ZoomTargetDialogKind kind,
        string expectedTitle,
        string expectedLabel,
        string automationPrefix)
    {
        var dialog = new SingleTargetZoomDialog(kind, Options, selectedTargetId: "second");
        var descendants = dialog.GetLogicalDescendants().ToArray();
        var combo = descendants.OfType<ComboBox>().Single();
        var label = descendants.OfType<TextBlock>().Single(text => text.Text == expectedLabel);
        var buttons = descendants.OfType<Button>().ToArray();

        dialog.TargetKind.Should().Be(kind);
        dialog.SelectedTargetId.Should().BeNull();
        dialog.Title.Should().Be(expectedTitle);
        dialog.Width.Should().Be(420);
        dialog.Height.Should().Be(160);
        dialog.CanResize.Should().BeFalse();
        dialog.WindowStartupLocation.Should().Be(WindowStartupLocation.CenterOwner);
        AutomationProperties.GetAutomationId(dialog).Should().Be($"{automationPrefix}.Dialog");

        label.Text.Should().Be(expectedLabel);
        combo.SelectedIndex.Should().Be(1);
        combo.MinWidth.Should().Be(260);
        AutomationProperties.GetAutomationId(combo).Should().Be($"{automationPrefix}.Target");
        buttons.Select(button => button.Content).Should().Equal("OK", "Cancel");
        buttons[0].IsDefault.Should().BeTrue();
        buttons[0].IsEnabled.Should().BeTrue();
        buttons[1].IsCancel.Should().BeTrue();
    }
}
