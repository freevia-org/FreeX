using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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

        source.Should().Contain("UiText.Get(\"TextToColumns_OriginalDataTypeGroup\")");
        source.Should().Contain("Content = UiText.Get(\"TextToColumns_Delimited\")");
        source.Should().Contain("Content = UiText.Get(\"TextToColumns_FixedWidth\")");
        source.Should().Contain("CreateFixedWidthResult");
        source.Should().Contain("ParseFixedWidthBreakPositions");
        source.Should().Contain("UiText.Get(\"TextToColumns_ChooseDelimitersInstruction\")");
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

        source.Should().Contain("UiText.Format(\"TextToColumns_TextWizardStepOf3\", normalizedStep)");
        source.Should().Contain("CreateWizardButtonRow");
        source.Should().Contain("Content = UiText.Get(\"TextToColumns_BackButton\")");
        source.Should().Contain("Content = UiText.Get(\"TextToColumns_NextButton\")");
        source.Should().Contain("Content = UiText.Get(\"TextToColumns_FinishButton\")");
        source.Should().Contain("MoveWizardStep");
        source.Should().Contain("UpdateWizardStep");
        source.Should().Contain("_backButton.IsEnabled = plan.BackEnabled");
        source.Should().Contain("_nextButton.IsEnabled = plan.NextEnabled");
        source.Should().Contain("UiText.Get(\"TextToColumns_ChooseFileTypeInstruction\")");
        source.Should().Contain("NextDefault: normalizedStep < 3");
        source.Should().Contain("FinishDefault: normalizedStep == 3");
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
                var next = FindVisualChildren<Button>(dialog)
                    .Single(button => Equals(button.Content, "_Next >"));

                next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                var tabDelimiter = FindVisualChildren<CheckBox>(dialog)
                    .Single(checkBox => Equals(checkBox.Content, "_Tab"));
                Keyboard.FocusedElement.Should().BeSameAs(tabDelimiter);

                next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                var columnSelector = FindVisualChildren<ComboBox>(dialog)
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

        source.Should().Contain("FocusInvalidDestinationInput();");
        source.Should().Contain("RefocusInvalidInputAfterWarning(ex.Message);");
        source.Should().Contain("private void RefocusInvalidInputAfterWarning(string message)");
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
        source.Should().Contain("FocusInvalidFixedWidthBreaksInput();");
        source.Should().Contain("RefocusInvalidInputAfterWarning(ex.Message);");
        source.Should().Contain("private void RefocusInvalidInputAfterWarning(string message)");
        source.Should().Contain("private void FocusInvalidFixedWidthBreaksInput()");
        source.Should().Contain("_wizardStep = 2;");
        source.Should().Contain("_fixedWidthButton.IsChecked = true;");
        source.Should().Contain("DialogFocus.FocusAndSelect(_fixedWidthBreaksBox);");
    }

    [Fact]
    public void TextToColumnsDialogInvalidCustomDelimiter_ReturnsToStepTwoAndFocusesOtherDelimiter()
    {
        var source = ReadTextToColumnsDialogSources();

        source.Should().Contain("FocusInvalidCustomDelimiterInput();");
        source.Should().Contain("RefocusInvalidInputAfterWarning(ex.Message);");
        source.Should().Contain("private void RefocusInvalidInputAfterWarning(string message)");
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
        source.Should().Contain("FocusInvalidDelimiterSelectionInput();");
        source.Should().Contain("throw new ArgumentException(UiText.Get(\"TextToColumns_SelectAtLeastOneDelimiter\"));");
        source.Should().Contain("string.Equals(message, UiText.Get(\"TextToColumns_SelectAtLeastOneDelimiter\"), StringComparison.Ordinal)");
        source.Should().Contain("private void FocusInvalidDelimiterSelectionInput()");
        source.Should().Contain("_wizardStep = 2;");
        source.Should().Contain("_delimitedButton.IsChecked = true;");
        source.Should().Contain("_tabBox.Focus();");
        source.Should().Contain("Keyboard.Focus(_tabBox);");
        source.Should().NotContain("return kinds.Count == 0 ? [TextToColumnsDelimiterKind.Comma] : kinds;");
    }
}
