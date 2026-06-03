using System.IO;
using System.Windows.Automation;
using System.Windows.Controls;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class ObjectDialogTests
{
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
}
