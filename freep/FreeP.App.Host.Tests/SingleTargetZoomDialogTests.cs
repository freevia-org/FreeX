using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using FreeP.App.Compositor;

namespace FreeP.App.Host.Tests;

public sealed class SingleTargetZoomDialogTests
{
    private static readonly IReadOnlyList<(string Id, string DisplayName)> Options =
    [
        ("first", "First target"),
        ("second", "Second target"),
    ];

    [StaFact]
    public void Renderer_preserves_kind_specific_copy_selection_automation_and_visual_metrics()
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
    }

    [StaFact]
    public void Renderer_rejects_non_single_target_kinds()
    {
        var act = () => new SingleTargetZoomDialog(ZoomTargetDialogKind.Summary, Options);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static void AssertDialog(
        ZoomTargetDialogKind kind,
        string expectedTitle,
        string expectedLabel,
        string automationPrefix)
    {
        var dialog = new SingleTargetZoomDialog(kind, Options, selectedTargetId: "second");
        var grid = dialog.Content.Should().BeOfType<Grid>().Subject;
        var combo = grid.Children.OfType<ComboBox>().Single();
        var label = grid.Children.OfType<Label>().Single();
        var buttons = grid.Children.OfType<StackPanel>().Single().Children.OfType<Button>().ToArray();

        dialog.TargetKind.Should().Be(kind);
        dialog.SelectedTargetId.Should().BeNull();
        dialog.Title.Should().Be(expectedTitle);
        dialog.Width.Should().Be(420);
        dialog.SizeToContent.Should().Be(SizeToContent.Height);
        dialog.ResizeMode.Should().Be(ResizeMode.NoResize);
        dialog.WindowStartupLocation.Should().Be(WindowStartupLocation.CenterOwner);
        AutomationProperties.GetAutomationId(dialog).Should().Be($"{automationPrefix}.Dialog");

        label.Content.Should().Be(expectedLabel);
        combo.SelectedIndex.Should().Be(1);
        combo.MinWidth.Should().Be(260);
        AutomationProperties.GetAutomationId(combo).Should().Be($"{automationPrefix}.Target");
        buttons.Select(button => button.Content).Should().Equal("OK", "Cancel");
        buttons[0].IsDefault.Should().BeTrue();
        buttons[0].IsEnabled.Should().BeTrue();
        buttons[1].IsCancel.Should().BeTrue();
    }
}
