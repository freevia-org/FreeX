using FluentAssertions;
using FreeX.App.Presentation.FillSeries;

namespace FreeX.App.Host.Tests;

public sealed partial class RemainingDialogTests
{
    [Fact]
    public void FillSeriesStepDialog_TryCreateResult_AcceptsNegativeStep()
    {
        FillSeriesStepDialog.TryCreateResult("-2", out var result, out _).Should().BeTrue();

        result.Should().Be(new FillSeriesOptions(-2));
    }

    [Fact]
    public void FillSeriesStepDialog_CreateResult_CapturesExcelSeriesOptions()
    {
        var source = ReadRemainingDialogSources();

        source.Should().Contain("using FreeX.App.Presentation.FillSeries;");
        source.Should().Contain("FillSeriesOptions");
        source.Should().Contain("FillSeriesDirection.Rows");
        source.Should().Contain("FillSeriesType.Date");
        source.Should().Contain("FillSeriesDateUnit.Month");
        source.Should().Contain("StopValue");
        source.Should().Contain("FillSeriesPlanner.DefaultOptions");
        source.Should().Contain("FillSeriesPlanner.CreateDefaultOptions");
        source.Should().NotContain("enum FillSeriesDirection");
        source.Should().NotContain("enum FillSeriesType");
        source.Should().NotContain("enum FillSeriesDateUnit");
        source.Should().NotContain("FillSeriesStepDialogResult");
    }

    [Fact]
    public void FillSeriesStepDialog_FieldLabelsUseUniqueAccessKeys()
    {
        var labelKeys = new[]
        {
            "FillSeriesStep_Rows",
            "FillSeriesStep_Columns",
            "FillSeriesStep_Linear",
            "FillSeriesStep_Growth",
            "FillSeriesStep_Date",
            "FillSeriesStep_AutoFill",
            "FillSeriesStep_Day",
            "FillSeriesStep_Weekday",
            "FillSeriesStep_Month",
            "FillSeriesStep_Year",
            "FillSeriesStep_StepValueLabel",
            "FillSeriesStep_StopValueLabel",
            "FillSeriesStep_Trend"
        };
        var labels = labelKeys.Select(UiText.Get).ToArray();

        labels.Select(GetAccessKey).Should().OnlyHaveUniqueItems();

        var source = ReadClassSource("FillSeriesStepDialog.cs", "public sealed class FillSeriesStepDialog", "public sealed record __NoNextFillSeriesStepDialog");
        foreach (var key in labelKeys)
            source.Should().Contain($"UiText.Get(\"{key}\")");
    }

    [Fact]
    public void FillSeriesStepDialog_InputFieldsExposeAutomationNames()
    {
        var source = ReadClassSource("FillSeriesStepDialog.cs", "public sealed class FillSeriesStepDialog", "public sealed record __NoNextFillSeriesStepDialog");

        source.Should().Contain("AutomationProperties.SetName(_stepBox, UiText.Get(\"FillSeriesStep_StepValueAutomationName\"));");
        source.Should().Contain("AutomationProperties.SetAutomationId(_stepBox, \"FillSeriesStepValueBox\");");
        source.Should().Contain("AutomationProperties.SetHelpText(_stepBox, UiText.Get(\"FillSeriesStep_StepValueHelpText\"));");
        source.Should().Contain("AutomationProperties.SetName(_stopBox, UiText.Get(\"FillSeriesStep_StopValueAutomationName\"));");
        source.Should().Contain("AutomationProperties.SetAutomationId(_stopBox, \"FillSeriesStopValueBox\");");
        source.Should().Contain("AutomationProperties.SetHelpText(_stopBox, UiText.Get(\"FillSeriesStep_StopValueHelpText\"));");
    }

    [Fact]
    public void FillSeriesStepDialog_ActionButtonsAreSeparatedFromStopValueField()
    {
        var source = ReadClassSource("FillSeriesStepDialog.cs", "public sealed class FillSeriesStepDialog", "public sealed record __NoNextFillSeriesStepDialog");

        source.Should().Contain("Height = 386;");
        source.Should().Contain("DialogButtonRowFactory.Create(Accept, 72, rowMargin: new Thickness(0, 16, 0, 0))");
    }

    [Fact]
    public void FillSeriesStepDialogOpenedFromKeyboard_FocusesSelectedSeriesDirection()
    {
        var source = ReadRemainingDialogSources();

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("_columnsButton.Focus();");
        source.Should().Contain("Keyboard.Focus(_columnsButton);");
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    public void FillSeriesStepDialog_TryCreateResult_RejectsNonFiniteSteps(string input)
    {
        FillSeriesStepDialog.TryCreateResult(input, out _, out var error).Should().BeFalse();

        error.Should().Contain("numeric");
    }

    [Fact]
    public void FillSeriesStepDialogInvalidStep_ShowsOwnedWarningAndRefocusesInput()
    {
        var source = ReadClassSource("FillSeriesStepDialog.cs", "public sealed class FillSeriesStepDialog", "public sealed record __NoNextFillSeriesStepDialog");

        source.Should().Contain("DialogFocus.ShowWarningAndFocus(this,");
        source.Should().Contain("error ?? UiText.Get(\"FillSeriesStep_InvalidStepMessage\")");
        source.Should().Contain("ResolveInvalidInput(inputError)");
        source.Should().Contain("private TextBox ResolveInvalidInput(FillSeriesInputError inputError)");
    }

    [Fact]
    public void FillSeriesStepDialog_TryCreateResult_RejectsInvalidNonBlankStopValue()
    {
        FillSeriesStepDialog.TryCreateResult(
                FillSeriesDirection.Columns,
                FillSeriesType.Linear,
                FillSeriesDateUnit.Day,
                "1",
                "not-a-number",
                out _,
                out var error)
            .Should()
            .BeFalse();

        error.Should().Contain("stop");
    }

    [Fact]
    public void FillSeriesStepDialogInvalidStop_ShowsOwnedWarningAndRefocusesStopInput()
    {
        var source = ReadClassSource("FillSeriesStepDialog.cs", "public sealed class FillSeriesStepDialog", "public sealed record __NoNextFillSeriesStepDialog");

        source.Should().Contain("UiText.Get(\"FillSeriesStep_InvalidStopMessage\")");
        source.Should().Contain("FillSeriesPlanner.FocusTargetFor(inputError) == FillSeriesInputFocusTarget.StopValue");
        source.Should().NotContain("string.Equals(error, UiText.Get(\"FillSeriesStep_InvalidStopMessage\")");
    }

    [Fact]
    public void FillSeriesStepDialog_DisablesDateUnitsUntilDateTypeSelected()
    {
        var source = ReadClassSource("FillSeriesStepDialog.cs", "public sealed class FillSeriesStepDialog", "public sealed record __NoNextFillSeriesStepDialog");

        source.Should().Contain("_linearButton.Checked += (_, _) => UpdateDateUnitAvailability();");
        source.Should().Contain("_growthButton.Checked += (_, _) => UpdateDateUnitAvailability();");
        source.Should().Contain("_dateButton.Checked += (_, _) => UpdateDateUnitAvailability();");
        source.Should().Contain("_autoFillButton.Checked += (_, _) => UpdateDateUnitAvailability();");
        source.Should().Contain("private void UpdateDateUnitAvailability()");
        source.Should().Contain("var isDateSeries = FillSeriesPlanner.IsDateUnitEnabled(SelectedSeriesType());");
        foreach (var button in new[] { "_dayButton", "_weekdayButton", "_monthButton", "_yearButton" })
            source.Should().Contain($"{button}.IsEnabled = isDateSeries;");
    }
}
