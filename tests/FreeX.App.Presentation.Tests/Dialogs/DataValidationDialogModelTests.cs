using FluentAssertions;
using FreeX.App.Presentation.Dialogs;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Dialogs;

public sealed class DataValidationDialogModelTests
{
    [Theory]
    [InlineData(DvType.WholeNumber)]
    [InlineData(DvType.Decimal)]
    [InlineData(DvType.Date)]
    [InlineData(DvType.Time)]
    [InlineData(DvType.TextLength)]
    public void ScalarTypes_ShowOperatorAndBothFormulas(DvType type)
    {
        var model = DataValidationDialogModel.ForType(type);

        model.ShowsOperator.Should().BeTrue();
        model.HasField(DvInputField.Formula1).Should().BeTrue();
        model.HasField(DvInputField.Formula2).Should().BeTrue();
        model.HasField(DvInputField.AllowBlank).Should().BeTrue();
        model.ShowsDropdown.Should().BeFalse();
        model.Operators.Should().BeEquivalentTo(DataValidationDialogModel.AllOperators);
    }

    [Fact]
    public void Any_ShowsNoControlsBeyondNone()
    {
        var model = DataValidationDialogModel.ForType(DvType.Any);

        model.Fields.Should().BeEmpty();
        model.ShowsOperator.Should().BeFalse();
        model.Operators.Should().BeEmpty();
        model.Formula1LabelFor(DvOperator.Between).Should().Be(DvFormula1Label.None);
    }

    [Fact]
    public void List_ShowsSourceAndDropdownButNoOperator()
    {
        var model = DataValidationDialogModel.ForType(DvType.List);

        model.ShowsOperator.Should().BeFalse();
        model.Operators.Should().BeEmpty();
        model.HasField(DvInputField.Formula1).Should().BeTrue();
        model.HasField(DvInputField.Formula2).Should().BeFalse();
        model.ShowsDropdown.Should().BeTrue();
        model.ShowDropdownDefault.Should().BeTrue();
        model.Formula1LabelFor(DvOperator.Between).Should().Be(DvFormula1Label.Source);
    }

    [Fact]
    public void Custom_ShowsFormulaButNoOperatorOrDropdown()
    {
        var model = DataValidationDialogModel.ForType(DvType.Custom);

        model.ShowsOperator.Should().BeFalse();
        model.Operators.Should().BeEmpty();
        model.HasField(DvInputField.Formula1).Should().BeTrue();
        model.HasField(DvInputField.Formula2).Should().BeFalse();
        model.ShowsDropdown.Should().BeFalse();
        model.Formula1LabelFor(DvOperator.GreaterThan).Should().Be(DvFormula1Label.Formula);
    }

    [Theory]
    [InlineData(DvType.WholeNumber)]
    [InlineData(DvType.Date)]
    public void ShowDropdownDefault_IsListOnly(DvType nonListType)
    {
        DataValidationDialogModel.ForType(nonListType).ShowDropdownDefault.Should().BeFalse();
        DataValidationDialogModel.ForType(DvType.List).ShowDropdownDefault.Should().BeTrue();
    }

    [Fact]
    public void AllowBlankDefault_IsTrue()
    {
        DataValidationDialogModel.ForType(DvType.WholeNumber).AllowBlankDefault.Should().BeTrue();
    }

    [Theory]
    [InlineData(DvOperator.Between, DvFormula1Label.Minimum)]
    [InlineData(DvOperator.NotBetween, DvFormula1Label.Minimum)]
    [InlineData(DvOperator.Equal, DvFormula1Label.Value)]
    [InlineData(DvOperator.GreaterThan, DvFormula1Label.Value)]
    [InlineData(DvOperator.LessThanOrEqual, DvFormula1Label.Value)]
    public void ScalarFormula1Label_TracksOperator(DvOperator op, DvFormula1Label expected)
    {
        DataValidationDialogModel.ForType(DvType.Decimal).Formula1LabelFor(op).Should().Be(expected);
    }

