using System.IO;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Source contracts for the WPF features of the picture format and object size dialogs that the
/// Avalonia shell carries: the crop page, the two reset buttons and the lock-aspect check box.
/// </summary>
public sealed class PictureObjectDialogFeatureParitySourceTests
{
    [Fact]
    public void FormatPictureDialog_HasCropPageBetweenSizeAndAltTextPages()
    {
        var source = ReadDrawingFormatDialogs();

        source.Should().Contain("UiText.Get(\"FormatPicture_CropTab\")");
        source.Should().Contain("UiText.Get(\"FormatPicture_LeftLabel\")");
        source.Should().Contain("UiText.Get(\"FormatPicture_TopLabel\")");
        source.Should().Contain("UiText.Get(\"FormatPicture_RightLabel\")");
        source.Should().Contain("UiText.Get(\"FormatPicture_BottomLabel\")");
        source.Should().Contain("UiText.Get(\"FormatPicture_CropUnavailableMessage\")");
        source.Should().Contain("PictureCropDialogPlanner.Capture(picture)");
        source.Should().Contain("IsEnabled = initialCrop.IsCroppable");

        var sizeTab = source.IndexOf("tabs.Items.Add(sizeTab);", StringComparison.Ordinal);
        var cropTab = source.IndexOf("tabs.Items.Add(cropTab);", StringComparison.Ordinal);
        var altTextTab = source.IndexOf("tabs.Items.Add(altTextTab);", StringComparison.Ordinal);
        sizeTab.Should().BeGreaterThanOrEqualTo(0);
        cropTab.Should().BeGreaterThan(sizeTab);
        altTextTab.Should().BeGreaterThan(cropTab);
    }

    [Fact]
    public void FormatPictureDialog_HasResetSizeAndResetCropButtonsInTheEditorColumn()
    {
        var source = ReadDrawingFormatDialogs();

        source.Should().Contain("UiText.Get(\"FormatPicture_ResetSizeButton\")");
        source.Should().Contain("UiText.Get(\"FormatPicture_ResetCropButton\")");
        source.Should().Contain("AvaloniaCompactDialogChrome.ApplyButton(resetSizeButton, DrawingDialogChromeStyle, 96);");
        source.Should().Contain("AvaloniaCompactDialogChrome.ApplyButton(resetCropButton, DrawingDialogChromeStyle, 96);");
        source.Should().Contain("Grid.SetRow(resetSizeButton, 4);");
        source.Should().Contain("Grid.SetColumn(resetSizeButton, 1);");
        source.Should().Contain("Grid.SetRow(resetCropButton, 4);");
        source.Should().Contain("Grid.SetColumn(resetCropButton, 1);");
        source.Should().Contain("lockAspectBox.IsChecked = state.LockAspectRatio;");
        source.Should().Contain("RowDefinitions = new RowDefinitions(\"Auto,Auto,Auto,Auto,Auto,Auto\")");
    }

    [Fact]
    public void FormatPictureDialog_ValidatesAndAppliesCropThroughTheSharedPicturePath()
    {
        var source = ReadDrawingFormatDialogs();

        source.Should().Contain("FormatPicturePlanner.TryCreatePictureResult(");
        source.Should().Contain("DrawingObjectFormatCommandPolicy.BuildPictureFormatCommand(");
        source.Should().Contain("UiText.Get(\"MainWindowMessage_PictureWasNotFound\")");
        source.Should().Contain("UiText.Get(\"FormatPicture_InvalidCropPercentagesMessage\")");
        source.Should().Contain("UiText.Get(\"FormatPicture_InvalidRotationMessage\")");
        source.Should().Contain("UiText.Get(\"FormatPicture_InvalidSizeMessage\")");
        source.Should().NotContain("new SetPictureCropCommand(");

        // A page that is not selected cannot take focus before layout has run.
        var select = source.IndexOf("tabs.SelectedItem = tab;", StringComparison.Ordinal);
        var layout = source.IndexOf("dialog.UpdateLayout();", select, StringComparison.Ordinal);
        var focus = source.IndexOf("AvaloniaCompactDialogChrome.FocusAndSelect(editor);", select, StringComparison.Ordinal);
        select.Should().BeGreaterThanOrEqualTo(0);
        layout.Should().BeGreaterThan(select);
        focus.Should().BeGreaterThan(layout);
    }

    [Fact]
    public void ObjectSizeDialog_HasLockAspectRatioCheckBoxUnderTheWidthEditor()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.PictureShapeTabs.cs"));

        source.Should().Contain("UiText.Get(\"ObjectSizing_LockAspectRatio\")");
        source.Should().Contain("UiText.Get(\"ObjectSizing_LockAspectRatio2\")");
        source.Should().Contain("UiText.Get(\"ObjectSizing_KeepTheObjectSWidthAndHeightProportional\")");
        source.Should().Contain("AutomationProperties.SetAutomationId(lockAspectRatioBox, \"ObjectSizeLockAspectRatioCheckBox\");");
        source.Should().Contain("IsChecked = state.LockAspectRatio,");
        source.Should().Contain("ObjectSizeDialogPlanner.SyncHeightFromWidth(widthBox.Text, state.OriginalSize)");
        source.Should().Contain("ObjectSizeDialogPlanner.SyncWidthFromHeight(heightBox.Text, state.OriginalSize)");

        var width = source.IndexOf("                widthBox,", StringComparison.Ordinal);
        var lockBox = source.IndexOf("                lockAspectRatioBox,", StringComparison.Ordinal);
        var buttons = source.IndexOf(
            "AvaloniaCompactDialogChrome.CreateActionRow([okButton, cancelButton]),",
            lockBox,
            StringComparison.Ordinal);
        width.Should().BeGreaterThanOrEqualTo(0);
        lockBox.Should().BeGreaterThan(width);
        buttons.Should().BeGreaterThan(lockBox);
    }

    private static string ReadDrawingFormatDialogs() =>
        File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.DrawingFormatDialogs.cs"));

    private static string RepoFile(params string[] parts) =>
        Path.Combine([TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx"), .. parts]);
}
