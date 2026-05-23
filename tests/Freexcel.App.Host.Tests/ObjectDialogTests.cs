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

        result.Should().Be(new HyperlinkDialogResult(
            HyperlinkLinkType.ExistingFileOrWebPage,
            "https://example.test",
            "https://example.test",
            "",
            ""));
    }

    [Fact]
    public void HyperlinkDialog_CreateResult_TrimsScreenTipAndBookmarkMetadata()
    {
        var result = HyperlinkDialog.CreateResult(
            " Sheet1!A1 ",
            " Jump ",
            HyperlinkLinkType.PlaceInThisDocument,
            "  Open budget cell  ",
            "  BudgetAnchor  ");

        result.Should().Be(new HyperlinkDialogResult(
            HyperlinkLinkType.PlaceInThisDocument,
            "Sheet1!A1",
            "Jump",
            "Open budget cell",
            "BudgetAnchor"));
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
        var objectSizeSource = source[
            source.IndexOf("public sealed class ObjectSizeDialog", StringComparison.Ordinal)..
            source.IndexOf("public sealed record RotationDialogResult", StringComparison.Ordinal)];

        objectSizeSource.Should().Contain("_widthBox");
        objectSizeSource.Should().Contain("_heightBox");
        objectSizeSource.Should().Contain("Height:");
        objectSizeSource.Should().Contain("Width:");
        objectSizeSource.Should().Contain("_lockAspectRatioBox");
        objectSizeSource.Should().Contain("Content = \"_Lock aspect ratio\"");
        objectSizeSource.Should().Contain("CalculateLockedAspectHeight");
        objectSizeSource.Should().Contain("CalculateLockedAspectWidth");
    }

    [Fact]
    public void ObjectSizeDialog_CalculatesLockedAspectSize()
    {
        ObjectSizeDialog.CalculateLockedAspectHeight(240, originalWidth: 120, originalHeight: 60)
            .Should()
            .Be(120);
        ObjectSizeDialog.CalculateLockedAspectWidth(90, originalWidth: 120, originalHeight: 60)
            .Should()
            .Be(180);
    }

    [Fact]
    public void ObjectDialogs_LabelSharedInputHelpersWithTargets()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "ObjectDialogs.cs"));

        source.Should().Contain("new Label { Content = label, Target = box");
        source.Should().NotContain("new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 4) }");
    }

    [Fact]
    public void HyperlinkDialog_LabelsTextRowsWithAccessKeyTargets()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "ObjectDialogs.cs"));

        source.Should().Contain("AddTextRow(grid, 0, \"Text to _display:\", _displayBox, displayText)");
        source.Should().Contain("AddTextRow(grid, 1, \"_Address:\", _targetBox, target)");
        source.Should().Contain("new Label");
        source.Should().Contain("Content = label");
        source.Should().Contain("Target = box");
    }

    [Fact]
    public void ShapeGradientDialog_LabelsStopRgbEditorsWithAccessKeyTargets()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "ShapeGradientDialog.cs"));

        source.Should().Contain("Gradient stops");
        source.Should().Contain("AddStopRow(grid, 0, \"Stop 1 _color (RGB):\", _startColorBox");
        source.Should().Contain("AddStopRow(grid, 1, \"Stop 2 c_olor (RGB):\", _endColorBox");
        source.Should().Contain("Target = box");
        source.Should().NotContain("RGB _override:");
        source.Should().NotContain("_gradientBox");
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
    public void FormatPictureDialog_TryCreateResult_CapturesSizeRotationCropAndAltText()
    {
        FormatPictureDialog.TryCreateResult(
                "320x180",
                "45",
                "10, 5, 0, 20",
                " Revenue chart ",
                out var result,
                out var error)
            .Should()
            .BeTrue();

        error.Should().BeNull();
        result.Should().Be(new FormatPictureDialogResult(
            320,
            180,
            45,
            0.10,
            0.05,
            0,
            0.20,
            "Revenue chart"));
    }

    [Fact]
    public void FormatPictureDialog_ExposesExcelStyleTabsAndAspectRatioControls()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "FormatPictureDialog.cs"));
        var drawingSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.Drawing.cs"));

        source.Should().Contain("public sealed class FormatPictureDialog");
        source.Should().Contain("Header = \"_Size\"");
        source.Should().Contain("Header = \"_Crop\"");
        source.Should().Contain("Header = \"_Alt Text\"");
        source.Should().Contain("Content = \"_Lock aspect ratio\"");
        source.Should().Contain("SyncAspectFromWidth");
        source.Should().Contain("SyncAspectFromHeight");
        source.Should().Contain("Crop is available for inserted image pictures.");
        drawingSource.Should().Contain("new FormatPictureDialog(picture)");
        drawingSource.Should().Contain("CreateFormatPictureCommand");
        drawingSource.Should().Contain("new SetPictureAltTextCommand");
        drawingSource.Should().Contain("new CompositeWorkbookCommand(\"Format Picture\", commands)");
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
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "ShapeGradientDialog.cs"));

        source.Should().Contain("_startColorButton");
        source.Should().Contain("_endColorButton");
        source.Should().Contain("Content = \"_Start Color...\"");
        source.Should().Contain("Content = \"_End Color...\"");
        source.Should().Contain("new ColorPickerDialog(_startColor)");
        source.Should().Contain("new ColorPickerDialog(_endColor)");
        source.Should().Contain("_startColorBox.TextChanged += (_, _) => SyncGradientTextFromInputs()");
        source.Should().Contain("_endColorBox.TextChanged += (_, _) => SyncGradientTextFromInputs()");
        source.Should().Contain("UpdateColorText()");
    }

    [Fact]
    public void HyperlinkDialog_ExposesExcelLikeLinkTypeAndScreenTipAffordances()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "ObjectDialogs.cs"));

        source.Should().Contain("Existing File or Web Page");
        source.Should().Contain("Create New Document");
        source.Should().Contain("Place in This Document");
        source.Should().Contain("E-mail Address");
        source.Should().Contain("_screenTipButton");
        source.Should().Contain("_bookmarkButton");
        source.Should().Contain("ScreenTipDialog");
        source.Should().Contain("BookmarkDialog");
        source.Should().Contain("_screenTipButton.Click +=");
        source.Should().Contain("_bookmarkButton.Click +=");
    }

    [Fact]
    public void TextEntryDialog_CreateResult_TrimsNullToEmptyText()
    {
        TextEntryDialog.CreateResult(null).Text.Should().Be("");
        TextEntryDialog.CreateResult("  keep spacing inside  ").Text.Should().Be("keep spacing inside");
    }
}
