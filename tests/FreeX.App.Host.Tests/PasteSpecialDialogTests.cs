using System.Reflection;
using System.Windows.Automation;
using System.Windows.Controls;
using FluentAssertions;
using System.IO;

namespace FreeX.App.Host.Tests;

public sealed class PasteSpecialDialogTests
{
    [Theory]
    [InlineData("_rbComments", PasteSpecialDialogMode.Comments)]
    [InlineData("_rbValidation", PasteSpecialDialogMode.Validation)]
    [InlineData("_rbAllUsingSourceTheme", PasteSpecialDialogMode.AllUsingSourceTheme)]
    [InlineData("_rbAllExceptBorders", PasteSpecialDialogMode.AllExceptBorders)]
    [InlineData("_rbAllMergingConditionalFormats", PasteSpecialDialogMode.AllMergingConditionalFormats)]
    [InlineData("_rbFormulasAndNumberFormats", PasteSpecialDialogMode.FormulasAndNumberFormats)]
    [InlineData("_rbValuesAndNumberFormats", PasteSpecialDialogMode.ValuesAndNumberFormats)]
    [InlineData("_rbValuesAndSourceFormatting", PasteSpecialDialogMode.ValuesAndSourceFormatting)]
    [InlineData("_rbText", PasteSpecialDialogMode.Text)]
    [InlineData("_rbUnicodeText", PasteSpecialDialogMode.UnicodeText)]
    [InlineData("_rbLinkedPicture", PasteSpecialDialogMode.LinkedPicture)]
    public void Mode_ExposesExcelPasteSpecialChoices(string fieldName, PasteSpecialDialogMode expectedMode)
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new PasteSpecialDialog();
            try
            {
                GetRadioButton(dialog, fieldName).IsChecked = true;

                dialog.Mode.Should().Be(expectedMode);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Theory]
    [InlineData("_rbAll", "_All")]
    [InlineData("_rbValues", "_Values")]
    [InlineData("_rbFormulas", "_Formulas")]
    [InlineData("_rbFormats", "Forma_ts")]
    [InlineData("_rbComments", "_Comments and notes")]
    [InlineData("_rbValidation", "Validatio_n")]
    [InlineData("_rbAllUsingSourceTheme", "All using source t_heme")]
    [InlineData("_rbAllExceptBorders", "All e_xcept borders")]
    [InlineData("_rbAllMergingConditionalFormats", "All merging conditional _formats")]
    [InlineData("_rbColumnWidths", "Column _widths")]
    [InlineData("_rbFormulasAndNumberFormats", "Formulas and number fo_rmats")]
    [InlineData("_rbValuesAndNumberFormats", "Values and number for_mats")]
    [InlineData("_rbValuesAndSourceFormatting", "Values and source f_ormatting")]
    [InlineData("_rbText", "T_ext")]
    [InlineData("_rbUnicodeText", "_Unicode Text")]
    [InlineData("_rbPicture", "_Picture")]
    [InlineData("_rbLinkedPicture", "_Linked picture")]
    public void Choices_ExposeKeyboardAccessKeys(string fieldName, string expectedContent)
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new PasteSpecialDialog();
            try
            {
                GetRadioButton(dialog, fieldName).Content.Should().Be(expectedContent);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Theory]
    [InlineData("_rbAll", "Paste all", "PasteSpecialAllOption", "Paste all cell contents and formatting.")]
    [InlineData("_rbValues", "Paste values", "PasteSpecialValuesOption", "Paste only cell values.")]
    [InlineData("_rbFormulas", "Paste formulas", "PasteSpecialFormulasOption", "Paste formulas without changing existing formatting.")]
    [InlineData("_rbFormats", "Paste formats", "PasteSpecialFormatsOption", "Paste only cell formatting.")]
    [InlineData("_rbComments", "Paste comments and notes", "PasteSpecialCommentsAndNotesOption", "Paste only comments and notes.")]
    [InlineData("_rbValidation", "Paste validation", "PasteSpecialValidationOption", "Paste only data validation rules.")]
    [InlineData("_rbAllUsingSourceTheme", "Paste all using source theme", "PasteSpecialAllUsingSourceThemeOption", "Paste all content using the copied source theme.")]
    [InlineData("_rbAllExceptBorders", "Paste all except borders", "PasteSpecialAllExceptBordersOption", "Paste all content and formatting except cell borders.")]
    [InlineData("_rbAllMergingConditionalFormats", "Paste all merging conditional formats", "PasteSpecialAllMergingConditionalFormatsOption", "Paste all content while merging conditional formatting rules.")]
    [InlineData("_rbColumnWidths", "Paste column widths", "PasteSpecialColumnWidthsOption", "Paste only copied column widths.")]
    [InlineData("_rbFormulasAndNumberFormats", "Paste formulas and number formats", "PasteSpecialFormulasAndNumberFormatsOption", "Paste formulas and number formats.")]
    [InlineData("_rbValuesAndNumberFormats", "Paste values and number formats", "PasteSpecialValuesAndNumberFormatsOption", "Paste values and number formats.")]
    [InlineData("_rbValuesAndSourceFormatting", "Paste values and source formatting", "PasteSpecialValuesAndSourceFormattingOption", "Paste values with copied source formatting.")]
    [InlineData("_rbText", "Paste text", "PasteSpecialTextOption", "Paste clipboard text.")]
    [InlineData("_rbUnicodeText", "Paste Unicode text", "PasteSpecialUnicodeTextOption", "Paste clipboard Unicode text.")]
    [InlineData("_rbPicture", "Paste picture", "PasteSpecialPictureOption", "Paste copied cells as a picture.")]
    [InlineData("_rbLinkedPicture", "Paste linked picture", "PasteSpecialLinkedPictureOption", "Paste copied cells as a linked picture.")]
    public void Choices_ExposeStableAutomationMetadata(string fieldName, string expectedName, string expectedAutomationId, string expectedHelpText)
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new PasteSpecialDialog();
            try
            {
                var button = GetRadioButton(dialog, fieldName);

                AutomationProperties.GetName(button).Should().Be(expectedName);
                AutomationProperties.GetAutomationId(button).Should().Be(expectedAutomationId);
                AutomationProperties.GetHelpText(button).Should().Be(expectedHelpText);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void PasteChoices_FollowExcelDialogOrder()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "PasteSpecialDialog.Controls.cs"));
        var expectedOrder = new[]
        {
            "AddPasteChoice(grid, _rbAll,",
            "AddPasteChoice(grid, _rbFormulas,",
            "AddPasteChoice(grid, _rbValues,",
            "AddPasteChoice(grid, _rbFormats,",
            "AddPasteChoice(grid, _rbComments,",
            "AddPasteChoice(grid, _rbValidation,",
            "AddPasteChoice(grid, _rbAllUsingSourceTheme,",
            "AddPasteChoice(grid, _rbAllExceptBorders,",
            "AddPasteChoice(grid, _rbColumnWidths,",
            "AddPasteChoice(grid, _rbFormulasAndNumberFormats,",
            "AddPasteChoice(grid, _rbValuesAndNumberFormats,",
            "AddPasteChoice(grid, _rbAllMergingConditionalFormats,"
        };

        var positions = expectedOrder
            .Select(marker => source.IndexOf(marker, StringComparison.Ordinal))
            .ToArray();

        positions.Should().OnlyContain(position => position >= 0);
        positions.Should().BeInAscendingOrder();
    }

    [Theory]
    [InlineData("_opNone", "None")]
    [InlineData("_opAdd", "Add")]
    [InlineData("_opSubtract", "Subtract")]
    [InlineData("_opMultiply", "Multiply")]
    [InlineData("_opDivide", "Divide")]
    public void Operation_UsesExcelStyleRadioButtons(string fieldName, string expectedOperation)
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new PasteSpecialDialog();
            try
            {
                GetRadioButton(dialog, fieldName).IsChecked = true;

                dialog.Operation.Should().Be(expectedOperation);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Theory]
    [InlineData("_opNone", "Operation none", "PasteSpecialOperationNoneOption", "Paste without a mathematical operation.")]
    [InlineData("_opAdd", "Operation add", "PasteSpecialOperationAddOption", "Add copied values to destination values.")]
    [InlineData("_opSubtract", "Operation subtract", "PasteSpecialOperationSubtractOption", "Subtract copied values from destination values.")]
    [InlineData("_opMultiply", "Operation multiply", "PasteSpecialOperationMultiplyOption", "Multiply destination values by copied values.")]
    [InlineData("_opDivide", "Operation divide", "PasteSpecialOperationDivideOption", "Divide destination values by copied values.")]
    public void Operations_ExposeStableAutomationMetadata(string fieldName, string expectedName, string expectedAutomationId, string expectedHelpText)
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new PasteSpecialDialog();
            try
            {
                var button = GetRadioButton(dialog, fieldName);

                AutomationProperties.GetName(button).Should().Be(expectedName);
                AutomationProperties.GetAutomationId(button).Should().Be(expectedAutomationId);
                AutomationProperties.GetHelpText(button).Should().Be(expectedHelpText);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Theory]
    [InlineData("_skipBlanks", "Skip blanks", "PasteSpecialSkipBlanksBox", "Skip blank cells from the copied range.")]
    [InlineData("_transpose", "Transpose", "PasteSpecialTransposeBox", "Switch copied rows and columns while pasting.")]
    [InlineData("_keepColumnWidths", "Keep source column widths", "PasteSpecialKeepColumnWidthsBox", "Apply the copied source column widths.")]
    [InlineData("_pasteLinkButton", "Paste Link", "PasteSpecialPasteLinkButton", "Paste formulas that link to the copied cells.")]
    public void OptionsAndPasteLink_ExposeStableAutomationMetadata(string fieldName, string expectedName, string expectedAutomationId, string expectedHelpText)
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new PasteSpecialDialog();
            try
            {
                var control = GetControl<Control>(dialog, fieldName);

                AutomationProperties.GetName(control).Should().Be(expectedName);
                AutomationProperties.GetAutomationId(control).Should().Be(expectedAutomationId);
                AutomationProperties.GetHelpText(control).Should().Be(expectedHelpText);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void DialogButtons_ExposeKeyboardAccessKeys()
    {
        var source = ReadPasteSpecialDialogSources();

        source.Should().Contain("Content = UiText.Ok");
        source.Should().Contain("Content = UiText.Cancel");
        source.Should().Contain("SetAutomationMetadata(ok, UiText.Get(\"PasteSpecial_OkAutomationName\"), \"PasteSpecialOkButton\", UiText.Get(\"PasteSpecial_ApplyTheSelectedPasteSpecialOptions\"));");
        source.Should().Contain("SetAutomationMetadata(cancel, UiText.Get(\"PasteSpecial_CancelAutomationName\"), \"PasteSpecialCancelButton\", UiText.Get(\"PasteSpecial_CloseThePasteSpecialDialogWithoutApplyingChanges\"));");
    }

    [Fact]
    public void DialogOpenedFromKeyboard_FocusesDefaultAllChoice()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "PasteSpecialDialog.cs"));

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("_rbAll.Focus();");
        source.Should().Contain("Keyboard.Focus(_rbAll);");
    }

    [Fact]
    public void Layout_UsesExcelStyleGroupedPasteAndOperationSections()
    {
        var source = ReadPasteSpecialDialogSources();

        source.Should().Contain("Header = UiText.Get(\"PasteSpecial_PasteGroup\")");
        source.Should().Contain("Header = UiText.Get(\"PasteSpecial_OperationGroup\")");
        source.Should().Contain("CreatePasteOptionsPanel");
        source.Should().Contain("CreateOperationGroup");
        source.Should().Contain("_pasteLinkButton");
    }

    private static RadioButton GetRadioButton(PasteSpecialDialog dialog, string fieldName)
    {
        return GetControl<RadioButton>(dialog, fieldName);
    }

    private static T GetControl<T>(PasteSpecialDialog dialog, string fieldName)
        where T : Control
    {
        var field = typeof(PasteSpecialDialog).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        return field!.GetValue(dialog).Should().BeAssignableTo<T>().Subject;
    }

    private static string ReadPasteSpecialDialogSources() =>
        string.Join(
            Environment.NewLine,
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "PasteSpecialDialog.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "PasteSpecialDialog.Controls.cs")));
}
