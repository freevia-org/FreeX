using FreeX.Core.Model;
using FluentAssertions;
using System.IO;
using System.Reflection;
using System.Windows.Automation;
using System.Windows.Controls;

namespace FreeX.App.Host.Tests;

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

    [Theory]
    [InlineData(HyperlinkLinkType.ExistingFileOrWebPage, "Enter an address.")]
    [InlineData(HyperlinkLinkType.CreateNewDocument, "Enter a new document name.")]
    [InlineData(HyperlinkLinkType.PlaceInThisDocument, "Enter a valid cell reference or defined name.")]
    [InlineData(HyperlinkLinkType.EmailAddress, "Enter an email address.")]
    public void HyperlinkDialog_TryCreateResult_RejectsBlankTarget(HyperlinkLinkType linkType, string expectedError)
    {
        HyperlinkDialog.TryCreateResult(" ", "Label", linkType, "", "", out _, out var error)
            .Should()
            .BeFalse();

        error.Should().Be(expectedError);
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("review@")]
    [InlineData("@example.test")]
    [InlineData("review@example test")]
    public void HyperlinkDialog_TryCreateResult_RejectsInvalidEmailTarget(string target)
    {
        HyperlinkDialog.TryCreateResult(target, "Label", HyperlinkLinkType.EmailAddress, "", "", out _, out var error)
            .Should()
            .BeFalse();

        error.Should().Be("Enter a valid email address.");
    }

    [Theory]
    [InlineData("review@example.test", "mailto:review@example.test")]
    [InlineData("mailto:review@example.test", "mailto:review@example.test")]
    public void HyperlinkDialog_TryCreateResult_AcceptsEmailTarget(string target, string expectedTarget)
    {
        HyperlinkDialog.TryCreateResult(target, "Label", HyperlinkLinkType.EmailAddress, "", "", out var result, out var error)
            .Should()
            .BeTrue(error);

        result.Target.Should().Be(expectedTarget);
    }

    [Theory]
    [InlineData("review@example.test", "mailto:review@example.test", "review@example.test")]
    [InlineData("mailto:review@example.test?subject=Budget", "mailto:review@example.test?subject=Budget", "review@example.test")]
    public void HyperlinkDialog_CreateResult_NormalizesEmailTargetWithoutLeakingMailtoIntoBlankDisplay(
        string target,
        string expectedTarget,
        string expectedDisplayText)
    {
        var result = HyperlinkDialog.CreateResult(target, " ", HyperlinkLinkType.EmailAddress);

        result.Should().Be(new HyperlinkDialogResult(
            HyperlinkLinkType.EmailAddress,
            expectedTarget,
            expectedDisplayText,
            "",
            ""));
    }

    [Fact]
    public void HyperlinkDialog_TryCreateResult_AcceptsTrimmedTargetAndMetadata()
    {
        HyperlinkDialog.TryCreateResult(
                " https://example.test ",
                " Example ",
                HyperlinkLinkType.ExistingFileOrWebPage,
                " Tip ",
                " Bookmark ",
                out var result,
                out var error)
            .Should()
            .BeTrue(error);

        result.Should().Be(new HyperlinkDialogResult(
            HyperlinkLinkType.ExistingFileOrWebPage,
            "https://example.test",
            "Example",
            "Tip",
            "Bookmark"));
    }

    [Fact]
    public void HyperlinkDialogPrefill_UsesExistingCellTextAsDisplayText()
    {
        var sheetId = SheetId.New();
        var address = new CellAddress(sheetId, 4, 2);
        var sheet = new Sheet(sheetId, "Sheet1");
        sheet.SetCell(address, new TextValue("Quarterly report"));

        HyperlinkDialogPrefill.FromCell(sheet, address).Should().Be(new HyperlinkDialogPrefill(
            "https://",
            "Quarterly report"));
    }

    [Fact]
    public void HyperlinkNavigationPlanner_CreatesExternalLaunchPlanForWebLink()
    {
        var sheetId = SheetId.New();
        var address = new CellAddress(sheetId, 1, 1);
        var sheet = new Sheet(sheetId, "Sheet1");
        sheet.Hyperlinks[address] = "https://example.test";
        sheet.HyperlinkMetadata[address] = new HyperlinkMetadata(HyperlinkTargetKind.ExistingFileOrWebPage);

        HyperlinkNavigationPlanner.TryCreatePlan(sheet, address, out var plan).Should().BeTrue();
        plan.Should().Be(new HyperlinkNavigationPlan(HyperlinkNavigationKind.External, "https://example.test", null));
    }

    [Fact]
    public void HyperlinkNavigationPlanner_CreatesWorksheetPlanForDocumentLink()
    {
        var sheetId = SheetId.New();
        var address = new CellAddress(sheetId, 1, 1);
        var sheet = new Sheet(sheetId, "Sheet1");
        sheet.Hyperlinks[address] = "Sheet2!C3";
        sheet.HyperlinkMetadata[address] = new HyperlinkMetadata(HyperlinkTargetKind.PlaceInThisDocument);

        HyperlinkNavigationPlanner.TryCreatePlan(sheet, address, out var plan).Should().BeTrue();
        plan!.Kind.Should().Be(HyperlinkNavigationKind.WorksheetCell);
        plan.Target.Should().Be("Sheet2!C3");
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
        var source = ReadObjectDialogSources();
        var objectSizeSource = source[
            source.IndexOf("public sealed class ObjectSizeDialog", StringComparison.Ordinal)..
            source.IndexOf("public sealed record RotationDialogResult", StringComparison.Ordinal)];

        objectSizeSource.Should().Contain("_widthBox");
        objectSizeSource.Should().Contain("_heightBox");
        objectSizeSource.Should().Contain("_Height:");
        objectSizeSource.Should().Contain("_Width:");
        objectSizeSource.Should().Contain("new Label { Content = label, Target = box");
        objectSizeSource.Should().Contain("_lockAspectRatioBox");
        objectSizeSource.Should().Contain("Content = \"_Lock aspect ratio\"");
        objectSizeSource.Should().Contain("CalculateLockedAspectHeight");
        objectSizeSource.Should().Contain("CalculateLockedAspectWidth");
    }

    [Fact]
    public void ObjectSizeDialog_SizeControlsExposeAutomationMetadata()
    {
        var source = ReadClassSource("ObjectSizingDialogs.cs", "public sealed class ObjectSizeDialog", "public sealed record RotationDialogResult");

        source.Should().Contain("AutomationProperties.SetName(_heightBox, \"Object height\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(_heightBox, \"ObjectSizeHeightBox\");");
        source.Should().Contain("AutomationProperties.SetHelpText(_heightBox, \"Enter the object's height.\");");
        source.Should().Contain("AutomationProperties.SetName(_widthBox, \"Object width\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(_widthBox, \"ObjectSizeWidthBox\");");
        source.Should().Contain("AutomationProperties.SetHelpText(_widthBox, \"Enter the object's width.\");");
        source.Should().Contain("AutomationProperties.SetName(_lockAspectRatioBox, \"Lock aspect ratio\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(_lockAspectRatioBox, \"ObjectSizeLockAspectRatioCheckBox\");");
        source.Should().Contain("AutomationProperties.SetHelpText(_lockAspectRatioBox, \"Keep the object's width and height proportional.\");");
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
    public void ObjectSizeDialogOpenedFromKeyboard_FocusesFirstSizeInput()
    {
        var source = ReadClassSource("ObjectSizingDialogs.cs", "public sealed class ObjectSizeDialog", "public sealed record RotationDialogResult");

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("DialogFocus.FocusAndSelect(_heightBox);");
    }

    [Fact]
    public void ObjectSizeDialogInvalidSize_ShowsOwnedWarningAndRefocusesInvalidSizeInput()
    {
        var source = ReadClassSource("ObjectSizingDialogs.cs", "public sealed class ObjectSizeDialog", "public sealed record RotationDialogResult");

        source.Should().Contain("DialogMessageHelper.ShowWarning(this,");
        source.Should().Contain("Enter positive width and height values.");
        source.Should().Contain("FocusInvalidSizeInput(ResolveInvalidSizeInput());");
        source.Should().Contain("private TextBox ResolveInvalidSizeInput()");
        source.Should().Contain("if (!TryParsePositiveSize(_heightBox.Text))");
        source.Should().Contain("if (!TryParsePositiveSize(_widthBox.Text))");
        source.Should().Contain("private static bool TryParsePositiveSize(string text)");
        source.Should().Contain("private static void FocusInvalidSizeInput(TextBox textBox)");
        source.Should().Contain("DialogFocus.FocusAndSelect(textBox);");
    }

    [Fact]
    public void ObjectDialogs_LabelSharedInputHelpersWithTargets()
    {
        var source = ReadObjectDialogSources();

        source.Should().Contain("new Label { Content = label, Target = box");
        source.Should().NotContain("new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 4) }");
    }

    [Fact]
    public void ObjectDialogs_UseSharedButtonRowsOutsideChartDialogs()
    {
        var objectSource = ReadObjectDialogSources();
        var formatPictureSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "FormatPictureDialog.cs"));
        var namedRangeSource =
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "NamedRangeDialog.xaml.cs")) +
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "NameDefinitionDialog.cs"));
        var shapeGradientSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ShapeGradientDialog.cs"));

        foreach (var source in new[] { objectSource, formatPictureSource, namedRangeSource, shapeGradientSource })
        {
            source.Should().Contain("DialogButtonRowFactory.Create");
            source.Should().NotContain("InsertChartDialog.CreateButtonRow");
        }
    }

    [Fact]
    public void HyperlinkDialog_LabelsTextRowsWithAccessKeyTargets()
    {
        var source = ReadObjectDialogSources();

        source.Should().Contain("AddTextRow(grid, 0, \"Text to _display:\", _displayBox, displayText)");
        source.Should().Contain("AddTextRow(grid, 1, \"_Address:\", _targetBox, target)");
        source.Should().Contain("new Label");
        source.Should().Contain("Content = label");
        source.Should().Contain("Target = box");
    }

    [Fact]
    public void ShapeGradientDialog_LabelsStopRgbEditorsWithAccessKeyTargets()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ShapeGradientDialog.cs"));

        source.Should().Contain("UiText.Get(\"ShapeGradient_GradientStopsGroup\")");
        source.Should().Contain("AddStopRow(grid, 0, UiText.Get(\"ShapeGradient_Stop1ColorLabel\"), _startColorBox");
        source.Should().Contain("AddStopRow(grid, 1, UiText.Get(\"ShapeGradient_Stop2ColorLabel\"), _endColorBox");
        source.Should().Contain("Target = box");
        source.Should().NotContain("RGB _override:");
        source.Should().NotContain("_gradientBox");
    }

    [Fact]
    public void ShapeGradientDialogOpenedFromKeyboard_FocusesStartColorBox()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ShapeGradientDialog.cs"));

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("DialogFocus.FocusAndSelect(_startColorBox);");
    }

    [Fact]
    public void ShapeGradientDialogInvalidColor_ShowsOwnedWarningAndRefocusesFirstInvalidColor()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ShapeGradientDialog.cs"));

        source.Should().Contain("DialogMessageHelper.ShowWarning(this,");
        source.Should().Contain("UiText.Get(\"ShapeGradient_InvalidRgbColorMessage\")");
        source.Should().Contain("FocusInvalidColorInput(_startColorBox);");
        source.Should().Contain("FocusInvalidColorInput(_endColorBox);");
        source.Should().Contain("private static void FocusInvalidColorInput(TextBox colorBox)");
        source.Should().Contain("DialogFocus.FocusAndSelect(colorBox);");
    }

    [Fact]
    public void RotationDialog_TryParseRotation_AcceptsNumericDegrees()
    {
        RotationDialog.TryParseRotation("45.5", out var rotation).Should().BeTrue();

        rotation.Should().Be(new RotationDialogResult(45.5));
    }

    [Theory]
    [InlineData("450", 90)]
    [InlineData("-90", 270)]
    [InlineData("720", 0)]
    public void RotationDialog_TryParseRotation_NormalizesExcelFullTurnDegrees(string input, double expectedDegrees)
    {
        RotationDialog.TryParseRotation(input, out var rotation).Should().BeTrue();

        rotation.Should().Be(new RotationDialogResult(expectedDegrees));
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
    public void RotationDialogOpenedFromKeyboard_FocusesDegreesInput()
    {
        var source = ReadClassSource("ObjectSizingDialogs.cs", "public sealed class RotationDialog", "public sealed record PictureCropDialogResult");

        source.Should().Contain("ObjectSizeDialog.CreateSingleInputContent(\"_Degrees:\", _rotationBox, Accept)");
        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("DialogFocus.FocusAndSelect(_rotationBox);");
        source.Should().Contain("NormalizeRotationDegrees(value)");
    }

    [Fact]
    public void RotationDialog_DegreesInputExposesAutomationMetadata()
    {
        var source = ReadClassSource("ObjectSizingDialogs.cs", "public sealed class RotationDialog", "public sealed record PictureCropDialogResult");

        source.Should().Contain("AutomationProperties.SetName(_rotationBox, \"Rotation degrees\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(_rotationBox, \"RotationDegreesBox\");");
        source.Should().Contain("AutomationProperties.SetHelpText(_rotationBox, \"Enter the object's rotation in degrees.\");");
    }

    [Fact]
    public void RotationDialogInvalidDegrees_ShowsOwnedWarningAndRefocusesInput()
    {
        var source = ReadClassSource("ObjectSizingDialogs.cs", "public sealed class RotationDialog", "public sealed record PictureCropDialogResult");

        source.Should().Contain("DialogMessageHelper.ShowWarning(this,");
        source.Should().Contain("Enter a numeric rotation value.");
        source.Should().Contain("FocusInvalidRotationInput();");
        source.Should().Contain("private void FocusInvalidRotationInput()");
        source.Should().Contain("DialogFocus.FocusAndSelect(_rotationBox);");
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
        var source = ReadObjectDialogSources();

        source.Should().Contain("_cropLeftBox");
        source.Should().Contain("_cropTopBox");
        source.Should().Contain("_cropRightBox");
        source.Should().Contain("_cropBottomBox");
        source.Should().Contain("_Left:");
        source.Should().Contain("_Top:");
        source.Should().Contain("_Right:");
        source.Should().Contain("_Bottom:");
        source.Should().Contain("new Label { Content = label, Target = box");
    }

    [Fact]
    public void PictureCropDialog_CropFieldsExposeAutomationMetadata()
    {
        var source = ReadClassSource("ObjectSizingDialogs.cs", "public sealed class PictureCropDialog", "");

        source.Should().Contain("AutomationProperties.SetName(_cropLeftBox, \"Crop left\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(_cropLeftBox, \"PictureCropLeftBox\");");
        source.Should().Contain("AutomationProperties.SetHelpText(_cropLeftBox, \"Enter the left crop percentage.\");");
        source.Should().Contain("AutomationProperties.SetName(_cropTopBox, \"Crop top\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(_cropTopBox, \"PictureCropTopBox\");");
        source.Should().Contain("AutomationProperties.SetHelpText(_cropTopBox, \"Enter the top crop percentage.\");");
        source.Should().Contain("AutomationProperties.SetName(_cropRightBox, \"Crop right\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(_cropRightBox, \"PictureCropRightBox\");");
        source.Should().Contain("AutomationProperties.SetHelpText(_cropRightBox, \"Enter the right crop percentage.\");");
        source.Should().Contain("AutomationProperties.SetName(_cropBottomBox, \"Crop bottom\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(_cropBottomBox, \"PictureCropBottomBox\");");
        source.Should().Contain("AutomationProperties.SetHelpText(_cropBottomBox, \"Enter the bottom crop percentage.\");");
    }

    [Fact]
    public void PictureCropDialogOpenedFromKeyboard_FocusesLeftCropInput()
    {
        var source = ReadClassSource("ObjectSizingDialogs.cs", "public sealed class PictureCropDialog", "");

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("DialogFocus.FocusAndSelect(_cropLeftBox);");
    }

    [Fact]
    public void PictureCropDialogInvalidCrop_ShowsOwnedWarningAndRefocusesInvalidCropInput()
    {
        var source = ReadClassSource("ObjectSizingDialogs.cs", "public sealed class PictureCropDialog", "");

        source.Should().Contain("DialogMessageHelper.ShowWarning(this,");
        source.Should().Contain("error ?? \"Enter four crop percentages.\"");
        source.Should().Contain("FocusInvalidCropInput(ResolveInvalidCropInput(error));");
        source.Should().Contain("private TextBox ResolveInvalidCropInput(string? error)");
        source.Should().Contain("return _cropLeftBox;");
        source.Should().Contain("return _cropTopBox;");
        source.Should().Contain("return _cropRightBox;");
        source.Should().Contain("return _cropBottomBox;");
        source.Should().Contain("private static void FocusInvalidCropInput(TextBox textBox)");
        source.Should().Contain("DialogFocus.FocusAndSelect(textBox);");
    }

    [Fact]
    public void FormatPictureDialog_TryCreateResult_CapturesSizeRotationCropAndAltText()
    {
        FormatPictureDialog.TryCreateResult(
                "320x180",
                "45",
                false,
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
            false,
            0.10,
            0.05,
            0,
            0.20,
            "Revenue chart"));
    }

    [Fact]
    public void FormatPictureDialog_ExposesExcelStyleTabsAndAspectRatioControls()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "FormatPictureDialog.cs"));
        var drawingSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.Drawing.cs"));

        source.Should().Contain("public sealed class FormatPictureDialog");
        source.Should().Contain("Header = UiText.Get(\"FormatPicture_SizeTab\")");
        source.Should().Contain("Header = UiText.Get(\"FormatPicture_CropTab\")");
        source.Should().Contain("Header = UiText.Get(\"FormatPicture_AltTextTab\")");
        source.Should().Contain("Content = UiText.Get(\"FormatPicture_LockAspectRatio\")");
        source.Should().Contain("LockAspectRatio");
        source.Should().Contain("_lockAspectRatioBox.IsChecked = picture.LockAspectRatio");
        source.Should().Contain("SyncAspectFromWidth");
        source.Should().Contain("SyncAspectFromHeight");
        source.Should().Contain("UiText.Get(\"FormatPicture_CropUnavailableMessage\")");
        drawingSource.Should().Contain("new FormatPictureDialog(picture)");
        drawingSource.Should().Contain("CreateFormatPictureCommand");
        drawingSource.Should().Contain("new SetPictureLockAspectRatioCommand");
        drawingSource.Should().Contain("new SetPictureAltTextCommand");
        drawingSource.Should().Contain("new CompositeWorkbookCommand(\"Format Picture\", commands)");
    }

    [Fact]
    public void FormatPictureDialog_ExposesQuickResetActionsForInitialSizeAndCrop()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "FormatPictureDialog.cs"));

        source.Should().Contain("Content = UiText.Get(\"FormatPicture_ResetSizeButton\")");
        source.Should().Contain("Content = UiText.Get(\"FormatPicture_ResetCropButton\")");
        source.Should().Contain("ResetSizeToInitial");
        source.Should().Contain("ResetCropToInitial");
        source.Should().Contain("_resetCropButton.IsEnabled = false");
    }

    [Fact]
    public void FormatPictureDialogOpenedFromKeyboard_FocusesHeightBox()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "FormatPictureDialog.cs"));

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("_heightBox.Focus();");
        source.Should().Contain("_heightBox.SelectAll();");
        source.Should().Contain("Keyboard.Focus(_heightBox);");
    }

    [Fact]
    public void FormatPictureDialogInvalidInput_SelectsRelevantTabAndField()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "FormatPictureDialog.cs"));

        source.Should().Contain("private readonly TabControl _tabs = new();");
        source.Should().Contain("private readonly TabItem _sizeTab = new() { Header = UiText.Get(\"FormatPicture_SizeTab\") };");
        source.Should().Contain("private readonly TabItem _cropTab = new() { Header = UiText.Get(\"FormatPicture_CropTab\") };");
        source.Should().Contain("FocusInvalidInput(error);");
        source.Should().Contain("private void FocusInvalidInput(string? error)");
        source.Should().Contain("_tabs.SelectedItem = _sizeTab;");
        source.Should().Contain("_tabs.SelectedItem = _cropTab;");
        source.Should().Contain("DialogFocus.FocusAndSelect(_rotationBox);");
        source.Should().Contain("DialogFocus.FocusAndSelect(ResolveInvalidSizeInput());");
        source.Should().Contain("private TextBox ResolveInvalidSizeInput()");
        source.Should().Contain("if (!TryParsePositiveSize(_heightBox.Text))");
        source.Should().Contain("if (!TryParsePositiveSize(_widthBox.Text))");
        source.Should().Contain("DialogFocus.FocusAndSelect(ResolveInvalidCropInput(error));");
        source.Should().Contain("private TextBox ResolveInvalidCropInput(string? error)");
        source.Should().Contain("return _cropLeftBox;");
        source.Should().Contain("return _cropTopBox;");
        source.Should().Contain("return _cropRightBox;");
        source.Should().Contain("return _cropBottomBox;");
        source.Should().NotContain("private static void FocusAndSelect(TextBox box)");
    }

    [Fact]
    public void FormatPictureDialog_ResetActionsRestoreInitialFieldText()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "FormatPictureDialog.cs"));

        source.Should().Contain("_widthBox.Text = _initialResult.Width.ToString(CultureInfo.InvariantCulture)");
        source.Should().Contain("_heightBox.Text = _initialResult.Height.ToString(CultureInfo.InvariantCulture)");
        source.Should().Contain("_rotationBox.Text = _initialResult.RotationDegrees.ToString(CultureInfo.InvariantCulture)");
        source.Should().Contain("_lockAspectRatioBox.IsChecked = _initialResult.LockAspectRatio");
        source.Should().Contain("_cropLeftBox.Text = DrawingInputParser.FormatCropPercent(_initialResult.CropLeft)");
        source.Should().Contain("_cropTopBox.Text = DrawingInputParser.FormatCropPercent(_initialResult.CropTop)");
        source.Should().Contain("_cropRightBox.Text = DrawingInputParser.FormatCropPercent(_initialResult.CropRight)");
        source.Should().Contain("_cropBottomBox.Text = DrawingInputParser.FormatCropPercent(_initialResult.CropBottom)");
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
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ShapeGradientDialog.cs"));

        source.Should().Contain("_startColorButton");
        source.Should().Contain("_endColorButton");
        source.Should().Contain("Content = UiText.Get(\"ShapeGradient_StartColorButton\")");
        source.Should().Contain("Content = UiText.Get(\"ShapeGradient_EndColorButton\")");
        source.Should().Contain("new ColorPickerDialog(_startColor)");
        source.Should().Contain("new ColorPickerDialog(_endColor)");
        source.Should().Contain("_startColorBox.TextChanged += (_, _) => SyncGradientTextFromInputs()");
        source.Should().Contain("_endColorBox.TextChanged += (_, _) => SyncGradientTextFromInputs()");
        source.Should().Contain("UpdateColorText()");
    }

    [Fact]
    public void HyperlinkDialog_ExposesExcelLikeLinkTypeAndScreenTipAffordances()
    {
        var source = string.Join(
            Environment.NewLine,
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "HyperlinkDialog.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "TextEntryDialogs.cs")));

        source.Should().Contain("Existing File or Web Page");
        source.Should().Contain("Create New Document");
        source.Should().Contain("Place in This Document");
        source.Should().Contain("E-mail Address");
        source.Should().Contain("_screenTipButton");
        source.Should().Contain("_bookmarkButton");
        source.Should().Contain("Content = \"_ScreenTip...\"");
        source.Should().Contain("Content = \"_Bookmark...\"");
        source.Should().Contain("ScreenTipDialog");
        source.Should().Contain("BookmarkDialog");
        source.Should().Contain("_screenTipButton.Click +=");
        source.Should().Contain("_bookmarkButton.Click +=");
    }

    [Fact]
    public void HyperlinkDialog_LabelsLinkTypeListWithAccessKeyTarget()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "HyperlinkDialog.cs"));

        source.Should().Contain("new Label { Content = \"Link _to:\", Target = _linkTypes");
        source.Should().Contain("AutomationProperties.SetName(_linkTypes, \"Link to\");");
    }

    [Fact]
    public void HyperlinkDialog_TextEditorsExposeAutomationNames()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "HyperlinkDialog.cs"));

        source.Should().Contain("AutomationProperties.SetName(_displayBox, \"Text to display\");");
        source.Should().Contain("AutomationProperties.SetName(_targetBox, automationName);");
    }

    [Theory]
    [InlineData(0, "_Address:", "Address", "Enter the file path or web page address for the hyperlink.")]
    [InlineData(1, "Name of new _document:", "Name of new document", "Enter the name of the new document to create.")]
    [InlineData(2, "Type the cell _reference:", "Cell reference or defined name", "Enter a cell reference or defined name in this workbook.")]
    [InlineData(3, "_E-mail address:", "E-mail address", "Enter the email address for the hyperlink.")]
    public void HyperlinkDialog_TargetFieldTracksSelectedLinkType(
        int selectedIndex,
        string expectedLabel,
        string expectedAutomationName,
        string expectedHelpText)
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new HyperlinkDialog();
            try
            {
                var linkTypes = GetField<ListBox>(dialog, "_linkTypes");
                var targetLabel = GetField<Label>(dialog, "_targetLabel");
                var targetBox = GetField<TextBox>(dialog, "_targetBox");

                linkTypes.SelectedIndex = selectedIndex;

                targetLabel.Content.Should().Be(expectedLabel);
                targetLabel.Target.Should().BeSameAs(targetBox);
                AutomationProperties.GetName(targetBox).Should().Be(expectedAutomationName);
                AutomationProperties.GetHelpText(targetBox).Should().Be(expectedHelpText);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void HyperlinkDialog_ScreenTipAndBookmarkButtonsExposeAutomationMetadata()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "HyperlinkDialog.cs"));

        source.Should().Contain("AutomationProperties.SetName(_screenTipButton, \"Set ScreenTip\");");
        source.Should().Contain("AutomationProperties.SetHelpText(_screenTipButton, \"Set the text shown when pointing to the hyperlink.\");");
        source.Should().Contain("AutomationProperties.SetName(_bookmarkButton, \"Select place in document\");");
        source.Should().Contain("AutomationProperties.SetHelpText(_bookmarkButton, \"Choose a bookmark, defined name, or cell reference in this workbook.\");");
    }

    [Fact]
    public void HyperlinkTextEntryDialogs_NameEntryBoxFromAccessKeyLabel()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "TextEntryDialogs.cs"));

        source.Should().Contain("AutomationProperties.SetName(_textBox, CreateAutomationName(label));");
        source.Should().Contain("label.Replace(\"_\", string.Empty, StringComparison.Ordinal)");
        source.Should().Contain(".Replace(\":\", string.Empty, StringComparison.Ordinal)");
    }

    [Fact]
    public void HyperlinkTextEntryDialogs_ExposeStableAutomationIdsAndHelpText()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "TextEntryDialogs.cs"));

        source.Should().Contain("AutomationProperties.SetAutomationId(_textBox, CreateAutomationId(title));");
        source.Should().Contain("AutomationProperties.SetHelpText(_textBox, CreateHelpText(label));");
        source.Should().Contain("string.Concat(title.Where(char.IsLetterOrDigit)) + \"TextBox\"");
        source.Should().Contain("$\"Enter {CreateAutomationName(label).ToLowerInvariant()}.\"");
    }

    [Fact]
    public void HyperlinkDialog_AcceptWarnsAndRefocusesBlankTarget()
    {
        var source = ReadClassSource("HyperlinkDialog.cs", "public sealed class HyperlinkDialog", "");

        source.Should().Contain("DialogButtonRowFactory.Create(Accept, 72)");
        source.Should().Contain("if (!TryCreateResult(_targetBox.Text, _displayBox.Text, SelectedLinkType, _screenTip, _bookmark, out var result, out var error))");
        source.Should().Contain("ShowInvalidInputWarning(error ?? \"Enter hyperlink details.\");");
        source.Should().Contain("DialogMessageHelper.ShowWarning(this, message, Title);");
        source.Should().Contain("_targetBox.Focus();");
        source.Should().Contain("_targetBox.SelectAll();");
        source.Should().Contain("Keyboard.Focus(_targetBox);");
    }

    [Fact]
    public void HyperlinkDialogOpenedFromKeyboard_FocusesAddressBox()
    {
        var source = ReadClassSource("HyperlinkDialog.cs", "public sealed class HyperlinkDialog", "");

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("DialogFocus.FocusAndSelect(_targetBox);");
    }

    [Fact]
    public void TextEntryDialog_CreateResult_TrimsNullToEmptyText()
    {
        TextEntryDialog.CreateResult(null).Text.Should().Be("");
        TextEntryDialog.CreateResult("  keep spacing inside  ").Text.Should().Be("keep spacing inside");
    }

    [Fact]
    public void ThreadedCommentDialog_CreateResult_DistinguishesRootEditFromReply()
    {
        var existing = new ThreadedComment("Old root", "Quinn")
        {
            Replies = [new CommentReply("Existing reply", "Codex")]
        };

        ThreadedCommentDialog.CreateResult(null, "  New root  ", "", isResolved: false)
            .Should()
            .Be(new ThreadedCommentDialogResult(null, "New root", false));
        ThreadedCommentDialog.CreateResult(existing, "  Edited root  ", "  Reply text  ", isResolved: true)
            .Should()
            .Be(new ThreadedCommentDialogResult("Edited root", "Reply text", true));
        ThreadedCommentDialog.CreateResult(existing, " Old root ", " ", isResolved: false)
            .Should()
            .Be(new ThreadedCommentDialogResult(null, null, false));
    }

    [Fact]
    public void ThreadedCommentDialog_TryCreateResult_RejectsBlankNewComment()
    {
        ThreadedCommentDialog.TryCreateResult(null, " ", "", isResolved: false, out _, out var error)
            .Should()
            .BeFalse();

        error.Should().Be("Enter a comment.");
    }

    [Fact]
    public void ThreadedCommentDialog_TryCreateResult_AllowsBlankReplyWhenResolvingExistingThread()
    {
        var existing = new ThreadedComment("Old root", "Quinn");

        ThreadedCommentDialog.TryCreateResult(existing, " Old root ", " ", isResolved: true, out var result, out var error)
            .Should()
            .BeTrue(error);

        result.Should().Be(new ThreadedCommentDialogResult(null, null, true));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void ThreadedCommentDialog_TryCreateResult_RejectsBlankExistingRootEdit(string rootText)
    {
        var existing = new ThreadedComment("Old root", "Quinn");

        ThreadedCommentDialog.TryCreateResult(existing, rootText, "Reply", isResolved: false, out _, out var error)
            .Should()
            .BeFalse();

        error.Should().Be("Enter a comment.");
    }

    [Fact]
    public void ThreadedCommentDialog_BlankNewCommentWarnsAndRefocusesCommentBox()
    {
        var source = ReadClassSource("ThreadedCommentDialog.cs", "public sealed class ThreadedCommentDialog", "");

        source.Should().Contain("if (!TryCreateResult(existing, _rootBox.Text, _replyBox.Text, _resolveBox.IsChecked == true, out var result, out var error))");
        source.Should().Contain("ShowInvalidThreadedCommentWarning(error ?? \"Enter a comment.\", _rootBox);");
        source.Should().Contain("DialogMessageHelper.ShowWarning(this, message, Title);");
        source.Should().Contain("target.Focus();");
        source.Should().Contain("target.SelectAll();");
        source.Should().Contain("Keyboard.Focus(target);");
    }

    [Fact]
    public void TextEntryDialogOpenedFromKeyboard_FocusesTextBox()
    {
        var source = ReadClassSource("TextEntryDialogs.cs", "public class TextEntryDialog", "");

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("DialogFocus.FocusAndSelect(_textBox);");
    }

    private static string ReadObjectDialogSources() =>
        string.Join(
            Environment.NewLine,
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "HyperlinkDialog.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "TextEntryDialogs.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ThreadedCommentDialog.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ObjectSizingDialogs.cs")));

    private static string ReadClassSource(string fileName, string startMarker, string endMarker)
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", fileName));
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = endMarker.Length == 0 ? source.Length : source.IndexOf(endMarker, start, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);
        if (end < 0)
            end = source.Length;
        end.Should().BeGreaterThan(start);
        return source[start..end];
    }

    private static T GetField<T>(object instance, string name)
        where T : class
    {
        var field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        return field!.GetValue(instance).Should().BeOfType<T>().Subject;
    }
}
