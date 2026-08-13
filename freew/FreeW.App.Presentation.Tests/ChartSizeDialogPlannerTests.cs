using System.Globalization;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Presentation.Tests;

public sealed class ChartSizeDialogPlannerTests
{
    [Fact]
    public void BuildInitialState_FormatsPointValuesWithCurrentDialogPrecision()
    {
        var state = ChartSizeDialogPlanner.BuildInitialState(
            widthPt: 320.125,
            heightPt: 180.5,
            CultureInfo.InvariantCulture);

        state.WidthText.Should().Be("320.13");
        state.HeightText.Should().Be("180.5");
    }

    [Fact]
    public void TryBuildResult_ConstructsPositivePointSize()
    {
        ChartSizeDialogPlanner.TryBuildResult(
                new ChartSizeDialogInput("320.5", "180"),
                CultureInfo.InvariantCulture,
                out var result,
                out var validation)
            .Should().BeTrue();

        validation.Should().BeNull();
        result.Should().Be(new ChartSizeDialogResult(320.5, 180));
    }

    [Theory]
    [InlineData("", "180", ChartSizeDialogPlanner.WidthValidationMessage)]
    [InlineData("0", "180", ChartSizeDialogPlanner.WidthValidationMessage)]
    [InlineData("-1", "180", ChartSizeDialogPlanner.WidthValidationMessage)]
    [InlineData("320", "", ChartSizeDialogPlanner.HeightValidationMessage)]
    [InlineData("320", "0", ChartSizeDialogPlanner.HeightValidationMessage)]
    [InlineData("320", "-1", ChartSizeDialogPlanner.HeightValidationMessage)]
    public void TryBuildResult_RejectsMissingZeroOrNegativeValues(
        string widthText,
        string heightText,
        string expectedMessage)
    {
        ChartSizeDialogPlanner.TryBuildResult(
                new ChartSizeDialogInput(widthText, heightText),
                CultureInfo.InvariantCulture,
                out var result,
                out var validation)
            .Should().BeFalse();

        result.Should().BeNull();
        validation.Should().NotBeNull();
        validation!.Message.Should().Be(expectedMessage);
        validation.Field.Should().Be(expectedMessage == ChartSizeDialogPlanner.WidthValidationMessage
            ? ChartSizeDialogField.Width
            : ChartSizeDialogField.Height);
    }
}
