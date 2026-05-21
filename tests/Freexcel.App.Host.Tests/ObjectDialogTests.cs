using Freexcel.Core.Model;
using FluentAssertions;
using System.IO;

namespace Freexcel.App.Host.Tests;

public sealed class ObjectDialogTests
{
    [Fact]
    public void HyperlinkDialog_CreateResult_UsesTargetAsDisplayTextWhenLabelIsBlank()
    {
        var result = HyperlinkDialog.CreateResult("https://example.test", " ");

        result.Should().Be(new HyperlinkDialogResult("https://example.test", "https://example.test"));
    }

    [Fact]
    public void ObjectSizeDialog_TryParseSize_AcceptsExcelLikeWidthByHeightText()
    {
        ObjectSizeDialog.TryParseSize("320 x 180", out var size).Should().BeTrue();

        size.Should().Be(new ObjectSizeDialogResult(320, 180));
    }

    [Theory]
    [InlineData("NaNx180")]
    [InlineData("320xInfinity")]
    [InlineData("-1x180")]
    [InlineData("320x0")]
    public void ObjectSizeDialog_TryParseSize_RejectsNonFiniteAndNonPositiveSizes(string input)
    {
        ObjectSizeDialog.TryParseSize(input, out var size).Should().BeFalse();

        size.Should().Be(new ObjectSizeDialogResult(0, 0));
    }

    [Fact]
    public void ObjectSizeDialog_ExposesExcelLikeWidthHeightAndAspectRatioControls()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "ObjectDialogs.cs"));

        source.Should().Contain("_widthBox");
        source.Should().Contain("_heightBox");
        source.Should().Contain("_lockAspectRatioBox");
        source.Should().Contain("Height:");
        source.Should().Contain("Width:");
        source.Should().Contain("Lock aspect ratio");
    }

    [Fact]
    public void RotationDialog_TryParseRotation_AcceptsNumericDegrees()
    {
        RotationDialog.TryParseRotation("45.5", out var rotation).Should().BeTrue();

        rotation.Should().Be(new RotationDialogResult(45.5));
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    public void RotationDialog_TryParseRotation_RejectsNonFiniteDegrees(string input)
    {
        RotationDialog.TryParseRotation(input, out var rotation).Should().BeFalse();

        rotation.Should().Be(new RotationDialogResult(0));
    }

    [Fact]
    public void PictureCropDialog_TryCreateResult_RejectsCropThatRemovesVisibleArea()
    {
        PictureCropDialog.TryCreateResult("60, 0, 50, 0", out _, out var error).Should().BeFalse();

        error.Should().Contain("percentages");
    }

    [Fact]
    public void PictureCropDialog_TryCreateResult_ParsesPercentEdges()
    {
        PictureCropDialog.TryCreateResult("10, 5, 0, 20", out var result, out _).Should().BeTrue();

        result.Should().Be(new PictureCropDialogResult(0.10, 0.05, 0, 0.20));
    }

    [Fact]
    public void PictureCropDialog_ExposesSeparateExcelCropEdgeFields()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "ObjectDialogs.cs"));

        source.Should().Contain("_cropLeftBox");
        source.Should().Contain("_cropTopBox");
        source.Should().Contain("_cropRightBox");
        source.Should().Contain("_cropBottomBox");
        source.Should().Contain("Left:");
        source.Should().Contain("Right:");
    }

    [Fact]
    public void ShapeGradientDialog_TryCreateResult_ParsesTwoRgbColors()
    {
        ShapeGradientDialog.TryCreateResult("31,119,180; 180,210,240", out var result, out _).Should().BeTrue();

        result.Should().Be(new ShapeGradientDialogResult(
            new CellColor(31, 119, 180),
            new CellColor(180, 210, 240)));
    }

    [Fact]
    public void ShapeGradientDialog_ExposesColorPickerButtonsForStartAndEndColors()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "ObjectDialogs.cs"));

        source.Should().Contain("_startColorButton");
        source.Should().Contain("_endColorButton");
        source.Should().Contain("Content = \"_Start Color...\"");
        source.Should().Contain("Content = \"_End Color...\"");
        source.Should().Contain("new ColorPickerDialog(_startColor)");
        source.Should().Contain("new ColorPickerDialog(_endColor)");
    }

    [Fact]
    public void HyperlinkDialog_ExposesExcelLikeLinkTypeAndScreenTipAffordances()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "ObjectDialogs.cs"));

        source.Should().Contain("Existing File or Web Page");
        source.Should().Contain("Place in This Document");
        source.Should().Contain("E-mail Address");
        source.Should().Contain("_screenTipButton");
        source.Should().Contain("_bookmarkButton");
    }

    [Fact]
    public void TextEntryDialog_CreateResult_TrimsNullToEmptyText()
    {
        TextEntryDialog.CreateResult(null).Text.Should().Be("");
        TextEntryDialog.CreateResult("  keep spacing inside  ").Text.Should().Be("keep spacing inside");
    }
}
