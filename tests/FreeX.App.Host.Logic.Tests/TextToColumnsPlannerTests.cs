using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class TextToColumnsPlannerTests
{
    [Theory]
    [InlineData(1, false, true, false, false, false, false, false, true, false)]
    [InlineData(2, false, false, true, false, false, false, true, true, false)]
    [InlineData(2, true, false, false, true, false, false, true, true, false)]
    [InlineData(3, false, false, false, false, true, true, true, false, true)]
    public void CreateWizardStepPlan_MapsExcelWizardPanelsAndButtons(
        int step,
        bool fixedWidth,
        bool showOriginal,
        bool showDelimited,
        bool showFixedWidth,
        bool showFormat,
        bool showDestination,
        bool backEnabled,
        bool nextEnabled,
        bool finishDefault)
    {
        var plan = TextToColumnsWizardPlanner.CreateStepPlan(step, fixedWidth);

        plan.Header.Should().Be(UiText.Format("TextToColumns_TextWizardStepOf3", Math.Clamp(step, 1, 3)));
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
    public void CreateWizardModePlan_MapsDelimitedAndFixedWidthControlState(
        bool fixedWidth,
        bool otherSelected,
        bool delimitedEnabled,
        bool customEnabled,
        bool fixedWidthEnabled,
        double rulerOpacity)
    {
        TextToColumnsWizardPlanner.CreateModePlan(fixedWidth, otherSelected)
            .Should()
            .Be(new TextToColumnsWizardModePlan(
                delimitedEnabled,
                customEnabled,
                fixedWidthEnabled,
                rulerOpacity));
    }
}
