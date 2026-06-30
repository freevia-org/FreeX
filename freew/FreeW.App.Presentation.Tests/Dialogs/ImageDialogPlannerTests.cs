using System.Globalization;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests;

public sealed class ImageDialogPlannerTests
{
    [Fact]
    public void ImageAdjust_FormatsPercentFieldsAndBuildsBoundedResult()
    {
        var state = ImageAdjustDialogPlanner.BuildInitialState(
            brightnessPct: -12.345,
            contrastPct: 10,
            saturationPct: 125.5,
            transparencyPct: 33.333,
            CultureInfo.InvariantCulture);

        state.Should().Be(new ImageAdjustDialogInitialState("-12.35", "10", "125.5", "33.33"));

        ImageAdjustDialogPlanner.TryBuildResult(
                new ImageAdjustDialogInput("-10", "20", "150", "25"),
                CultureInfo.InvariantCulture,
                out var result,
                out var validation)
            .Should().BeTrue();

        validation.Should().BeNull();
        result.Should().Be(new ImageAdjustDialogResult(-10, 20, 150, 25));
    }

    [Theory]
    [InlineData("-101", "0", "100", "0", ImageAdjustDialogField.Brightness, ImageAdjustDialogPlanner.BrightnessValidationMessage)]
    [InlineData("0", "101", "100", "0", ImageAdjustDialogField.Contrast, ImageAdjustDialogPlanner.ContrastValidationMessage)]
    [InlineData("0", "0", "401", "0", ImageAdjustDialogField.Saturation, ImageAdjustDialogPlanner.SaturationValidationMessage)]
    [InlineData("0", "0", "100", "-1", ImageAdjustDialogField.Transparency, ImageAdjustDialogPlanner.TransparencyValidationMessage)]
    public void ImageAdjust_RejectsFirstOutOfRangeField(
        string brightnessText,
        string contrastText,
        string saturationText,
        string transparencyText,
        ImageAdjustDialogField expectedField,
        string expectedMessage)
    {
        ImageAdjustDialogPlanner.TryBuildResult(
                new ImageAdjustDialogInput(brightnessText, contrastText, saturationText, transparencyText),
                CultureInfo.InvariantCulture,
                out var result,
                out var validation)
            .Should().BeFalse();

        result.Should().BeNull();
        validation.Should().Be(new ImageAdjustValidation(expectedField, expectedMessage));
    }

    [Fact]
    public void ImageBorder_ExposesDashCatalogDefaultsAndNormalizesResult()
    {
        ImageBorderDialogPlanner.DashItems.Select(item => item.Label)
            .Should().Equal("solid", "dash", "dot", "dashDot", "dashDotDot", "lgDash", "lgDashDot");

        var state = ImageBorderDialogPlanner.BuildInitialState(
            colorHex: "#00aaee",
            widthPt: 0,
            dash: "dashDot",
            CultureInfo.InvariantCulture);

        state.Should().Be(new ImageBorderDialogInitialState("00aaee", "0.75", 3));

        ImageBorderDialogPlanner.TryBuildResult(
                new ImageBorderDialogInput("#00aaee", "1.25", 3),
                CultureInfo.InvariantCulture,
                out var result,
                out var validation)
            .Should().BeTrue();

        validation.Should().BeNull();
        result.Should().Be(new ImageBorderDialogResult("00AAEE", 1.25, "dashDot"));

        ImageBorderDialogPlanner.TryBuildResult(
                new ImageBorderDialogInput(" ", "9", 1),
                CultureInfo.InvariantCulture,
                out result,
                out validation)
            .Should().BeTrue();

        validation.Should().BeNull();
        result.Should().Be(new ImageBorderDialogResult(null, 0, null));
    }

    [Theory]
    [InlineData("oops", "1", ImageBorderDialogField.Color, ImageBorderDialogPlanner.ColorValidationMessage)]
    [InlineData("FF0000", "0", ImageBorderDialogField.Width, ImageBorderDialogPlanner.WidthValidationMessage)]
    public void ImageBorder_RejectsInvalidColorOrWidth(
        string colorText,
        string widthText,
        ImageBorderDialogField expectedField,
        string expectedMessage)
    {
        ImageBorderDialogPlanner.TryBuildResult(
                new ImageBorderDialogInput(colorText, widthText, 0),
                CultureInfo.InvariantCulture,
                out var result,
                out var validation)
            .Should().BeFalse();

        result.Should().BeNull();
        validation.Should().Be(new ImageBorderValidation(expectedField, expectedMessage));
    }

    [Theory]
    [InlineData("#abc")]
    [InlineData("11223344")]
    public void ImageBorder_RejectsWatermarkStyleShorthandOrAlphaHex(string colorText)
    {
        ImageBorderDialogPlanner.TryBuildResult(
                new ImageBorderDialogInput(colorText, "1", 0),
                CultureInfo.InvariantCulture,
                out var result,
                out var validation)
            .Should().BeFalse();

        result.Should().BeNull();
        validation.Should().Be(new ImageBorderValidation(
            ImageBorderDialogField.Color,
            ImageBorderDialogPlanner.ColorValidationMessage));
    }

