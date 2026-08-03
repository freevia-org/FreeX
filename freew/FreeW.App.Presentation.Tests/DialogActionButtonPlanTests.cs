using FreeW.App.Presentation.Dialogs;
using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Presentation.Tests;

public sealed class DialogActionButtonPlanTests
{
    [Fact]
    public void Watermark_cross_reference_and_inspector_routes_share_explicit_action_semantics()
    {
        AssertPlan(
            WatermarkOptionsDialogPlanner.ActionButtons,
            ("OK", true, false),
            ("Remove Watermark", false, false),
            ("Cancel", false, true));

        AssertPlan(
            CrossReferenceDialogPlanner.ActionButtons,
            ("OK", true, false),
            ("Cancel", false, true));

        AssertPlan(
            DocumentInspectorDialogPlanner.ActionButtons,
            ("Remove Selected", true, false),
            ("Close", false, true));
    }

    private static void AssertPlan(
        IReadOnlyList<DialogActionButtonPlan> actual,
        params (string Label, bool IsDefault, bool IsCancel)[] expected)
    {
        actual.Select(button => (button.Label, button.IsDefault, button.IsCancel))
            .Should().Equal(expected);
        actual.Count(button => button.IsDefault).Should().Be(1);
        actual.Count(button => button.IsCancel).Should().Be(1);
    }
}
