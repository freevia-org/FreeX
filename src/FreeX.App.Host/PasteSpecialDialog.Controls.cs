using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace FreeX.App.Host;

public sealed partial class PasteSpecialDialog
{
    private GroupBox CreatePasteGroup()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        for (var i = 0; i < 9; i++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddPasteChoice(grid, _rbAll, 0, 0);
        AddPasteChoice(grid, _rbFormulas, 1, 0);
        AddPasteChoice(grid, _rbValues, 2, 0);
        AddPasteChoice(grid, _rbFormats, 3, 0);
        AddPasteChoice(grid, _rbComments, 4, 0);
        AddPasteChoice(grid, _rbValidation, 5, 0);
        AddPasteChoice(grid, _rbAllUsingSourceTheme, 6, 0);
        AddPasteChoice(grid, _rbAllExceptBorders, 7, 0);
        AddPasteChoice(grid, _rbColumnWidths, 8, 0);
        AddPasteChoice(grid, _rbFormulasAndNumberFormats, 0, 1);
        AddPasteChoice(grid, _rbValuesAndNumberFormats, 1, 1);
        AddPasteChoice(grid, _rbAllMergingConditionalFormats, 2, 1);
        AddPasteChoice(grid, _rbValuesAndSourceFormatting, 3, 1);
        AddPasteChoice(grid, _rbText, 4, 1);
        AddPasteChoice(grid, _rbUnicodeText, 5, 1);
        AddPasteChoice(grid, _rbPicture, 6, 1);
        AddPasteChoice(grid, _rbLinkedPicture, 7, 1);

        return new GroupBox
        {
            Header = UiText.Get("PasteSpecial_PasteGroup"),
            Content = grid,
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 10)
        };
    }

    private StackPanel CreatePasteOptionsPanel()
    {
        var options = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        options.Children.Add(_skipBlanks);
        options.Children.Add(_transpose);
        options.Children.Add(_keepColumnWidths);
        return options;
    }

    private GroupBox CreateOperationGroup() =>
        new()
        {
            Header = UiText.Get("PasteSpecial_OperationGroup"),
            Content = CreateOperationPanel(),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 12)
        };

    private StackPanel CreateFooterRow()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        _pasteLinkButton.Click += (_, _) =>
        {
            _pasteLinkRequested = true;
            DialogResult = true;
        };

        var ok = new Button { Content = UiText.Ok, Width = 80, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancel = new Button { Content = UiText.Cancel, Width = 80, IsCancel = true };
        SetAutomationMetadata(ok, UiText.Get("PasteSpecial_OkAutomationName"), "PasteSpecialOkButton", UiText.Get("PasteSpecial_ApplyTheSelectedPasteSpecialOptions"));
        SetAutomationMetadata(cancel, UiText.Get("PasteSpecial_CancelAutomationName"), "PasteSpecialCancelButton", UiText.Get("PasteSpecial_CloseThePasteSpecialDialogWithoutApplyingChanges"));
        ok.Click += (_, _) => { DialogResult = true; };
        row.Children.Add(_pasteLinkButton);
        row.Children.Add(ok);
        row.Children.Add(cancel);
        return row;
    }

    private void ApplyAutomationMetadata()
    {
        SetAutomationMetadata(_rbAll, UiText.Get("PasteSpecial_AllAutomationName"), "PasteSpecialAllOption", UiText.Get("PasteSpecial_PasteAllCellContentsAndFormatting"));
        SetAutomationMetadata(_rbValues, UiText.Get("PasteSpecial_ValuesAutomationName"), "PasteSpecialValuesOption", UiText.Get("PasteSpecial_PasteOnlyCellValues"));
        SetAutomationMetadata(_rbFormulas, UiText.Get("PasteSpecial_FormulasAutomationName"), "PasteSpecialFormulasOption", UiText.Get("PasteSpecial_PasteFormulasWithoutChangingExistingFormatting"));
        SetAutomationMetadata(_rbFormats, UiText.Get("PasteSpecial_FormatsAutomationName"), "PasteSpecialFormatsOption", UiText.Get("PasteSpecial_PasteOnlyCellFormatting"));
        SetAutomationMetadata(_rbComments, UiText.Get("PasteSpecial_CommentsAndNotesAutomationName"), "PasteSpecialCommentsAndNotesOption", UiText.Get("PasteSpecial_PasteOnlyCommentsAndNotes"));
        SetAutomationMetadata(_rbValidation, UiText.Get("PasteSpecial_ValidationAutomationName"), "PasteSpecialValidationOption", UiText.Get("PasteSpecial_PasteOnlyDataValidationRules"));
        SetAutomationMetadata(_rbAllUsingSourceTheme, UiText.Get("PasteSpecial_AllUsingSourceThemeAutomationName"), "PasteSpecialAllUsingSourceThemeOption", UiText.Get("PasteSpecial_PasteAllContentUsingTheCopiedSourceTheme"));
        SetAutomationMetadata(_rbAllExceptBorders, UiText.Get("PasteSpecial_AllExceptBordersAutomationName"), "PasteSpecialAllExceptBordersOption", UiText.Get("PasteSpecial_PasteAllContentAndFormattingExceptCellBorders"));
        SetAutomationMetadata(_rbAllMergingConditionalFormats, UiText.Get("PasteSpecial_AllMergingConditionalFormatsAutomationName"), "PasteSpecialAllMergingConditionalFormatsOption", UiText.Get("PasteSpecial_PasteAllContentWhileMergingConditionalFormattingRules"));
        SetAutomationMetadata(_rbColumnWidths, UiText.Get("PasteSpecial_ColumnWidthsAutomationName"), "PasteSpecialColumnWidthsOption", UiText.Get("PasteSpecial_PasteOnlyCopiedColumnWidths"));
        SetAutomationMetadata(_rbFormulasAndNumberFormats, UiText.Get("PasteSpecial_FormulasAndNumberFormatsAutomationName"), "PasteSpecialFormulasAndNumberFormatsOption", UiText.Get("PasteSpecial_PasteFormulasAndNumberFormats"));
        SetAutomationMetadata(_rbValuesAndNumberFormats, UiText.Get("PasteSpecial_ValuesAndNumberFormatsAutomationName"), "PasteSpecialValuesAndNumberFormatsOption", UiText.Get("PasteSpecial_PasteValuesAndNumberFormats"));
        SetAutomationMetadata(_rbValuesAndSourceFormatting, UiText.Get("PasteSpecial_ValuesAndSourceFormattingAutomationName"), "PasteSpecialValuesAndSourceFormattingOption", UiText.Get("PasteSpecial_PasteValuesWithCopiedSourceFormatting"));
        SetAutomationMetadata(_rbText, UiText.Get("PasteSpecial_TextAutomationName"), "PasteSpecialTextOption", UiText.Get("PasteSpecial_PasteClipboardText"));
        SetAutomationMetadata(_rbUnicodeText, UiText.Get("PasteSpecial_UnicodeTextAutomationName"), "PasteSpecialUnicodeTextOption", UiText.Get("PasteSpecial_PasteClipboardUnicodeText"));
        SetAutomationMetadata(_rbPicture, UiText.Get("PasteSpecial_PictureAutomationName"), "PasteSpecialPictureOption", UiText.Get("PasteSpecial_PasteCopiedCellsAsAPicture"));
        SetAutomationMetadata(_rbLinkedPicture, UiText.Get("PasteSpecial_LinkedPictureAutomationName"), "PasteSpecialLinkedPictureOption", UiText.Get("PasteSpecial_PasteCopiedCellsAsALinkedPicture"));
        SetAutomationMetadata(_skipBlanks, UiText.Get("PasteSpecial_SkipBlanksAutomationName"), "PasteSpecialSkipBlanksBox", UiText.Get("PasteSpecial_SkipBlankCellsFromTheCopiedRange"));
        SetAutomationMetadata(_transpose, UiText.Get("PasteSpecial_TransposeAutomationName"), "PasteSpecialTransposeBox", UiText.Get("PasteSpecial_SwitchCopiedRowsAndColumnsWhilePasting"));
        SetAutomationMetadata(_keepColumnWidths, UiText.Get("PasteSpecial_KeepSourceColumnWidthsAutomationName"), "PasteSpecialKeepColumnWidthsBox", UiText.Get("PasteSpecial_ApplyTheCopiedSourceColumnWidths"));
        SetAutomationMetadata(_opNone, UiText.Get("PasteSpecial_OperationNoneAutomationName"), "PasteSpecialOperationNoneOption", UiText.Get("PasteSpecial_PasteWithoutAMathematicalOperation"));
        SetAutomationMetadata(_opAdd, UiText.Get("PasteSpecial_OperationAddAutomationName"), "PasteSpecialOperationAddOption", UiText.Get("PasteSpecial_AddCopiedValuesToDestinationValues"));
        SetAutomationMetadata(_opSubtract, UiText.Get("PasteSpecial_OperationSubtractAutomationName"), "PasteSpecialOperationSubtractOption", UiText.Get("PasteSpecial_SubtractCopiedValuesFromDestinationValues"));
        SetAutomationMetadata(_opMultiply, UiText.Get("PasteSpecial_OperationMultiplyAutomationName"), "PasteSpecialOperationMultiplyOption", UiText.Get("PasteSpecial_MultiplyDestinationValuesByCopiedValues"));
        SetAutomationMetadata(_opDivide, UiText.Get("PasteSpecial_OperationDivideAutomationName"), "PasteSpecialOperationDivideOption", UiText.Get("PasteSpecial_DivideDestinationValuesByCopiedValues"));
        SetAutomationMetadata(_pasteLinkButton, UiText.Get("PasteSpecial_PasteLinkAutomationName"), "PasteSpecialPasteLinkButton", UiText.Get("PasteSpecial_PasteFormulasThatLinkToTheCopiedCells"));
    }

    private static void SetAutomationMetadata(Control control, string name, string automationId, string helpText)
    {
        AutomationProperties.SetName(control, name);
        AutomationProperties.SetAutomationId(control, automationId);
        AutomationProperties.SetHelpText(control, helpText);
    }

    private static void AddPasteChoice(Grid panel, RadioButton button, int row, int column)
    {
        Grid.SetRow(button, row);
        Grid.SetColumn(button, column);
        panel.Children.Add(button);
    }

    private static RadioButton CreateOperationButton(string content, bool isChecked = false) =>
        new()
        {
            Content = content,
            GroupName = "PasteSpecialOperation",
            IsChecked = isChecked,
            Margin = new Thickness(0, 0, 12, 6)
        };

    private Grid CreateOperationPanel()
    {
        var panel = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        panel.ColumnDefinitions.Add(new ColumnDefinition());
        panel.ColumnDefinitions.Add(new ColumnDefinition());
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddOperation(panel, _opNone, 0, 0);
        AddOperation(panel, _opAdd, 0, 1);
        AddOperation(panel, _opSubtract, 1, 0);
        AddOperation(panel, _opMultiply, 1, 1);
        AddOperation(panel, _opDivide, 2, 0);
        return panel;
    }

    private static void AddOperation(Grid panel, RadioButton button, int row, int column)
    {
        Grid.SetRow(button, row);
        Grid.SetColumn(button, column);
        panel.Children.Add(button);
    }
}