    [Theory]
    [InlineData(DvOperator.Between, true)]
    [InlineData(DvOperator.NotBetween, true)]
    [InlineData(DvOperator.Equal, false)]
    [InlineData(DvOperator.GreaterThan, false)]
    public void ShowsFormula2_OnlyForBetweenOperators(DvOperator op, bool expected)
    {
        DataValidationDialogModel.ForType(DvType.WholeNumber).ShowsFormula2(op).Should().Be(expected);
    }

    [Fact]
    public void ShowsFormula2_AlwaysFalse_ForListAndCustom()
    {
        DataValidationDialogModel.ForType(DvType.List).ShowsFormula2(DvOperator.Between).Should().BeFalse();
        DataValidationDialogModel.ForType(DvType.Custom).ShowsFormula2(DvOperator.Between).Should().BeFalse();
    }

    [Theory]
    [InlineData(DvType.WholeNumber, true)]
    [InlineData(DvType.List, false)]
    [InlineData(DvType.Custom, false)]
    public void SupportsOperator_MatchesShape(DvType type, bool supports)
    {
        DataValidationDialogModel.ForType(type).SupportsOperator(DvOperator.Equal).Should().Be(supports);
    }

    // ----- Message visibility -----

    [Fact]
    public void MessageVisibility_Default_BothShownStopAlert()
    {
        var v = DvMessageVisibility.Default;

        v.ShowInputMessage.Should().BeTrue();
        v.ShowErrorMessage.Should().BeTrue();
        v.AlertStyle.Should().Be(DvAlertStyle.Stop);
        v.InputEditorsEnabled.Should().BeTrue();
        v.ErrorEditorsEnabled.Should().BeTrue();
        v.AlertStyleEnabled.Should().BeTrue();
    }

    [Fact]
    public void MessageVisibility_InputOff_DisablesInputEditorsOnly()
    {
        var v = new DvMessageVisibility(ShowInputMessage: false, ShowErrorMessage: true, DvAlertStyle.Warning);

        v.InputEditorsEnabled.Should().BeFalse();
        v.ErrorEditorsEnabled.Should().BeTrue();
        v.AlertStyleEnabled.Should().BeTrue();
    }

    [Fact]
    public void MessageVisibility_ErrorOff_DisablesErrorEditorsAndAlertStyle()
    {
        var v = new DvMessageVisibility(ShowInputMessage: true, ShowErrorMessage: false, DvAlertStyle.Information);

        v.InputEditorsEnabled.Should().BeTrue();
        v.ErrorEditorsEnabled.Should().BeFalse();
        v.AlertStyleEnabled.Should().BeFalse();
    }

    [Fact]
    public void MessageVisibility_FromRule_CopiesState()
    {
        var rule = new DataValidation
        {
            ShowInputMessage = false,
            ShowErrorMessage = true,
            AlertStyle = DvAlertStyle.Warning
        };

        var v = DvMessageVisibility.FromRule(rule);

        v.Should().Be(new DvMessageVisibility(false, true, DvAlertStyle.Warning));
    }

    // ----- Validation: valid inputs -----

