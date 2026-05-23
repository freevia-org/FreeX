using System.Reflection;
using System.Windows.Controls;
using FluentAssertions;
using System.IO;

namespace Freexcel.App.Host.Tests;

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

    [Fact]
    public void DialogButtons_ExposeKeyboardAccessKeys()
    {
        var source = ReadPasteSpecialDialogSources();

        source.Should().Contain("Content = \"_OK\"");
        source.Should().Contain("Content = \"_Cancel\"");
    }

    [Fact]
    public void DialogOpenedFromKeyboard_FocusesDefaultAllChoice()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PasteSpecialDialog.cs"));

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("_rbAll.Focus();");
        source.Should().Contain("Keyboard.Focus(_rbAll);");
    }

    [Fact]
    public void Layout_UsesExcelStyleGroupedPasteAndOperationSections()
    {
        var source = ReadPasteSpecialDialogSources();

        source.Should().Contain("Header = \"Paste\"");
        source.Should().Contain("Header = \"Operation\"");
        source.Should().Contain("CreatePasteOptionsPanel");
        source.Should().Contain("CreateOperationGroup");
        source.Should().Contain("_pasteLinkButton");
    }

    private static RadioButton GetRadioButton(PasteSpecialDialog dialog, string fieldName)
    {
        var field = typeof(PasteSpecialDialog).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        return field!.GetValue(dialog).Should().BeOfType<RadioButton>().Subject;
    }

    private static string ReadPasteSpecialDialogSources() =>
        string.Join(
            Environment.NewLine,
            File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PasteSpecialDialog.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PasteSpecialDialog.Controls.cs")));
}
