using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using FluentAssertions;
using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.Charts.Editing;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class ChartDialogTests
{
    [Fact]
    public void ChartTitlesDialog_UsesSharedInputAndPlanner()
    {
        var result = ChartTitlesPlanner.Normalize(new ChartTitlesInput(" Revenue ", " Quarter ", " Amount "));

        result.Should().Be(new ChartTitlesInput("Revenue", "Quarter", "Amount"));
        ChartTitlesPlanner.Plan(ChartType.Column, result).Should().Be(new ChartLayoutOptions(
            Title: "Revenue",
            XAxisTitle: "Quarter",
            YAxisTitle: "Amount"));
    }

    [Fact]
    public void ChartTitlesDialog_LabelsTitleEditorsWithExcelAccessKeys()
    {
        var source = ReadChartDialogSource();

        source.Should().Contain("AddInput(stack, UiText.Get(\"ChartTitles_ChartTitleLabel\"), _chartTitleBox)");
        source.Should().Contain("AddInput(stack, UiText.Get(\"ChartTitles_XAxisTitleLabel\"), _xAxisTitleBox)");
        source.Should().Contain("AddInput(stack, UiText.Get(\"ChartTitles_YAxisTitleLabel\"), _yAxisTitleBox)");
        source.Should().Contain("new Label { Content = label, Target = box");
    }

    [Fact]
    public void ChartTitlesDialog_EditorsExposeAutomationNames()
    {
        var source = ReadChartDialogSource();
        var dialogSource = source[
            source.IndexOf("public sealed class ChartTitlesDialog", StringComparison.Ordinal)..
            source.IndexOf("public sealed class ChartStyleDialog", StringComparison.Ordinal)];

        dialogSource.Should().Contain("AutomationProperties.SetName(_chartTitleBox, UiText.Get(\"ChartTitles_ChartTitleAutomationName\"));");
        dialogSource.Should().Contain("AutomationProperties.SetName(_xAxisTitleBox, UiText.Get(\"ChartTitles_XAxisTitleAutomationName\"));");
        dialogSource.Should().Contain("AutomationProperties.SetName(_yAxisTitleBox, UiText.Get(\"ChartTitles_YAxisTitleAutomationName\"));");
    }

    [Fact]
    public void ChartTitlesDialogOpenedFromKeyboard_FocusesChartTitleBox()
    {
        var source = ReadChartDialogSource();
        var dialogSource = source[
            source.IndexOf("public sealed class ChartTitlesDialog", StringComparison.Ordinal)..
            source.IndexOf("public sealed class ChartStyleDialog", StringComparison.Ordinal)];

        dialogSource.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        dialogSource.Should().Contain("private void FocusInitialKeyboardTarget()");
        dialogSource.Should().Contain("_chartTitleBox.Focus();");
        dialogSource.Should().Contain("_chartTitleBox.SelectAll();");
        dialogSource.Should().Contain("Keyboard.Focus(_chartTitleBox);");
    }

    [Fact]
    public void ChartStyleDialog_ExposesAutomaticAndCommonStyleOptions()
    {
        var options = ChartStyleDialog.GetStyleOptions();

        options.Should().HaveCount(49);
        options[0].Should().Be(new ChartStyleOption(null, "Automatic", "Use current chart formatting"));
        options.Skip(1).Select(option => option.StyleId).Should().Equal(Enumerable.Range(1, 48).Cast<int?>());
        options.Skip(1).Should().OnlyContain(option => !string.IsNullOrWhiteSpace(option.PreviewLabel));

        var source = ReadChartDialogSource();
        source.Should().Contain("ChartStylePlanner.GetStyleOptions()");
        source.Should().Contain("public ChartStyleInput Result { get; private set; }");
        source.Should().Contain("ChartStylePlanner.CreateResult(option.StyleId)");
        source.Should().NotContain("Enumerable.Range(1, 48).Select");
        source.Should().NotContain("Math.Clamp(value.Value, 1, 48)");
    }

    [Fact]
    public void ChartStyleDialog_UsesVisualGalleryInsteadOfPlainStyleCombo()
    {
        var source = ReadChartDialogSource();

        source.Should().Contain("UiText.Get(\"ChartStyle_GalleryAutomationName\")");
        source.Should().Contain("CreateStyleGalleryTemplate");
        source.Should().Contain("CreateStylePreviewSwatch");
        source.Should().Contain("UniformGrid");
        source.Should().NotContain("private readonly ComboBox _styleBox");
    }

    [Fact]
    public void ChartStyleDialog_ResultNormalizesCurrentAndSelectedStyle()
    {
        var chart = new ChartModel { ChartStyleId = 99 };

        ChartStylePlanner.Read(chart).Should().Be(new ChartStyleInput(48));
        ChartStylePlanner.CreateResult(0).Should().Be(new ChartStyleInput(1));
        ChartStylePlanner.CreateResult(null).Should().Be(new ChartStyleInput(null));
    }

    [Fact]
    public void ChartStyleDialogOpenedFromKeyboard_FocusesStyleGallery()
    {
        var source = ReadChartDialogSource();
        var dialogSource = source[
            source.IndexOf("public sealed class ChartStyleDialog", StringComparison.Ordinal)..
            source.IndexOf("public sealed record ChartStyleOption", StringComparison.Ordinal)];

        dialogSource.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        dialogSource.Should().Contain("private void FocusInitialKeyboardTarget()");
        dialogSource.Should().Contain("_styleGallery.Focus();");
        dialogSource.Should().Contain("Keyboard.Focus(_styleGallery);");
    }

    [Fact]
    public void MoveChartDialog_CreatesObjectAndNewSheetResults()
    {
        MoveChartDialog.CreateObjectResult("Sheet2").Should().Be(
            new ChartMoveInput(ChartMoveTargetKind.ObjectInSheet, "Sheet2"));
        MoveChartDialog.CreateNewSheetResult("Revenue Chart").Should().Be(
            new ChartMoveInput(ChartMoveTargetKind.NewSheet, "Revenue Chart"));
    }

    [Fact]
    public void MoveChartDialogOpenedFromKeyboard_FocusesObjectInSheetChoice()
    {
        var source = ReadChartDialogSource();
        var dialogSource = source[
            source.IndexOf("public sealed class MoveChartDialog", StringComparison.Ordinal)..
            source.IndexOf("public sealed record SelectDataSourceDialogResult", StringComparison.Ordinal)];

        dialogSource.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        dialogSource.Should().Contain("private void FocusInitialKeyboardTarget()");
        dialogSource.Should().Contain("_objectInSheet.Focus();");
        dialogSource.Should().Contain("Keyboard.Focus(_objectInSheet);");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MoveChartDialog_RejectsMissingTargetName(string? targetName)
    {
        var act = () => MoveChartDialog.CreateNewSheetResult(targetName);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void MoveChartDialogInvalidTargetName_ShowsOwnedWarningAndRefocusesTargetBox()
    {
        var source = ReadChartDialogSource();
        var dialogSource = source[
            source.IndexOf("public sealed class MoveChartDialog", StringComparison.Ordinal)..
            source.IndexOf("public sealed record SelectDataSourceDialogResult", StringComparison.Ordinal)];

        dialogSource.Should().Contain("catch (ArgumentException ex)");
        dialogSource.Should().Contain("DialogMessageHelper.ShowWarning(this, ex.Message, Title);");
        dialogSource.Should().Contain("FocusInvalidTargetName();");
        dialogSource.Should().Contain("_targetBox.Focus();");
        dialogSource.Should().Contain("_targetBox.SelectAll();");
        dialogSource.Should().Contain("Keyboard.Focus(_targetBox);");
    }

    [Fact]
    public void MoveChartDialog_LabelsTargetNameEditorWithAccessKeyAndAutomationName()
    {
        var source = ReadChartDialogSource();
        var dialogSource = source[
            source.IndexOf("public sealed class MoveChartDialog", StringComparison.Ordinal)..
            source.IndexOf("public sealed record SelectDataSourceDialogResult", StringComparison.Ordinal)];

        dialogSource.Should().Contain("var targetField = ChartMovePlanner.GetTargetNameField();");
        dialogSource.Should().Contain("new Label { Content = UiText.Get(targetField.LabelResourceKey), Target = _targetBox");
        dialogSource.Should().Contain("AutomationProperties.SetName(_targetBox, UiText.Get(targetField.AutomationNameResourceKey!));");
        dialogSource.Should().Contain("AutomationProperties.SetHelpText(_targetBox, UiText.Get(targetField.HelpResourceKey!));");
    }

    [Fact]
    public void ChartDataAndMoveDialogs_ExposeKeyboardAccessKeys()
    {
        var source = ReadChartDialogSource();

        source.Should().Contain("MoveTargetLabel(ChartMoveTargetKind.ObjectInSheet)");
        source.Should().Contain("MoveTargetLabel(ChartMoveTargetKind.NewSheet)");
        source.Should().Contain("ChartMovePlanner.GetTargetChoices()");
        source.Should().Contain("FieldLabel(SelectDataSourcePlanner.GetFirstColumnCategoriesField())");
        source.Should().Contain("FieldLabel(SelectDataSourcePlanner.GetSwitchRowColumnField())");
        source.Should().Contain("FieldLabel(rangeField)");
        source.Should().Contain("UiText.Get(action.LabelResourceKey)");
        source.Should().NotContain("ChartTitlesDialogResult");
        source.Should().NotContain("ChartStyleDialogResult");
        source.Should().NotContain("MoveChartDialogResult");
        source.Should().NotContain("public enum MoveChartTargetKind");
    }

}
