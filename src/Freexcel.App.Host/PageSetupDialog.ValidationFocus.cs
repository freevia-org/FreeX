using System.Globalization;
using System.Windows.Controls;
using System.Windows.Input;

namespace Freexcel.App.Host;

public partial class PageSetupDialog
{
    private void FocusInvalidPrintArea()
    {
        PageSetupTabs.SelectedItem = SheetTab;
        PrintAreaBox.Focus();
        PrintAreaBox.SelectAll();
        Keyboard.Focus(PrintAreaBox);
    }

    private void FocusInvalidPrintTitles()
    {
        var target = PageLayoutInputParser.TryParseRepeatRows(RowsRepeatBox.Text, out _)
            ? ColumnsRepeatBox
            : RowsRepeatBox;
        PageSetupTabs.SelectedItem = SheetTab;
        target.Focus();
        target.SelectAll();
        Keyboard.Focus(target);
    }

    private void FocusInvalidPageTabNumber(TextBox target)
    {
        PageSetupTabs.SelectedItem = PageTab;
        target.Focus();
        target.SelectAll();
        Keyboard.Focus(target);
    }

    private void FocusInvalidScalingInput()
    {
        TextBox target;
        if (FitToRadioButton.IsChecked == true)
        {
            target = int.TryParse(FitPagesWideBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var wide) && wide > 0
                ? FitPagesTallBox
                : FitPagesWideBox;
        }
        else
        {
            target = ScalePercentBox;
        }

        PageSetupTabs.SelectedItem = PageTab;
        target.Focus();
        target.SelectAll();
        Keyboard.Focus(target);
    }

    private void FocusInvalidMarginInput()
    {
        foreach (var target in new[] { LeftMarginBox, RightMarginBox, TopMarginBox, BottomMarginBox })
        {
            if (!PageLayoutInputParser.TryParseMarginDistance(target.Text, out _))
            {
                FocusMarginsTabTextBox(target);
                return;
            }
        }

        FocusMarginsTabTextBox(LeftMarginBox);
    }

    private void FocusInvalidHeaderFooterMargin()
    {
        FocusMarginsTabTextBox(
            PageLayoutInputParser.TryParseMarginDistance(HeaderMarginBox.Text, out _)
                ? FooterMarginBox
                : HeaderMarginBox);
    }

    private void FocusMarginsTabTextBox(TextBox target)
    {
        PageSetupTabs.SelectedItem = MarginsTab;
        target.Focus();
        target.SelectAll();
        Keyboard.Focus(target);
    }
}
