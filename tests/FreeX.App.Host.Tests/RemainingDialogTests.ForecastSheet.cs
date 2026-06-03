using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class RemainingDialogTests
{
    [Fact]
    public void ForecastSheetDialog_TryCreateResult_RequiresPositivePeriods()
    {
        ForecastSheetDialog.TryCreateResult("0", out _, out var error).Should().BeFalse();

        error.Should().Contain("positive");
    }

    [Fact]
    public void ForecastSheetDialog_TryCreateResult_AcceptsPositivePeriods()
    {
        ForecastSheetDialog.TryCreateResult("12", out var result, out var error).Should().BeTrue(error);

        result.Should().Be(new ForecastSheetDialogResult(12));
    }

    [Fact]
    public void ForecastSheetDialogOpenedFromKeyboard_FocusesPeriodsBox()
    {
        var source = ReadClassSource("ForecastSheetDialog.cs", "public sealed class ForecastSheetDialog", "public sealed record __NoNextForecastSheetDialog");

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("DialogFocus.FocusAndSelect(_periodsBox);");
    }

    [Fact]
    public void ForecastSheetDialog_PeriodsBoxExposesAutomationMetadata()
    {
        var source = ReadClassSource("ForecastSheetDialog.cs", "public sealed class ForecastSheetDialog", "public sealed record __NoNextForecastSheetDialog");

        source.Should().Contain("AutomationProperties.SetName(_periodsBox, UiText.Get(\"ForecastSheet_PeriodsAutomationName\"));");
        source.Should().Contain("AutomationProperties.SetAutomationId(_periodsBox, \"ForecastPeriodsBox\");");
        source.Should().Contain("AutomationProperties.SetHelpText(_periodsBox, UiText.Get(\"ForecastSheet_PeriodsHelpText\"));");
    }

    [Fact]
    public void ForecastSheetDialog_UsesExcelLikeCreateDefaultAction()
    {
        var source = ReadClassSource("ForecastSheetDialog.cs", "public sealed class ForecastSheetDialog", "public sealed record __NoNextForecastSheetDialog");
        var helperSource = ReadClassSource("ObjectSizingDialogs.cs", "public sealed class ObjectSizeDialog", "public sealed class ObjectRotationDialog");

        source.Should().Contain("ObjectSizeDialog.CreateSingleInputContent(");
        source.Should().Contain("UiText.Get(\"ForecastSheet_PeriodsLabel\")");
        source.Should().Contain("acceptContent: UiText.Get(\"ForecastSheet_CreateButton\")");
        helperSource.Should().Contain("string? acceptContent = null");
        helperSource.Should().Contain("DialogButtonRowFactory.Create(accept, 72, acceptContent: acceptContent ?? UiText.Ok)");
    }

    [Fact]
    public void ForecastSheetDialogInvalidPeriods_ShowsOwnedWarningAndRefocusesInput()
    {
        var source = ReadClassSource("ForecastSheetDialog.cs", "public sealed class ForecastSheetDialog", "public sealed record __NoNextForecastSheetDialog");

        source.Should().Contain("DialogMessageHelper.ShowWarning(this,");
        source.Should().Contain("error ?? UiText.Get(\"ForecastSheet_InvalidPeriodsMessage\")");
        source.Should().Contain("FocusInvalidPeriodsInput();");
        source.Should().Contain("private void FocusInvalidPeriodsInput()");
        source.Should().Contain("DialogFocus.FocusAndSelect(_periodsBox);");
    }
}
