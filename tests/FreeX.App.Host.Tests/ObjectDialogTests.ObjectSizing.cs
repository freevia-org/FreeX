using System.IO;
using System.Windows.Automation;
using System.Windows.Controls;
using FluentAssertions;
using FreeX.App.Presentation.DrawingUI;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class ObjectDialogTests
{
    [Fact]
    public void ObjectSizeDialog_TryParseSize_AcceptsExcelLikeWidthByHeightText()
    {
        ObjectSizeDialog.TryParseSize("320 x 180", out var size).Should().BeTrue();

        size.Should().Be(new ObjectSizeDialogSize(320, 180));
    }

    [Theory]
    [InlineData("NaNx180")]
    [InlineData("320xInfinity")]
    [InlineData("-1x180")]
    [InlineData("320x0")]
    public void ObjectSizeDialog_TryParseSize_RejectsNonFiniteAndNonPositiveSizes(string input)
    {
        ObjectSizeDialog.TryParseSize(input, out var size).Should().BeFalse();

        size.Should().Be(default(ObjectSizeDialogSize));
    }

    [Fact]
    public void ObjectSizeDialog_ExposesExcelLikeWidthHeightAndAspectRatioControls()
    {
        var source = ReadObjectDialogSources();
        var objectSizeSource = source[
            source.IndexOf("public sealed class ObjectSizeDialog", StringComparison.Ordinal)..
            source.IndexOf("public sealed class RotationDialog", StringComparison.Ordinal)];

        objectSizeSource.Should().Contain("_widthBox");
        objectSizeSource.Should().Contain("_heightBox");
        objectSizeSource.Should().Contain("UiText.Get(\"ObjectSizing_HeightLabel\")");
        objectSizeSource.Should().Contain("UiText.Get(\"ObjectSizing_WidthLabel\")");
        objectSizeSource.Should().Contain("new Label { Content = label, Target = box");
        objectSizeSource.Should().Contain("_lockAspectRatioBox");
        objectSizeSource.Should().Contain("Content = UiText.Get(\"ObjectSizing_LockAspectRatio\")");
        objectSizeSource.Should().Contain("CalculateLockedAspectHeight");
        objectSizeSource.Should().Contain("CalculateLockedAspectWidth");
    }

    [Fact]
    public void ObjectSizeDialog_SizeControlsExposeAutomationMetadata()
    {
        var source = ReadClassSource("ObjectSizingDialogs.cs", "public sealed class ObjectSizeDialog", "public sealed class RotationDialog");

        source.Should().Contain("AutomationProperties.SetName(_heightBox, UiText.Get(\"ObjectSizing_ObjectHeight\"));");
        source.Should().Contain("AutomationProperties.SetAutomationId(_heightBox, \"ObjectSizeHeightBox\");");
        source.Should().Contain("AutomationProperties.SetHelpText(_heightBox, UiText.Get(\"ObjectSizing_EnterTheObjectSHeight\"));");
        source.Should().Contain("AutomationProperties.SetName(_widthBox, UiText.Get(\"ObjectSizing_ObjectWidth\"));");
        source.Should().Contain("AutomationProperties.SetAutomationId(_widthBox, \"ObjectSizeWidthBox\");");
        source.Should().Contain("AutomationProperties.SetHelpText(_widthBox, UiText.Get(\"ObjectSizing_EnterTheObjectSWidth\"));");
        source.Should().Contain("AutomationProperties.SetName(_lockAspectRatioBox, UiText.Get(\"ObjectSizing_LockAspectRatio2\"));");
        source.Should().Contain("AutomationProperties.SetAutomationId(_lockAspectRatioBox, \"ObjectSizeLockAspectRatioCheckBox\");");
        source.Should().Contain("AutomationProperties.SetHelpText(_lockAspectRatioBox, UiText.Get(\"ObjectSizing_KeepTheObjectSWidthAndHeightProportional\"));");
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
        var source = ReadClassSource("ObjectSizingDialogs.cs", "public sealed class ObjectSizeDialog", "public sealed class RotationDialog");

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("DialogFocus.FocusAndSelect(_heightBox);");
    }

    [Fact]
    public void ObjectSizeDialogInvalidSize_ShowsOwnedWarningAndRefocusesInvalidSizeInput()
    {
        var source = ReadClassSource("ObjectSizingDialogs.cs", "public sealed class ObjectSizeDialog", "public sealed class RotationDialog");

        source.Should().Contain("DialogMessageHelper.ShowWarning(this,");
        source.Should().Contain("UiText.Get(\"ObjectSizing_EnterPositiveWidthAndHeightValues\")");
        source.Should().Contain("FocusInvalidSizeInput(invalidField == ObjectSizeDialogField.Height ? _heightBox : _widthBox);");
        source.Should().Contain("ObjectSizeDialogPlanner.TryCreateSize(");
        source.Should().Contain("new ObjectSizeDialogSubmission(_widthBox.Text, _heightBox.Text, _sizeState.FirstInvalidField)");
        source.Should().Contain("_sizeState.FirstInvalidField");
        source.Should().NotContain("private TextBox ResolveInvalidSizeInput()");
        source.Should().NotContain("private static bool TryParsePositiveSize(string text)");
        source.Should().Contain("private static void FocusInvalidSizeInput(TextBox textBox)");
        source.Should().Contain("DialogFocus.FocusAndSelect(textBox);");
    }

    [Fact]
    public void ObjectSizeDialog_RoutesParsingAndAspectMathThroughSharedPlanner()
    {
        var source = ReadClassSource("ObjectSizingDialogs.cs", "public sealed class ObjectSizeDialog", "public sealed class RotationDialog");

        source.Should().Contain("ObjectSizeDialogPlanner.CreateState(");
        source.Should().Contain("ObjectSizeDialogPlanner.TryCreateDelimitedSize(input");
        source.Should().Contain("ObjectSizeDialogPlanner.TryCreateSize(");
        source.Should().Contain("new ObjectSizeDialogSubmission(");
        source.Should().Contain("ObjectSizeDialogPlanner.SyncHeightFromWidth(");
        source.Should().Contain("ObjectSizeDialogPlanner.SyncWidthFromHeight(");
        source.Should().Contain("ObjectSizeDialogPlanner.FormatSize(value");
        source.Should().NotContain("DrawingInputParser.TryParseSize(input");
        source.Should().NotContain("ObjectSizeDialogResult");
    }

    [Fact]
    public void RotationDialog_TryParseRotation_AcceptsNumericDegrees()
    {
        RotationDialog.TryParseRotation("45.5", out var rotation).Should().BeTrue();

        rotation.Should().Be(new FormatPicturePlanner.RotationResult(45.5));
    }

    [Theory]
    [InlineData("450", 90)]
    [InlineData("-90", 270)]
    [InlineData("720", 0)]
    public void RotationDialog_TryParseRotation_NormalizesExcelFullTurnDegrees(string input, double expectedDegrees)
    {
        RotationDialog.TryParseRotation(input, out var rotation).Should().BeTrue();

        rotation.Should().Be(new FormatPicturePlanner.RotationResult(expectedDegrees));
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    public void RotationDialog_TryParseRotation_RejectsNonFiniteDegrees(string input)
    {
        RotationDialog.TryParseRotation(input, out var rotation).Should().BeFalse();

        rotation.Should().Be(new FormatPicturePlanner.RotationResult(0));
    }

    [Fact]
    public void RotationDialogOpenedFromKeyboard_FocusesDegreesInput()
    {
        var source = ReadClassSource("ObjectSizingDialogs.cs", "public sealed class RotationDialog", "public sealed class PictureCropDialog");

        source.Should().Contain("ObjectSizeDialog.CreateSingleInputContent(UiText.Get(\"ObjectSizing_Degrees\"), _rotationBox, Accept)");
        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("DialogFocus.FocusAndSelect(_rotationBox);");
        source.Should().Contain("NormalizeRotationDegrees(value)");
    }

    [Fact]
    public void RotationDialog_DegreesInputExposesAutomationMetadata()
    {
        var source = ReadClassSource("ObjectSizingDialogs.cs", "public sealed class RotationDialog", "public sealed class PictureCropDialog");

        source.Should().Contain("AutomationProperties.SetName(_rotationBox, UiText.Get(\"ObjectSizing_RotationDegrees\"));");
        source.Should().Contain("AutomationProperties.SetAutomationId(_rotationBox, \"RotationDegreesBox\");");
        source.Should().Contain("AutomationProperties.SetHelpText(_rotationBox, UiText.Get(\"ObjectSizing_EnterTheObjectSRotationInDegrees\"));");
    }

    [Fact]
    public void RotationDialogInvalidDegrees_ShowsOwnedWarningAndRefocusesInput()
    {
        var source = ReadClassSource("ObjectSizingDialogs.cs", "public sealed class RotationDialog", "public sealed class PictureCropDialog");

        source.Should().Contain("DialogMessageHelper.ShowWarning(this,");
        source.Should().Contain("UiText.Get(\"ObjectSizing_EnterANumericRotationValue\")");
        source.Should().Contain("FocusInvalidRotationInput();");
        source.Should().Contain("private void FocusInvalidRotationInput()");
        source.Should().Contain("DialogFocus.FocusAndSelect(_rotationBox);");
    }

    [Fact]
    public void RotationDialog_RoutesParsingThroughSharedPlanner()
    {
        var source = ReadClassSource("ObjectSizingDialogs.cs", "public sealed class RotationDialog", "public sealed class PictureCropDialog");

        source.Should().Contain("FormatPicturePlanner.TryCreateRotationResult(input");
        source.Should().Contain("FormatPicturePlanner.NormalizeRotationDegrees(value)");
        source.Should().NotContain("DrawingInputParser.TryParseRotationDegrees(input");
        source.Should().NotContain("RotationDialogResult");
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

        result.Should().Be(new PictureCropDialogPlanner.CropResult(0.10, 0.05, 0, 0.20));

        ReadObjectDialogSources().Should().NotContain("PictureCropDialogResult");
    }

    [Fact]
    public void PictureCropDialog_ExposesSeparateExcelCropEdgeFields()
    {
        var source = ReadObjectDialogSources();

        source.Should().Contain("_cropLeftBox");
        source.Should().Contain("_cropTopBox");
        source.Should().Contain("_cropRightBox");
        source.Should().Contain("_cropBottomBox");
        source.Should().Contain("UiText.Get(\"ObjectSizing_LeftLabel\")");
        source.Should().Contain("UiText.Get(\"ObjectSizing_TopLabel\")");
        source.Should().Contain("UiText.Get(\"ObjectSizing_RightLabel\")");
        source.Should().Contain("UiText.Get(\"ObjectSizing_BottomLabel\")");
        source.Should().Contain("new Label { Content = label, Target = box");
    }

    [Fact]
    public void PictureCropDialog_CropFieldsExposeAutomationMetadata()
    {
        var source = ReadClassSource("ObjectSizingDialogs.cs", "public sealed class PictureCropDialog", "");

        source.Should().Contain("AutomationProperties.SetName(_cropLeftBox, UiText.Get(\"ObjectSizing_CropLeft\"));");
        source.Should().Contain("AutomationProperties.SetAutomationId(_cropLeftBox, \"PictureCropLeftBox\");");
        source.Should().Contain("AutomationProperties.SetHelpText(_cropLeftBox, UiText.Get(\"ObjectSizing_EnterTheLeftCropPercentage\"));");
        source.Should().Contain("AutomationProperties.SetName(_cropTopBox, UiText.Get(\"ObjectSizing_CropTop\"));");
        source.Should().Contain("AutomationProperties.SetAutomationId(_cropTopBox, \"PictureCropTopBox\");");
        source.Should().Contain("AutomationProperties.SetHelpText(_cropTopBox, UiText.Get(\"ObjectSizing_EnterTheTopCropPercentage\"));");
        source.Should().Contain("AutomationProperties.SetName(_cropRightBox, UiText.Get(\"ObjectSizing_CropRight\"));");
        source.Should().Contain("AutomationProperties.SetAutomationId(_cropRightBox, \"PictureCropRightBox\");");
        source.Should().Contain("AutomationProperties.SetHelpText(_cropRightBox, UiText.Get(\"ObjectSizing_EnterTheRightCropPercentage\"));");
        source.Should().Contain("AutomationProperties.SetName(_cropBottomBox, UiText.Get(\"ObjectSizing_CropBottom\"));");
        source.Should().Contain("AutomationProperties.SetAutomationId(_cropBottomBox, \"PictureCropBottomBox\");");
        source.Should().Contain("AutomationProperties.SetHelpText(_cropBottomBox, UiText.Get(\"ObjectSizing_EnterTheBottomCropPercentage\"));");
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
        source.Should().Contain("error ?? UiText.Get(\"ObjectSizing_EnterFourCropPercentages\")");
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
    public void PictureCropDialog_RoutesParsingThroughSharedPlanner()
    {
        var source = ReadClassSource("ObjectSizingDialogs.cs", "public sealed class PictureCropDialog", "");

        source.Should().Contain("PictureCropDialogPlanner.TryCreateResult(input");
        source.Should().Contain("PictureCropDialogPlanner.TryParsePercent(_cropLeftBox.Text");
        source.Should().NotContain("DrawingInputParser.TryParseCropPercents(input");
    }
}
