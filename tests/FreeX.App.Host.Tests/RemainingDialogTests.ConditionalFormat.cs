using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class RemainingDialogTests
{
    [Fact]
    public void ConditionalFormatThresholdDialog_CreateResult_TrimsThresholdText()
    {
        ConditionalFormatThresholdDialog.CreateResult("  100  ")
            .Should()
            .Be(new ConditionalFormatThresholdDialogResult("100"));
    }

    [Fact]
    public void ConditionalFormatThresholdDialog_TryCreateResult_RejectsBlankThreshold()
    {
        ConditionalFormatThresholdDialog.TryCreateResult(" ", out _, out var error)
            .Should()
            .BeFalse();

        error.Should().Be("Enter a threshold value.");
    }

    [Fact]
    public void ConditionalFormatThresholdDialog_TryCreateResult_AcceptsTrimmedThreshold()
    {
        ConditionalFormatThresholdDialog.TryCreateResult("  100  ", out var result, out var error)
            .Should()
            .BeTrue(error);

        result.Should().Be(new ConditionalFormatThresholdDialogResult("100"));
    }

    [Fact]
    public void ConditionalFormatThresholdDialog_AcceptWarnsAndRefocusesBlankThreshold()
    {
        var source = ReadClassSource("RemainingDialogs.cs", "public sealed class ConditionalFormatThresholdDialog", "public sealed record RowHeightDialogResult");

        source.Should().Contain("if (!TryCreateResult(_thresholdBox.Text, out var result, out var error))");
        source.Should().Contain("ShowInvalidInputWarning(error ?? UiText.Get(\"Remaining_EnterThresholdValue\"));");
        source.Should().Contain("DialogMessageHelper.ShowWarning(this, message, Title);");
        source.Should().Contain("_thresholdBox.Focus();");
        source.Should().Contain("_thresholdBox.SelectAll();");
        source.Should().Contain("Keyboard.Focus(_thresholdBox);");
    }

    [Fact]
    public void ConditionalFormatThresholdDialogOpenedFromKeyboard_FocusesThresholdBox()
    {
        var source = ReadClassSource("RemainingDialogs.cs", "public sealed class ConditionalFormatThresholdDialog", "public sealed record RowHeightDialogResult");

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("DialogFocus.FocusAndSelect(_thresholdBox);");
    }

    [Fact]
    public void ConditionalFormatThresholdDialog_FieldExposesAutomationMetadata()
    {
        var source = ReadClassSource("RemainingDialogs.cs", "public sealed class ConditionalFormatThresholdDialog", "public sealed record RowHeightDialogResult");

        source.Should().Contain("AutomationProperties.SetName(_thresholdBox, UiText.Get(\"Remaining_ConditionalFormatThreshold\"));");
        source.Should().Contain("AutomationProperties.SetAutomationId(_thresholdBox, \"ConditionalFormatThresholdBox\");");
        source.Should().Contain("AutomationProperties.SetHelpText(_thresholdBox, UiText.Get(\"Remaining_EnterTheValueForTheConditionalFormattingRuleThreshold\"));");
    }
}
