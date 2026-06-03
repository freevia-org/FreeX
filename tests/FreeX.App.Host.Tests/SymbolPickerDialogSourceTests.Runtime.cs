using FluentAssertions;
using System.Windows.Automation;
using System.Windows.Controls;

namespace FreeX.App.Host.Tests;

public sealed partial class SymbolPickerDialogSourceTests
{
    [Fact]
    public void Dialog_AppliesSelectedFontToInitialAndRecentSymbols()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new SymbolPickerDialog();
            try
            {
                var fontBox = FindLogicalChildren<ComboBox>(dialog)
                    .Single(box => AutomationProperties.GetName(box) == UiText.Get("SymbolPicker_FontAutomationName"));
                var preview = FindLogicalChildren<TextBlock>(dialog)
                    .Single(text => AutomationProperties.GetName(text) == UiText.Get("SymbolPicker_SelectedSymbolPreviewAutomationName"));
                var symbolButtons = FindLogicalChildren<Button>(dialog)
                    .Where(button => button.Tag is string)
                    .ToList();

                fontBox.SelectedItem.Should().Be("Segoe UI Symbol");
                preview.FontFamily.Source.Should().Be("Segoe UI Symbol");
                symbolButtons.Should().NotBeEmpty();
                symbolButtons.Should().AllSatisfy(button => button.FontFamily.Source.Should().Be("Segoe UI Symbol"));

                fontBox.SelectedItem = "Arial";

                preview.FontFamily.Source.Should().Be("Arial");
                symbolButtons.Should().AllSatisfy(button => button.FontFamily.Source.Should().Be("Arial"));
            }
            finally
            {
                dialog.Close();
            }
        });
    }
}
