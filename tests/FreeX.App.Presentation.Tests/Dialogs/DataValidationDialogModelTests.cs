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
}