    [Fact]
    public void Validate_Any_AlwaysValid_EvenWhenBlank()
    {
        var model = DataValidationDialogModel.ForType(DvType.Any);

        model.Validate(new DvCriteriaInput { Type = DvType.Any }).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(DvType.WholeNumber, "5")]
    [InlineData(DvType.WholeNumber, "=A1")]
    [InlineData(DvType.Decimal, "3.14")]
    [InlineData(DvType.Decimal, "1E+10")]
    [InlineData(DvType.Date, "2024-01-15")]
    [InlineData(DvType.Time, "13:30")]
    [InlineData(DvType.Time, "0.5")]
    [InlineData(DvType.TextLength, "10")]
    [InlineData(DvType.List, "Red,Green,Blue")]
    [InlineData(DvType.List, "=$A$1:$A$5")]
    [InlineData(DvType.Custom, "=A1>0")]
    [InlineData(DvType.Custom, "A1>0")]
    public void Validate_AcceptsWellFormedCriteria(DvType type, string formula1)
    {
        var model = DataValidationDialogModel.ForType(type);

        var result = model.Validate(new DvCriteriaInput
        {
            Type = type,
            Operator = DvOperator.GreaterThan,
            Formula1 = formula1
        });

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_Between_AcceptsBothBounds()
    {
        var model = DataValidationDialogModel.ForType(DvType.WholeNumber);

        var result = model.Validate(new DvCriteriaInput
        {
            Type = DvType.WholeNumber,
            Operator = DvOperator.Between,
            Formula1 = "1",
            Formula2 = "10"
        });

        result.IsValid.Should().BeTrue();
    }

    // ----- Validation: each error -----

    [Theory]
    [InlineData(DvType.WholeNumber, "A value is required.")]
    [InlineData(DvType.List, "A list source is required.")]
    [InlineData(DvType.Custom, "A formula is required.")]
    public void Validate_BlankFormula1_FailsWithTypeSpecificMessage(DvType type, string message)
    {
        var model = DataValidationDialogModel.ForType(type);

        var result = model.Validate(new DvCriteriaInput { Type = type, Formula1 = "   " });

        result.IsValid.Should().BeFalse();
        result.FirstError!.Target.Should().Be(DvValidationTarget.Formula1);
        result.FirstError!.Message.Should().Be(message);
    }

    [Fact]
    public void Validate_Between_MissingSecond_FailsOnFormula2()
    {
        var model = DataValidationDialogModel.ForType(DvType.Decimal);

        var result = model.Validate(new DvCriteriaInput
        {
            Type = DvType.Decimal,
            Operator = DvOperator.Between,
            Formula1 = "1",
            Formula2 = ""
        });

        result.IsValid.Should().BeFalse();
        result.FirstError!.Target.Should().Be(DvValidationTarget.Formula2);
    }

    [Theory]
    [InlineData(DvType.WholeNumber, "1.5")]
    [InlineData(DvType.WholeNumber, "abc")]
    [InlineData(DvType.Decimal, "notnum")]
    [InlineData(DvType.Date, "not-a-date")]
    [InlineData(DvType.Time, "99:99")]
    [InlineData(DvType.TextLength, "-1")]
    [InlineData(DvType.Custom, "=A1>")]
    public void Validate_MalformedFormula1_FailsOnFormula1(DvType type, string formula1)
    {
        var model = DataValidationDialogModel.ForType(type);

        var result = model.Validate(new DvCriteriaInput
        {
            Type = type,
            Operator = DvOperator.GreaterThan,
            Formula1 = formula1
        });

        result.IsValid.Should().BeFalse();
        result.FirstError!.Target.Should().Be(DvValidationTarget.Formula1);
    }

    [Fact]
    public void Validate_Between_MalformedSecond_FailsOnFormula2()
    {
        var model = DataValidationDialogModel.ForType(DvType.WholeNumber);

        var result = model.Validate(new DvCriteriaInput
        {
            Type = DvType.WholeNumber,
            Operator = DvOperator.Between,
            Formula1 = "1",
            Formula2 = "notnum"
        });

        result.IsValid.Should().BeFalse();
        result.FirstError!.Target.Should().Be(DvValidationTarget.Formula2);
    }

    // ----- Culture-sensitive number parsing (R9 regression) -----

    [Fact]
    public void Validate_DeDE_CommaDecimal_ParsesAsDecimalNotThousandsSeparated()
    {
        // In de-DE, "3,5" is 3.5 (decimal comma). With NumberStyles.Any + InvariantCulture the comma
        // was treated as a thousands separator, silently accepting 35 as a whole number.
        using var scope = TestCultureScope.CurrentCulture("de-DE");

        var model = DataValidationDialogModel.ForType(DvType.Decimal);
        var result = model.Validate(new DvCriteriaInput
        {
            Type = DvType.Decimal,
            Operator = DvOperator.GreaterThan,
            Formula1 = "3,5"
        });

        result.IsValid.Should().BeTrue("\"3,5\" is a valid decimal in de-DE (= 3.5)");
    }

    [Fact]
    public void Validate_DeDE_CommaDecimal_IsNotClassifiedAsWholeNumber()
    {
        // "3,5" in de-DE is 3.5 — a non-integer — so WholeNumber validation must reject it.
        // The bug would have silently accepted it (as 35, a whole number).
        using var scope = TestCultureScope.CurrentCulture("de-DE");

        var model = DataValidationDialogModel.ForType(DvType.WholeNumber);
        var result = model.Validate(new DvCriteriaInput
        {
            Type = DvType.WholeNumber,
            Operator = DvOperator.GreaterThan,
            Formula1 = "3,5"
        });

        result.IsValid.Should().BeFalse("\"3,5\" in de-DE is 3.5, which is not a whole number");
        result.FirstError!.Target.Should().Be(DvValidationTarget.Formula1);
    }

    [Fact]
    public void Validate_EnglishDotDecimal_StillParsesCorrectly()
    {
        // Regression guard: English-format "3.5" must still parse as 3.5 (InvariantCulture fallback).
        var model = DataValidationDialogModel.ForType(DvType.Decimal);
        var result = model.Validate(new DvCriteriaInput
        {
            Type = DvType.Decimal,
            Operator = DvOperator.GreaterThan,
            Formula1 = "3.5"
        });

        result.IsValid.Should().BeTrue("\"3.5\" is always a valid decimal via InvariantCulture fallback");
    }

    [Fact]
    public void Validate_ListFormulaRangeOver10000Cells_IsAccepted()
    {
        // Excel places no upper bound on the size of a range referenced as a List validation
        // source (a full-column reference is a legal source); a prior arbitrary 10,000-cell cap
        // here rejected this ordinary same-sheet column reference (R29-dialogs-validation-logic-2).
        var model = DataValidationDialogModel.ForType(DvType.List);

        var result = model.Validate(new DvCriteriaInput
        {
            Type = DvType.List,
            Formula1 = "=$A$1:$A$10001"
        });

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("Any", DvType.Any)]
    [InlineData("WholeNumber", DvType.WholeNumber)]
    [InlineData("Decimal", DvType.Decimal)]
    [InlineData("List", DvType.List)]
    [InlineData("Date", DvType.Date)]
    [InlineData("Time", DvType.Time)]
    [InlineData("TextLength", DvType.TextLength)]
    [InlineData("Custom", DvType.Custom)]
    public void Planner_MapsTypeTagsRoundTrip(string tag, DvType type)
    {
        DataValidationDialogPlanner.TypeFromTag(tag).Should().Be(type);
        DataValidationDialogPlanner.TypeTag(type).Should().Be(tag);
    }

    [Theory]
    [InlineData("Between", DvOperator.Between)]
    [InlineData("NotBetween", DvOperator.NotBetween)]
    [InlineData("Equal", DvOperator.Equal)]
    [InlineData("NotEqual", DvOperator.NotEqual)]
    [InlineData("GreaterThan", DvOperator.GreaterThan)]
    [InlineData("LessThan", DvOperator.LessThan)]
    [InlineData("GreaterThanOrEqual", DvOperator.GreaterThanOrEqual)]
    [InlineData("LessThanOrEqual", DvOperator.LessThanOrEqual)]
    public void Planner_MapsOperatorTagsRoundTrip(string tag, DvOperator op)
    {
        DataValidationDialogPlanner.OperatorFromTag(tag).Should().Be(op);
        DataValidationDialogPlanner.OperatorTag(op).Should().Be(tag);
    }

    [Fact]
    public void Planner_MapsAlertStyleTagsRoundTrip()
    {
        DataValidationDialogPlanner.AlertStyleFromTag("Stop").Should().Be(DvAlertStyle.Stop);
        DataValidationDialogPlanner.AlertStyleFromTag("Warning").Should().Be(DvAlertStyle.Warning);
        DataValidationDialogPlanner.AlertStyleFromTag("Information").Should().Be(DvAlertStyle.Information);
        DataValidationDialogPlanner.AlertStyleTag(DvAlertStyle.Warning).Should().Be("Warning");
        DataValidationDialogPlanner.AlertStyleTag(DvAlertStyle.Information).Should().Be("Information");
        DataValidationDialogPlanner.AlertStyleTag(DvAlertStyle.Stop).Should().Be("Stop");
    }

    [Fact]
    public void Planner_CreateVisibilityPlan_TracksRuleTypeOperatorAndSelection()
    {
        var between = DataValidationDialogPlanner.CreateVisibilityPlan(
            DvType.WholeNumber,
            DvOperator.Between,
            hasSelectionSource: true);

        between.ShowOperator.Should().BeTrue();
        between.Formula1Label.Should().Be(DvFormula1Label.Minimum);
        between.ShowFormula1.Should().BeTrue();
        between.ShowFormula2.Should().BeTrue();
        between.ShowFormula1UseSelection.Should().BeTrue();
        between.ShowFormula2UseSelection.Should().BeTrue();
        between.ShowDropdown.Should().BeFalse();

        var equal = DataValidationDialogPlanner.CreateVisibilityPlan(
            DvType.WholeNumber,
            DvOperator.Equal,
            hasSelectionSource: true);

        equal.Formula1Label.Should().Be(DvFormula1Label.Value);
        equal.ShowFormula2.Should().BeFalse();
        equal.ShowFormula2RangePicker.Should().BeFalse();

        var any = DataValidationDialogPlanner.CreateVisibilityPlan(
            DvType.Any,
            DvOperator.Between,
            hasSelectionSource: true);

        any.ShowFormula1.Should().BeFalse();
        any.ShowFormula1RangePicker.Should().BeFalse();
        any.ShowFormula1UseSelection.Should().BeFalse();
    }

    [Fact]
    public void Planner_CreateRule_NormalizesHiddenFieldsAndTrimsMessages()
    {
        var id = Guid.NewGuid();

        var rule = DataValidationDialogPlanner.CreateRule(new DataValidationRuleEditorInput
        {
            Id = id,
            Type = DvType.List,
            Operator = DvOperator.Between,
            AlertStyle = DvAlertStyle.Warning,
            Formula1 = "  Red,Blue  ",
            Formula2 = "  hidden  ",
            AllowBlank = false,
            ShowDropdown = true,
            ShowInputMessage = false,
            ShowErrorMessage = true,
            PromptTitle = "  Pick  ",
            PromptMessage = "  Choose one  ",
            ErrorTitle = "  Bad  ",
            ErrorMessage = "  Not allowed  "
        });

        rule.Id.Should().Be(id);
        rule.Type.Should().Be(DvType.List);
        rule.Formula1.Should().Be("Red,Blue");
        rule.Formula2.Should().BeEmpty();
        rule.ShowDropdown.Should().BeTrue();
        rule.AlertStyle.Should().Be(DvAlertStyle.Warning);
        rule.PromptTitle.Should().Be("Pick");
        rule.ErrorMessage.Should().Be("Not allowed");

        var any = DataValidationDialogPlanner.CreateRule(new DataValidationRuleEditorInput
        {
            Type = DvType.Any,
            Formula1 = "A1",
            Formula2 = "B1",
            ShowDropdown = true
        });

        any.Formula1.Should().BeEmpty();
        any.Formula2.Should().BeEmpty();
        any.ShowDropdown.Should().BeFalse();
    }

    [Theory]
    [InlineData(DvType.WholeNumber, DvOperator.Between, "1", "100", false)]
    [InlineData(DvType.Decimal, DvOperator.Between, "0", "100", false)]
    [InlineData(DvType.List, DvOperator.Between, "Yes,No", "", true)]
    [InlineData(DvType.Date, DvOperator.Between, "2024-01-01", "2024-12-31", false)]
    [InlineData(DvType.Time, DvOperator.Between, "09:00", "17:00", false)]
    [InlineData(DvType.TextLength, DvOperator.LessThanOrEqual, "50", "", false)]
    [InlineData(DvType.Custom, DvOperator.Between, "=A1>0", "", false)]
    [InlineData(DvType.Any, DvOperator.Between, "", "", false)]
    public void Planner_CreateDefaultRule_SeedsRuleEditorDefaults(
        DvType type,
        DvOperator expectedOperator,
        string expectedFormula1,
        string expectedFormula2,
        bool expectedDropdown)
    {
        var sheetId = new SheetId(Guid.NewGuid());
        var range = new GridRange(
            new CellAddress(sheetId, 1, 1),
            new CellAddress(sheetId, 2, 2));

        var rule = DataValidationDialogPlanner.CreateDefaultRule(type, range);

        rule.AppliesTo.Should().Be(range);
        rule.Type.Should().Be(type);
        rule.Operator.Should().Be(expectedOperator);
        rule.Formula1.Should().Be(expectedFormula1);
        rule.Formula2.Should().Be(expectedFormula2);
        rule.AllowBlank.Should().BeTrue();
        rule.ShowDropdown.Should().Be(expectedDropdown);
        rule.AlertStyle.Should().Be(DvAlertStyle.Stop);
        rule.ShowInputMessage.Should().BeTrue();
        rule.ShowErrorMessage.Should().BeTrue();
    }

    [Fact]
    public void Planner_FocusTargetForInvalidCriteria_PrefersSecondWhenFirstIsValid()
    {
        DataValidationDialogPlanner.FocusTargetForInvalidCriteria(
                DvType.WholeNumber,
                DvOperator.Between,
                "1",
                "two")
            .Should()
            .Be(DvRuleEditorFocusTarget.Formula2);

        DataValidationDialogPlanner.FocusTargetForInvalidCriteria(
                DvType.WholeNumber,
                DvOperator.Between,
                "one",
                "two")
            .Should()
            .Be(DvRuleEditorFocusTarget.Formula1);
    }

    [Fact]
    public void Planner_CreateRangeSelectionRequest_TrimsTextAndKeepsDialogOpenByDefault()
    {
        DataValidationDialogPlanner.CreateRangeSelectionRequest(
                DataValidationRangeSelectionTarget.Formula2,
                "  =Sheet1!$C$2:$C$8  ")
            .Should()
            .Be(new DataValidationRangeSelectionRequest(
                DataValidationRangeSelectionTarget.Formula2,
                "=Sheet1!$C$2:$C$8",
                CollapseDialog: false));
    }

    [Fact]
    public void Planner_IsClearAllState_RequiresDefaultRawEditorState()
    {
        var clearState = new DataValidationRuleEditorInput
        {
            Type = DvType.Any,
            Operator = DvOperator.Between,
            AlertStyle = DvAlertStyle.Stop,
            AllowBlank = true,
            ShowDropdown = true,
            ShowInputMessage = true,
            ShowErrorMessage = true
        };

        DataValidationDialogPlanner.IsClearAllState(clearState).Should().BeTrue();
        DataValidationDialogPlanner.IsClearAllState(clearState with { ApplyToSameSettings = true }).Should().BeFalse();
        DataValidationDialogPlanner.IsClearAllState(clearState with { Formula1 = "A1" }).Should().BeFalse();
    }
}
