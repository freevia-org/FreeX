using System.Windows.Controls;
using System.Windows.Input;

using FreeX.App.Presentation.PageLayout;

namespace FreeX.App.Host;

public partial class PageSetupDialog
{
    private void FocusDialogTarget(PageSetupDialogFocusPlan plan)
    {
        PageSetupTabs.SelectedItem = plan.Route.Tab switch
        {
            PageSetupDialogTab.Margins => MarginsTab,
            PageSetupDialogTab.Sheet => SheetTab,
            _ => PageTab,
        };

        var target = FocusControlFor(plan.Target);
        if (target is TextBox textBox)
        {
            DialogFocus.FocusAndSelect(textBox);
            return;
        }

        target.Focus();
        Keyboard.Focus(target);
    }

    private Control FocusControlFor(PageSetupDialogFocusTarget target) =>
        target switch
        {
            PageSetupDialogFocusTarget.PaperSize => PaperSizeBox,
            PageSetupDialogFocusTarget.Margins => LeftMarginBox,
            PageSetupDialogFocusTarget.LeftMargin => LeftMarginBox,
            PageSetupDialogFocusTarget.RightMargin => RightMarginBox,
            PageSetupDialogFocusTarget.TopMargin => TopMarginBox,
            PageSetupDialogFocusTarget.BottomMargin => BottomMarginBox,
            PageSetupDialogFocusTarget.HeaderMargin => HeaderMarginBox,
            PageSetupDialogFocusTarget.FooterMargin => FooterMarginBox,
            PageSetupDialogFocusTarget.ScalePercent => ScalePercentBox,
            PageSetupDialogFocusTarget.FitPagesWide => FitPagesWideBox,
            PageSetupDialogFocusTarget.FitPagesTall => FitPagesTallBox,
            PageSetupDialogFocusTarget.FirstPageNumber => FirstPageNumberBox,
            PageSetupDialogFocusTarget.PrintQuality => PrintQualityBox,
            PageSetupDialogFocusTarget.PrintArea => PrintAreaBox,
            PageSetupDialogFocusTarget.RepeatRows => RowsRepeatBox,
            PageSetupDialogFocusTarget.RepeatColumns => ColumnsRepeatBox,
            PageSetupDialogFocusTarget.PageOrder => PageOrderBox,
            PageSetupDialogFocusTarget.PrintErrorValue => PrintErrorValueBox,
            PageSetupDialogFocusTarget.PrintComments => PrintCommentsBox,
            _ => OrientationBox,
        };
}
