using System.Reflection;
using System.Windows.Controls;
using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class PasteSpecialDialogTests
{
    [Theory]
    [InlineData("_rbComments", PasteSpecialDialogMode.Comments)]
    [InlineData("_rbValidation", PasteSpecialDialogMode.Validation)]
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

    private static RadioButton GetRadioButton(PasteSpecialDialog dialog, string fieldName)
    {
        var field = typeof(PasteSpecialDialog).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        return field!.GetValue(dialog).Should().BeOfType<RadioButton>().Subject;
    }
}