    [Fact]
    public void ImageCrop_FormatsFractionsAsPercentAndBuildsFractionResult()
    {
        var state = ImageCropDialogPlanner.BuildInitialState(
            left: 0.125,
            right: 0.05,
            top: 0.333,
            bottom: 0,
            CultureInfo.InvariantCulture);

        state.Should().Be(new ImageCropDialogInitialState("12.5", "5", "33.3", "0"));

        ImageCropDialogPlanner.TryBuildResult(
                new ImageCropDialogInput("10", "20", "3.5", "4"),
                CultureInfo.InvariantCulture,
                out var result,
                out var validation)
            .Should().BeTrue();

        validation.Should().BeNull();
        result.Should().Be(new ImageCropDialogResult(0.1, 0.2, 0.035, 0.04));
    }

    [Theory]
    [InlineData("-1", "0", "0", "0", ImageCropDialogField.Left, ImageCropDialogPlanner.PercentageValidationMessage)]
    [InlineData("50", "50", "0", "0", ImageCropDialogField.Totals, ImageCropDialogPlanner.TotalsValidationMessage)]
    public void ImageCrop_RejectsInvalidPercentOrCombinedEdges(
        string leftText,
        string rightText,
        string topText,
        string bottomText,
        ImageCropDialogField expectedField,
        string expectedMessage)
    {
        ImageCropDialogPlanner.TryBuildResult(
                new ImageCropDialogInput(leftText, rightText, topText, bottomText),
                CultureInfo.InvariantCulture,
                out var result,
                out var validation)
            .Should().BeFalse();

        result.Should().BeNull();
        validation.Should().Be(new ImageCropValidation(expectedField, expectedMessage));
    }

    [Fact]
    public void ImagePosition_ExposesAnchorCatalogsAndBuildsResult()
    {
        ImagePositionDialogPlanner.HorizontalAnchorItems.Select(item => item.Value)
            .Should().Equal(HorizontalAnchor.Column, HorizontalAnchor.Margin, HorizontalAnchor.Page);
        ImagePositionDialogPlanner.VerticalAnchorItems.Select(item => item.Value)
            .Should().Equal(VerticalAnchor.Paragraph, VerticalAnchor.Margin, VerticalAnchor.Page);

        var state = ImagePositionDialogPlanner.BuildInitialState(
            horizontalOffsetPt: 12.345,
            verticalOffsetPt: -6.5,
            horizontalAnchor: HorizontalAnchor.Page,
            verticalAnchor: VerticalAnchor.Margin,
            CultureInfo.InvariantCulture);

        state.Should().Be(new ImagePositionDialogInitialState("12.35", "-6.5", 2, 1));

        ImagePositionDialogPlanner.TryBuildResult(
                new ImagePositionDialogInput("1.5", "-2", 1, 2),
                CultureInfo.InvariantCulture,
                out var result,
                out var validation)
            .Should().BeTrue();

        validation.Should().BeNull();
        result.Should().Be(new ImagePositionDialogResult(
            1.5,
            -2,
            HorizontalAnchor.Margin,
            VerticalAnchor.Page));
    }

    [Fact]
    public void ImagePosition_RejectsFirstInvalidOffset()
    {
        ImagePositionDialogPlanner.TryBuildResult(
                new ImagePositionDialogInput("1", "x", 0, 0),
                CultureInfo.InvariantCulture,
                out var result,
                out var validation)
            .Should().BeFalse();

        result.Should().BeNull();
        validation.Should().Be(new ImagePositionValidation(
            ImagePositionDialogField.VerticalOffset,
            ImagePositionDialogPlanner.OffsetValidationMessage));
    }

    [Fact]
    public void ImageSize_FormatsInitialStateSyncsLockedAspectAndBuildsResult()
    {
        var state = ImageSizeDialogPlanner.BuildInitialState(
            currentWidthPt: 400,
            currentHeightPt: 200,
            CultureInfo.InvariantCulture);

        state.Should().Be(new ImageSizeDialogInitialState("400", "200", 0.5, true));

        ImageSizeDialogPlanner.TryBuildLockedHeightText(
                "300",
                state.AspectRatio,
                lockAspectRatio: true,
                CultureInfo.InvariantCulture,
                out var heightText)
            .Should().BeTrue();
        heightText.Should().Be("150");

        ImageSizeDialogPlanner.TryBuildLockedWidthText(
                "125",
                state.AspectRatio,
                lockAspectRatio: true,
                CultureInfo.InvariantCulture,
                out var widthText)
            .Should().BeTrue();
        widthText.Should().Be("250");

        ImageSizeDialogPlanner.TryBuildResult(
                new ImageSizeDialogInput("250", "125"),
                CultureInfo.InvariantCulture,
                out var result,
                out var validation)
            .Should().BeTrue();

        validation.Should().BeNull();
        result.Should().Be(new ImageSizeDialogResult(250, 125));
    }

    [Fact]
    public void ImageSize_RejectsNonPositiveDimensions()
    {
        ImageSizeDialogPlanner.TryBuildResult(
                new ImageSizeDialogInput("0", "125"),
                CultureInfo.InvariantCulture,
                out var result,
                out var validation)
            .Should().BeFalse();

        result.Should().BeNull();
        validation.Should().Be(new ImageSizeValidation(
            ImageSizeDialogField.Width,
            ImageSizeDialogPlanner.PositiveSizeValidationMessage));
    }
}
