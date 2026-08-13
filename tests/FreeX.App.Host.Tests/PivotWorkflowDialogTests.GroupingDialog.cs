using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class PivotWorkflowDialogTests
{
    [Fact]
    public void PivotFieldGroupingDialog_ExposesExcelLikeGroupingSections()
    {
        var source = ReadPivotWorkflowSource();

        source.Should().Contain("UiText.Get(\"PivotFieldGrouping_SelectionGroup\")");
        source.Should().Contain("UiText.Get(\"PivotFieldGrouping_GroupByGroup\")");
        source.Should().Contain("UiText.Get(\"PivotFieldGrouping_RangeGroup\")");
        source.Should().NotContain("Select the PivotTable field and grouping interval");
    }

    [Fact]
    public void PivotFieldGroupingDialogOpenedFromKeyboard_FocusesFieldBox()
    {
        var source = ReadClassSource(
            "PivotFieldGroupingDialog.cs",
            "public sealed class PivotFieldGroupingDialog",
            "");

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("_fieldBox.Focus();");
        source.Should().Contain("Keyboard.Focus(_fieldBox);");
    }

    [Fact]
    public void PivotFieldGroupingDialogInvalidNumberRangeIntervals_ShowOwnedWarningAndRefocusByBox()
    {
        var source = ReadClassSource(
            "PivotFieldGroupingDialog.cs",
            "public sealed class PivotFieldGroupingDialog",
            "");

        source.Should().Contain("PivotGroupFieldPlanner.TryCreateSubmission(");
        source.Should().Contain("PivotGroupFieldPlanner.InvalidIntervalMessage");
        source.Should().Contain("UiText.Get(\"PivotFieldGrouping_EnterPositiveGroupingInterval\")");
        source.Should().Contain("DialogFocus.ShowWarningAndFocus(this, message, Title, target);");
        source.Should().Contain("private bool ShowInvalidInputWarning(string message, TextBox target)");
    }

    [Fact]
    public void PivotFieldGroupingDialogInvalidBounds_ShowOwnedWarningAndRefocusBadInput()
    {
        var source = ReadClassSource(
            "PivotFieldGroupingDialog.cs",
            "public sealed class PivotFieldGroupingDialog",
            "");

        source.Should().Contain("PivotGroupFieldPlanner.InvalidEndMessage");
        source.Should().Contain("UiText.Get(\"PivotFieldGrouping_EnterValidStartingValue\")");
        source.Should().Contain("UiText.Get(\"PivotFieldGrouping_EnterValidEndingValue\")");
        source.Should().Contain("_ => (UiText.Get(\"PivotFieldGrouping_EnterValidStartingValue\"), _startBox)");
        source.Should().NotContain("NumericInputParser.TryParseFiniteDouble");
    }
}
