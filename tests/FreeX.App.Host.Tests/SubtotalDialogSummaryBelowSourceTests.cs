using System.Windows.Controls;
using FluentAssertions;
using FreeX.Core.Model;
using SubtotalColumnChoice = FreeX.App.Presentation.DataTools.SubtotalDialogColumnChoice;

namespace FreeX.App.Host.Tests;

/// <summary>
/// R40-commands-group-outline-3-1: the Subtotal dialog's "Summary below data" checkbox must
/// reflect the active sheet's actual outline direction (<c>Sheet.OutlineSummaryBelow</c>) instead
/// of always opening checked, matching Excel's own Subtotal dialog which shares the same
/// underlying setting as <c>outlinePr/@summaryBelow</c>.
/// </summary>
public sealed class SubtotalDialogSummaryBelowSourceTests
{
    private static readonly SubtotalColumnChoice[] Columns =
    [
        new SubtotalColumnChoice(0, "Region", false),
        new SubtotalColumnChoice(1, "Sales", true)
    ];

    [Fact]
    public void SubtotalDialog_WhenSheetOutlineDirectionIsAbove_OpensSummaryBelowUnchecked()
    {
        // Sheet.OutlineSummaryBelow == false means the sheet's outline direction places summary
        // rows ABOVE each group's detail, so the dialog should open with the checkbox unchecked.
        StaTestRunner.Run(() =>
        {
            var sheetId = SheetId.New();
            var sheet = new Sheet(sheetId, "Data") { OutlineSummaryBelow = false };

            var dialog = new SubtotalDialog(Columns, summaryBelowData: sheet.OutlineSummaryBelow ?? true);
            dialog.Show();
            try
            {
                var summaryBelowBox = WpfTestTree.FindVisualDescendants<CheckBox>(dialog)
                    .Single(box => Equals(box.Content, UiText.Get("Subtotal_SummaryBelowData")));
                summaryBelowBox.IsChecked.Should().BeFalse();
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void SubtotalDialog_WhenSheetOutlineDirectionIsBelowOrUnset_OpensSummaryBelowChecked()
    {
        // No-regression sibling: an unset (null, Excel default) or explicitly-true outline
        // direction must still open the checkbox checked, matching the previous behavior for the
        // common case.
        StaTestRunner.Run(() =>
        {
            var sheetId = SheetId.New();
            var sheet = new Sheet(sheetId, "Data");
            sheet.OutlineSummaryBelow.Should().BeNull();

            var dialog = new SubtotalDialog(Columns, summaryBelowData: sheet.OutlineSummaryBelow ?? true);
            dialog.Show();
            try
            {
                var summaryBelowBox = WpfTestTree.FindVisualDescendants<CheckBox>(dialog)
                    .Single(box => Equals(box.Content, UiText.Get("Subtotal_SummaryBelowData")));
                summaryBelowBox.IsChecked.Should().BeTrue();
            }
            finally
            {
                dialog.Close();
            }

            // Also cover the default (no-argument) constructor path used by any caller that hasn't
            // been updated to pass the sheet's setting yet -- must remain unchanged (checked).
            var defaultDialog = new SubtotalDialog(Columns);
            defaultDialog.Show();
            try
            {
                var summaryBelowBox = WpfTestTree.FindVisualDescendants<CheckBox>(defaultDialog)
                    .Single(box => Equals(box.Content, UiText.Get("Subtotal_SummaryBelowData")));
                summaryBelowBox.IsChecked.Should().BeTrue();
            }
            finally
            {
                defaultDialog.Close();
            }
        });
    }
}
