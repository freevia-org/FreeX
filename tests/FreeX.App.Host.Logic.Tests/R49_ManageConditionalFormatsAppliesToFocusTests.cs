using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for R49-commands-cf-manage-3-2
/// (src/FreeX.App.Host/ManageConditionalFormatsDialog.Columns.cs, AppliesToTextBox_LostFocus).
///
/// Before the fix: the Applies-To column's TextBox uses UpdateSourceTrigger=LostFocus, so merely
/// clicking into that cell (e.g. to inspect it, or via keyboard row navigation) and back out --
/// with zero text changes -- fired AppliesToTextBox_LostFocus, which unconditionally executed
/// `rule.AdditionalRanges = null;` whenever the (unchanged) displayed text still parsed. A
/// multi-area rule's non-active areas were silently discarded on a no-op focus visit.
///
/// After the fix, a new AppliesToTextBox_GotFocus handler stashes the text shown when focus is
/// gained (on the TextBox's Tag), and AppliesToTextBox_LostFocus only clears AdditionalRanges when
/// the text actually changed from that stashed value.
/// </summary>
public sealed class R49_ManageConditionalFormatsAppliesToFocusTests
{
    [Fact]
    public void AppliesToTextBox_LostFocus_NoActualEdit_PreservesAdditionalRanges()
    {
        StaTestRunner.Run(() =>
        {
            var sheet = new Workbook("Book1").AddSheet("Sheet1");
            var dialog = new ManageConditionalFormatsDialog(sheet, selection: null);

            var rule = new ConditionalFormat
            {
                AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 1)), // A1:A5
                AdditionalRanges =
                [
                    new GridRange(new CellAddress(sheet.Id, 1, 3), new CellAddress(sheet.Id, 5, 3)) // C1:C5
                ],
            };

            var textBox = new TextBox { DataContext = rule, Text = "$A$1:$A$5" };

            InvokeGotFocus(textBox);
            // No edit -- text is exactly what it was when focus was gained.
            InvokeLostFocus(dialog, textBox);

            rule.AdditionalRanges.Should().NotBeNull(
                "merely visiting the Applies-To cell with no edit must not drop the rule's other areas");
            rule.AdditionalRanges.Should().ContainSingle();
        });
    }

    // Sibling no-regression: the ORIGINAL intent of this handler -- dropping stale
    // AdditionalRanges once the user genuinely retypes the Applies-To reference to something new --
    // must still work exactly as before the fix.
    [Fact]
    public void AppliesToTextBox_LostFocus_ActualEdit_StillClearsAdditionalRanges()
    {
        StaTestRunner.Run(() =>
        {
            var sheet = new Workbook("Book1").AddSheet("Sheet1");
            var dialog = new ManageConditionalFormatsDialog(sheet, selection: null);

            var rule = new ConditionalFormat
            {
                AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 1)), // A1:A5
                AdditionalRanges =
                [
                    new GridRange(new CellAddress(sheet.Id, 1, 3), new CellAddress(sheet.Id, 5, 3)) // C1:C5
                ],
            };

            var textBox = new TextBox { DataContext = rule, Text = "$A$1:$A$5" };

            InvokeGotFocus(textBox);
            // The user genuinely retypes a different single-area reference.
            textBox.Text = "$B$1:$B$5";
            InvokeLostFocus(dialog, textBox);

            rule.AdditionalRanges.Should().BeNull(
                "a genuine edit to the Applies-To reference must still drop the stale multi-area scope");
        });
    }

    private static void InvokeGotFocus(TextBox textBox)
    {
        ManageConditionalFormatsDialog.AppliesToTextBox_GotFocus(textBox, new RoutedEventArgs());
    }

    private static void InvokeLostFocus(ManageConditionalFormatsDialog dialog, TextBox textBox)
    {
        dialog.AppliesToTextBox_LostFocus(textBox, new RoutedEventArgs());
    }
}
