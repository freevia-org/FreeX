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
                var fontBox = WpfTestTree.FindLogicalDescendants<ComboBox>(dialog)
                    .Single(box => AutomationProperties.GetName(box) == UiText.Get("SymbolPicker_FontAutomationName"));
                var preview = WpfTestTree.FindLogicalDescendants<TextBlock>(dialog)
                    .Single(text => AutomationProperties.GetName(text) == UiText.Get("SymbolPicker_SelectedSymbolPreviewAutomationName"));
                var symbolList = WpfTestTree.FindLogicalDescendants<ListBox>(dialog)
                    .Single(list => AutomationProperties.GetName(list) == UiText.Get("SymbolPicker_SymbolsAutomationName"));
                var recentList = WpfTestTree.FindLogicalDescendants<ListBox>(dialog)
                    .Single(list => AutomationProperties.GetName(list) == UiText.Get("SymbolPicker_RecentlyUsedSymbols"));

                fontBox.SelectedItem.Should().Be("Segoe UI Symbol");
                preview.FontFamily.Source.Should().Be("Segoe UI Symbol");
                symbolList.Items.Count.Should().BeGreaterThan(20);
                recentList.Items.Count.Should().BeGreaterThan(8);
                symbolList.FontFamily.Source.Should().Be("Segoe UI Symbol");
                recentList.FontFamily.Source.Should().Be("Segoe UI Symbol");

                fontBox.SelectedItem = "Arial";

                preview.FontFamily.Source.Should().Be("Arial");
                symbolList.FontFamily.Source.Should().Be("Arial");
                recentList.FontFamily.Source.Should().Be("Arial");
            }
            finally
            {
                dialog.Close();
            }
        });
    }
}
