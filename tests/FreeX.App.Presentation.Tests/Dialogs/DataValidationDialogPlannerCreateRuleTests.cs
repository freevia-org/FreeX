using FluentAssertions;
using FreeX.App.Presentation.Dialogs;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Dialogs;

/// <summary>
/// Covers DataValidationDialogPlanner.CreateRule's IsX14 promotion for freshly authored List
/// rules. Excel keeps the legacy &lt;dataValidation&gt;&lt;formula1&gt; slot inert for List
/// sources that reference another sheet (or are too long), routing the real formula through the
/// worksheet x14 extLst extension instead — see DataValidation.IsX14's doc comment.
/// </summary>
public sealed class DataValidationDialogPlannerCreateRuleTests
{
    private static DataValidationRuleEditorInput NewListInput(string formula1, bool isX14 = false) =>
        new()
        {
            Type = DvType.List,
            Formula1 = formula1,
            IsX14 = isX14
        };

    [Fact]
    public void List_NewCrossSheetFormulaSource_IsPromotedToX14()
    {
        var input = NewListInput("=Sheet2!$A$1:$A$5");

        var rule = DataValidationDialogPlanner.CreateRule(input);

        rule.IsX14.Should().BeTrue();
        rule.Formula1.Should().Be("=Sheet2!$A$1:$A$5");
    }

    [Fact]
    public void List_NewFormulaSourceLongerThan255Chars_IsPromotedToX14()
    {
        // Not a cross-sheet reference, but too long for the legacy 255-char formula1 slot.
        var longList = string.Join(",", Enumerable.Range(1, 60).Select(i => $"Item{i:D3}"));
        longList.Length.Should().BeGreaterThan(255);
        var input = NewListInput(longList);

        var rule = DataValidationDialogPlanner.CreateRule(input);

        rule.IsX14.Should().BeTrue();
    }

    [Fact]
    public void List_NewInlineShortSource_IsNotPromotedToX14()
    {
        // Sibling case: a plain literal item list must keep working exactly as before.
        var input = NewListInput("Yes,No");

        var rule = DataValidationDialogPlanner.CreateRule(input);

        rule.IsX14.Should().BeFalse();
        rule.Formula1.Should().Be("Yes,No");
    }

    [Fact]
    public void List_NewSameSheetFormulaSource_IsNotPromotedToX14()
    {
        // Sibling case: a same-sheet range reference has no '!' and fits the legacy element fine.
        var input = NewListInput("=$A$1:$A$5");

        var rule = DataValidationDialogPlanner.CreateRule(input);

        rule.IsX14.Should().BeFalse();
    }

    [Fact]
    public void List_ExistingX14RuleEditedToSameSheetFormula_IsNotDowngraded()
    {
        // Sibling case (already-guarded edit path): once a rule is carried over as x14 it must
        // never be silently downgraded back to the legacy slot, even if the edited formula no
        // longer references another sheet.
        var input = NewListInput("=$A$1:$A$5", isX14: true);

        var rule = DataValidationDialogPlanner.CreateRule(input);

        rule.IsX14.Should().BeTrue();
    }

    [Fact]
    public void CustomType_CrossSheetFormula_IsNotPromotedToX14()
    {
        // Only the List source-formula slot is the one Excel keeps inert cross-sheet; other DV
        // types' classic formula1 element evaluates cross-sheet formulas just fine.
        var input = new DataValidationRuleEditorInput
        {
            Type = DvType.Custom,
            Formula1 = "=Sheet2!A1>0"
        };

        var rule = DataValidationDialogPlanner.CreateRule(input);

        rule.IsX14.Should().BeFalse();
    }
}
