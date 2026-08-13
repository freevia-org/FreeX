using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class DataToolDialogTests
{
    [Fact]
    public void TextToColumnsDialog_ExposesDelimitedAndFixedWidthSplitChoices()
    {
        var source = ReadTextToColumnsDialogSources();
        var surfacePlannerSource = DialogSourceTestSupport.ReadPresentationSources(
            "TextToColumns",
            "TextToColumnsWizardSurfacePlanner.cs");

        source.Should().Contain("UiText.Get(\"TextToColumns_OriginalDataTypeGroup\")");
        source.Should().Contain("Content = UiText.Get(\"TextToColumns_Delimited\")");
        source.Should().Contain("Content = UiText.Get(\"TextToColumns_FixedWidth\")");
        source.Should().Contain("CreateFixedWidthResult");
        source.Should().Contain("ParseFixedWidthBreakPositions");
        surfacePlannerSource.Should().Contain("\"TextToColumns_ChooseDelimitersInstruction\"");
        source.Should().Contain("UiText.Get(\"TextToColumns_DelimitersGroup\")");
        source.Should().Contain("UiText.Get(\"TextToColumns_FixedWidth2\")");
        source.Should().Contain("_fixedWidthRuler");
        source.Should().Contain("MouseLeftButtonDown");
        source.Should().Contain("MouseMove");
        source.Should().Contain("MouseRightButtonDown");
        source.Should().Contain("UiText.Get(\"TextToColumns_ClickTheRulerToCreateABreakLineDragToMoveItOrRightClickALineToRemoveIt\")");
        source.Should().Contain("UiText.Get(\"TextToColumns_TextQualifierLabel\")");
        source.Should().Contain("UiText.Get(\"TextToColumns_TreatConsecutiveDelimitersAsOne\")");
        source.Should().Contain("UiText.Get(\"TextToColumns_DestinationLabel\")");
        source.Should().Contain("UiText.Get(\"TextToColumns_ColumnDataFormatGroup\")");
        source.Should().Contain("Content = UiText.Get(\"TextToColumns_General\")");
        source.Should().Contain("Content = UiText.Get(\"TextToColumns_Text\")");
        source.Should().Contain("Content = UiText.Get(\"TextToColumns_Date\")");
        source.Should().Contain("_dateFormatBox");
        source.Should().Contain("UiText.Get(\"TextToColumns_DoNotImportColumnSkip\")");
        source.Should().Contain("UiText.Get(\"TextToColumns_AdvancedGroup\")");
        source.Should().Contain("UiText.Get(\"TextToColumns_DecimalSeparatorLabel\")");
        source.Should().Contain("UiText.Get(\"TextToColumns_ThousandsSeparatorLabel\")");
        source.Should().Contain("UiText.Get(\"TextToColumns_TrailingMinusForNegativeNumbers\")");
        source.Should().Contain("TryParseAdvancedSeparator(_decimalSeparatorBox.Text, out _)");
        source.Should().Contain("TryParseAdvancedSeparator(_thousandsSeparatorBox.Text, out _)");
        source.Should().Contain("FocusInvalidAdvancedSeparatorInput(_decimalSeparatorBox);");
        source.Should().Contain("FocusInvalidAdvancedSeparatorInput(_thousandsSeparatorBox);");
    }

    [Fact]
    public void TextToColumnsDialog_UsesExcelWizardChromeAroundDelimitedFlow()
    {
        var source = ReadTextToColumnsDialogSources();
        var surfacePlannerSource = DialogSourceTestSupport.ReadPresentationSources(
            "TextToColumns",
            "TextToColumnsWizardSurfacePlanner.cs");

        source.Should().Contain("UiText.Format(TextToColumnsWizardSurfacePlanner.HeaderFormatKey, normalizedStep)");
        source.Should().Contain("TextToColumnsWizardSurfacePlanner.CreateStepPlan");
        source.Should().Contain("CreateWizardButtonRow");
        source.Should().Contain("Content = UiText.Get(\"TextToColumns_BackButton\")");
        source.Should().Contain("Content = UiText.Get(\"TextToColumns_NextButton\")");
        source.Should().Contain("Content = UiText.Get(\"TextToColumns_FinishButton\")");
        source.Should().Contain("MoveWizardStep");
        source.Should().Contain("UpdateWizardStep");
        source.Should().Contain("_backButton.IsEnabled = plan.BackEnabled");
        source.Should().Contain("_nextButton.IsEnabled = plan.NextEnabled");
        surfacePlannerSource.Should().Contain("\"TextToColumns_ChooseFileTypeInstruction\"");
        surfacePlannerSource.Should().Contain("NextDefault: normalizedStep < 3");
        surfacePlannerSource.Should().Contain("FinishDefault: normalizedStep == 3");
        source.Should().Contain("Accept()");
        source.Should().NotContain("Additional wizard steps are not supported yet.");
        source.Should().NotContain("This dialog opens on the split-options step.");
    }

    [Fact]
    public void TextToColumnsDialog_UsesExcelWizardDefaultButtonsPerStep()
    {
        var source = ReadTextToColumnsDialogSources();

        source.Should().Contain("private Button? _finishButton;");
        source.Should().Contain("_finishButton = new Button");
        source.Should().Contain("_nextButton.IsDefault = plan.NextDefault");
        source.Should().Contain("_finishButton.IsDefault = plan.FinishDefault");
    }

    [Fact]
    public void TextToColumnsDialogStepThreeLayout_FitsContentAndNavigationAtDefaultSize()
    {
        StaTestRunner.Run(() =>
        {
            var sheetId = SheetId.New();
            var dialog = new TextToColumnsDialog(
                [
                    "East,42,Open",
                    "West,7,Closed",
                    "North,18,Pending"
                ],
                new CellAddress(sheetId, 2, 6));
            dialog.Show();
            try
            {
                var next = WpfTestTree.FindVisualDescendants<Button>(dialog)
                    .Single(button => Equals(button.Content, "_Next >"));

                DialogSourceTestSupport.ClickButton(next);
                DialogSourceTestSupport.ClickButton(next);
                dialog.UpdateLayout();

                dialog.ResizeMode.Should().Be(ResizeMode.CanResizeWithGrip);
                var root = dialog.Content.Should().BeOfType<Grid>().Subject;
                root.RowDefinitions.Should().HaveCount(3);
                root.RowDefinitions[1].Height.GridUnitType.Should().Be(GridUnitType.Star);

                var bodyScroller = GetTextToColumnsField<ScrollViewer>(dialog, "_wizardBodyScrollViewer");
                var preview = GetTextToColumnsField<ListView>(dialog, "_previewGrid");
                var columnFormat = GetTextToColumnsField<FrameworkElement>(dialog, "_columnFormatPanel");
                var destination = GetTextToColumnsField<FrameworkElement>(dialog, "_destinationPanel");
                var back = GetTextToColumnsField<Button>(dialog, "_backButton");
                var nextButton = GetTextToColumnsField<Button>(dialog, "_nextButton");
                var finish = GetTextToColumnsField<Button>(dialog, "_finishButton");
                var cancel = WpfTestTree.FindVisualDescendants<Button>(dialog).Single(button => button.IsCancel);

                bodyScroller.ScrollableHeight.Should().BeLessThan(1);
                preview.ActualHeight.Should().BeGreaterThan(70);
                columnFormat.Visibility.Should().Be(Visibility.Visible);
                destination.Visibility.Should().Be(Visibility.Visible);

                var navigationButtons = new[] { back, nextButton, finish, cancel };
                var navigationTop = navigationButtons.Min(button => BoundsRelativeTo(root, button).Top);
                BoundsRelativeTo(root, destination).Bottom.Should().BeLessThanOrEqualTo(navigationTop + 0.5);

                foreach (var element in new FrameworkElement[] { bodyScroller, preview, columnFormat, destination, back, nextButton, finish, cancel })
                {
                    element.ActualWidth.Should().BeGreaterThan(0);
                    element.ActualHeight.Should().BeGreaterThan(0);
                    AssertInside(root, element);
                }
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void TextToColumnsDialogOpenedFromKeyboard_FocusesOriginalDataTypeChoice()
    {
        var source = ReadTextToColumnsDialogSources();

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("_delimitedButton.Focus();");
        source.Should().Contain("Keyboard.Focus(_delimitedButton);");
    }

    [Fact]
    public void TextToColumnsWizardNavigation_FocusesFirstControlOnNewStep()
    {
        StaTestRunner.Run(() =>
        {
            var sheetId = SheetId.New();
            var dialog = new TextToColumnsDialog(
                ["East,42"],
                new CellAddress(sheetId, 2, 6));
            dialog.Show();
            try
            {
                var next = WpfTestTree.FindVisualDescendants<Button>(dialog)
                    .Single(button => Equals(button.Content, "_Next >"));

                DialogSourceTestSupport.ClickButton(next);

                var tabDelimiter = WpfTestTree.FindVisualDescendants<CheckBox>(dialog)
                    .Single(checkBox => Equals(checkBox.Content, "_Tab"));
                Keyboard.FocusedElement.Should().BeSameAs(tabDelimiter);

                DialogSourceTestSupport.ClickButton(next);

                var columnSelector = WpfTestTree.FindVisualDescendants<ComboBox>(dialog)
                    .Single(comboBox => comboBox.Items.OfType<string>().Contains("Column 1"));
                Keyboard.FocusedElement.Should().BeSameAs(columnSelector);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void TextToColumnsDialogInvalidDestination_ReturnsToStepThreeAndFocusesDestination()
    {
        var source = ReadTextToColumnsDialogSources();

        source.Should().Contain("ShowValidation(TextToColumnsDialogValidationIssue.InvalidDestination);");
        source.Should().Contain("var presentation = TextToColumnsDialogPlanner.DescribeValidationIssue(issue);");
        source.Should().Contain("case TextToColumnsDialogFocusTarget.FixedWidthBreaks:");
        source.Should().Contain("default:");
        source.Should().Contain("FocusInvalidDestinationInput();");
        source.Should().Contain("private void FocusInvalidDestinationInput()");
        source.Should().Contain("_wizardStep = 3;");
        source.Should().Contain("UpdateWizardStep();");
        source.Should().Contain("DialogFocus.FocusAndSelect(_destinationBox);");
    }

    [Fact]
    public void TextToColumnsDialogInvalidFixedWidthBreaks_ReturnsToStepTwoAndFocusesBreaks()
    {
        var source = ReadTextToColumnsDialogSources();

        source.Should().Contain("TryParseFixedWidthBreakPositions(_fixedWidthBreaksBox.Text, FixedWidthMaxLength(), out _)");
        source.Should().Contain("ShowValidation(TextToColumnsDialogValidationIssue.MissingFixedWidthBreaks);");
        source.Should().Contain("FocusInvalidFixedWidthBreaksInput();");
        source.Should().Contain("case TextToColumnsDialogFocusTarget.FixedWidthBreaks:");
        source.Should().Contain("private void FocusInvalidFixedWidthBreaksInput()");
        source.Should().Contain("_wizardStep = 2;");
        source.Should().Contain("_fixedWidthButton.IsChecked = true;");
        source.Should().Contain("DialogFocus.FocusAndSelect(_fixedWidthBreaksBox);");
    }

    [Fact]
    public void TextToColumnsDialogInvalidCustomDelimiter_ReturnsToStepTwoAndFocusesOtherDelimiter()
    {
        var source = ReadTextToColumnsDialogSources();

        source.Should().Contain("ShowValidation(TextToColumnsDialogValidationIssue.MissingCustomDelimiter);");
        source.Should().Contain("case TextToColumnsDialogFocusTarget.CustomDelimiter:");
        source.Should().Contain("FocusInvalidCustomDelimiterInput();");
        source.Should().Contain("private void FocusInvalidCustomDelimiterInput()");
        source.Should().Contain("_wizardStep = 2;");
        source.Should().Contain("_delimitedButton.IsChecked = true;");
        source.Should().Contain("_otherBox.IsChecked = true;");
        source.Should().Contain("DialogFocus.FocusAndSelect(_customBox);");
    }

    [Fact]
    public void TextToColumnsDialogNoDelimiterSelected_ReturnsToStepTwoAndFocusesDelimiterGroup()
    {
        var source = ReadTextToColumnsDialogSources();

        source.Should().Contain("SelectedDelimiterKinds().Count == 0");
        source.Should().Contain("ShowValidation(TextToColumnsDialogValidationIssue.MissingDelimiter);");
        source.Should().Contain("case TextToColumnsDialogFocusTarget.DelimiterSelection:");
        source.Should().Contain("FocusInvalidDelimiterSelectionInput();");
        source.Should().Contain("private void FocusInvalidDelimiterSelectionInput()");
        source.Should().Contain("_wizardStep = 2;");
        source.Should().Contain("_delimitedButton.IsChecked = true;");
        source.Should().Contain("_tabBox.Focus();");
        source.Should().Contain("Keyboard.Focus(_tabBox);");
        source.Should().NotContain("return kinds.Count == 0 ? [TextToColumnsDelimiterKind.Comma] : kinds;");
    }
}
