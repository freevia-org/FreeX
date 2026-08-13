using Free.Shared.Shell;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Presentation.Tests;

public sealed class FreeWDialogFocusPlannerTests
{
    [Fact]
    public void Route_plans_share_WPF_authority_keyboard_contract()
    {
        var plans = new[]
        {
            FreeWDialogFocusPlanner.CompareDocuments,
            FreeWDialogFocusPlanner.Properties,
            FreeWDialogFocusPlanner.TableFormula,
            FreeWDialogFocusPlanner.Zoom,
        };

        plans.Should().AllBeAssignableTo<DialogFocusPlan<string>>();

        plans.Should().OnlyContain(plan =>
            plan.InitialFocusTarget == plan.ValidationFocusTarget
            && plan.SelectAllOnFocus
            && plan.ActionButtons.Select(button => button.Label).SequenceEqual(new[] { "OK", "Cancel" })
            && plan.ActionButtons[0].IsDefault
            && plan.ActionButtons[1].IsCancel);

        plans.Select(plan => plan.InitialFocusTarget).Should().Equal(
            "CompareDocumentsAuthorBox",
            "DocumentPropertiesTitle",
            "TableFormulaFormulaBox",
            "ZoomCustomPercentBox");
    }
}
