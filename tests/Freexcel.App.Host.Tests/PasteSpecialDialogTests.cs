using System.Reflection;
using System.Windows.Controls;
using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class PasteSpecialDialogTests
{
    [Theory]
    [InlineData("_rbComments", PasteSpecialDialogMode.Comments)]
    [InlineData("_rbValidation", PasteSpecialDialogMode.Validation)]
    [InlineData("_rbAllUsingSourceTheme", PasteSpecialDialogMode.AllUsingSourceTheme)]
    [InlineData("_rbAllExceptBorders", PasteSpecialDialogMode.AllExceptBorders)]
    [InlineData("_rbFormulasAndNumberFormats", PasteSpecialDialogMode.FormulasAndNumberFormats)]
    [InlineData("_rbValuesAndNumberFormats", PasteSpecialDialogMode.ValuesAndNumberFormats)]
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
    [InlineData("_rbValues", "_Values only")]
    [InlineData("_rbFormulas", "_Formulas only")]
    [InlineData("_rbFormats", "Forma_ts only")]
    [InlineData("_rbComments", "_Comments and notes")]
    [InlineData("_rbValidation", "Validatio_n")]
    [InlineData("_rbAllUsingSourceTheme", "All using source t_heme")]
    [InlineData("_rbAllExceptBorders", "All e_xcept borders")]
    [InlineData("_rbColumnWidths", "Column _widths")]
    [InlineData("_rbFormulasAndNumberFormats", "Formulas and number fo_rmats")]
    [InlineData("_rbValuesAndNumberFormats", "Values and number for_mats")]
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

    private static RadioButton GetRadioButton(PasteSpecialDialog dialog, string fieldName)
    {
        var field = typeof(PasteSpecialDialog).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        return field!.GetValue(dialog).Should().BeOfType<RadioButton>().Subject;
    }
}
