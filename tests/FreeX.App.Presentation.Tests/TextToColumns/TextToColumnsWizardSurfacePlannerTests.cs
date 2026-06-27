using FluentAssertions;
using FreeX.App.Presentation.TextToColumns;

namespace FreeX.App.Presentation.Tests.TextToColumns;

public sealed class TextToColumnsWizardSurfacePlannerTests
{
    [Theory]
    [InlineData(1, false, true, false, false, false, false, false, true, false, "TextToColumns_ChooseFileTypeInstruction")]
    [InlineData(2, false, false, true, false, false, false, true, true, false, "TextToColumns_ChooseDelimitersInstruction")]
    [InlineData(2, true, false, false, true, false, false, true, true, false, "TextToColumns_ChooseDelimitersInstruction")]
    [InlineData(3, false, false, false, false, true, true, true, false, true, "TextToColumns_SelectColumnFormatAndDestinationInstruction")]
    public void CreateStepPlan_MapsExcelWizardPanelsAndButtons(
        int step,
        bool fixedWidth,
        bool showOriginal,
        bool showDelimited,
        bool showFixedWidth,
        bool showFormat,
        bool showDestination,
        bool backEnabled,
        bool nextEnabled,
        bool finishDefault,
        string instructionKey)
    {
        var plan = TextToColumnsWizardSurfacePlanner.CreateStepPlan(step, fixedWidth);

        TextToColumnsWizardSurfacePlanner.HeaderFormatKey.Should().Be("TextToColumns_TextWizardStepOf3");
        plan.Step.Should().Be(Math.Clamp(step, 1, 3));
        plan.InstructionKey.Should().Be(instructionKey);
        plan.ShowOriginalDataTypePanel.Should().Be(showOriginal);
        plan.ShowDelimiterPanel.Should().Be(showDelimited);
        plan.ShowFixedWidthPanel.Should().Be(showFixedWidth);
        plan.ShowColumnFormatPanel.Should().Be(showFormat);
        plan.ShowDestinationPanel.Should().Be(showDestination);
        plan.BackEnabled.Should().Be(backEnabled);
        plan.NextEnabled.Should().Be(nextEnabled);
        plan.NextDefault.Should().Be(nextEnabled);
        plan.FinishDefault.Should().Be(finishDefault);
    }

    [Theory]
    [InlineData(false, false, true, false, false, 0.55)]
    [InlineData(false, true, true, true, false, 0.55)]
    [InlineData(true, true, false, false, true, 1.0)]
    public void CreateModePlan_MapsDelimitedAndFixedWidthControlState(
        bool fixedWidth,
        bool otherSelected,
        bool delimitedEnabled,
        bool customEnabled,
        bool fixedWidthEnabled,
        double rulerOpacity)
    {
        TextToColumnsWizardSurfacePlanner.CreateModePlan(fixedWidth, otherSelected)
            .Should()
            .Be(new TextToColumnsWizardSurfaceModePlan(
                delimitedEnabled,
                customEnabled,
                fixedWidthEnabled,
                rulerOpacity));
    }
}
