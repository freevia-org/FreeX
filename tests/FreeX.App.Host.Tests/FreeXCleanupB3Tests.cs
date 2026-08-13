using System.Windows.Controls;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression tests for FreeX cleanup batch B3 (HIGH findings P62).
/// The WPF Find/Replace dialog's single-step Replace must advance past a match that turns out
/// not to be replaceable (e.g. a formula cell matched in Look-in=Values mode, whose displayed
/// result matches but whose formula text cannot be replaced) instead of retrying the same match
/// forever.
/// </summary>
public sealed class FreeXCleanupB3Tests
{
    [Fact]
    public void ReplaceOne_SkipsNonReplaceableValuesMatch_AndReplacesTheNextOne()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Book1");
            var sheet = workbook.AddSheet("Sheet1");
            var a1 = new CellAddress(sheet.Id, 1, 1);
            var a2 = new CellAddress(sheet.Id, 2, 1);

            // A1: formula whose cached/displayed result is "foo" — matches in Look-in=Values mode
            // (the dialog's default) but cannot itself be replaced in that mode (no plain text to
            // rewrite in-place; TryCreateReplacementCell returns false for a formula cell there).
            var formulaCell = Cell.FromFormula("=\"foo\"");
            formulaCell.Value = new TextValue("foo");
            sheet.SetCell(a1, formulaCell);
            // A2: a literal "foo" — replaceable.
            sheet.SetCell(a2, new TextValue("foo"));

            var commandBus = new CommandBus(_ => new TestCommandContext(workbook));
            var navigated = new List<CellAddress>();
            var dialog = new FindReplaceDialog(
                () => workbook,
                command => commandBus.Execute(workbook.Id, command),
                navigated.Add,
                replaceMode: true,
                getCurrentSheetId: () => sheet.Id);
            dialog.Show();
            try
            {
                var replaceFindBox = DialogSourceTestSupport.GetPrivateField<TextBox>(dialog, "ReplaceFindBox");
                var replaceBox = DialogSourceTestSupport.GetPrivateField<TextBox>(dialog, "ReplaceBox");
                replaceFindBox.Text = "foo";
                replaceBox.Text = "bar";

                // This scenario specifically exercises Look-in=Values (LookInCombo.SelectedIndex = 1);
                // set it explicitly rather than relying on the dialog's default, which is Formulas
                // (matching Excel and the Avalonia shell).
                DialogSourceTestSupport.GetPrivateField<ComboBox>(dialog, "LookInCombo").SelectedIndex = 1;
                DialogSourceTestSupport.InvokePrivateHandler(dialog, "FindNext_Click");

                // First Replace click: A1 is found first (row order) but is not replaceable in
                // Values mode. Before the fix this got permanently stuck here, leaving A2
                // untouched no matter how many more times Replace is clicked.
                DialogSourceTestSupport.InvokePrivateHandler(dialog, "Replace_Click");

                sheet.GetCell(a1)!.Value.Should().Be(new TextValue("foo"), "the formula cell cannot be replaced in Values mode");
                sheet.GetCell(a2)!.Value.Should().Be(new TextValue("bar"), "Replace must advance past the skipped match and replace A2");
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void ReplaceOne_AllMatchesNonReplaceable_ReportsNoReplaceableMatchWithoutHanging()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Book1");
            var sheet = workbook.AddSheet("Sheet1");
            var a1 = new CellAddress(sheet.Id, 1, 1);
            var a2 = new CellAddress(sheet.Id, 2, 1);

            var formula1 = Cell.FromFormula("=\"foo\"");
            formula1.Value = new TextValue("foo");
            sheet.SetCell(a1, formula1);
            var formula2 = Cell.FromFormula("=\"foo\"");
            formula2.Value = new TextValue("foo");
            sheet.SetCell(a2, formula2);

            var commandBus = new CommandBus(_ => new TestCommandContext(workbook));
            var dialog = new FindReplaceDialog(
                () => workbook,
                command => commandBus.Execute(workbook.Id, command),
                _ => { },
                replaceMode: true,
                getCurrentSheetId: () => sheet.Id);
            dialog.Show();
            try
            {
                var replaceFindBox = DialogSourceTestSupport.GetPrivateField<TextBox>(dialog, "ReplaceFindBox");
                var replaceBox = DialogSourceTestSupport.GetPrivateField<TextBox>(dialog, "ReplaceBox");
                replaceFindBox.Text = "foo";
                replaceBox.Text = "bar";

                // This scenario specifically exercises Look-in=Values; set it explicitly rather than
                // relying on the dialog's default, which is Formulas (matching Excel and the Avalonia
                // shell).
                DialogSourceTestSupport.GetPrivateField<ComboBox>(dialog, "LookInCombo").SelectedIndex = 1;
                DialogSourceTestSupport.InvokePrivateHandler(dialog, "FindNext_Click");
                // Must terminate (bounded by the match count) rather than loop forever, and must
                // leave both formula cells untouched.
                DialogSourceTestSupport.InvokePrivateHandler(dialog, "Replace_Click");

                sheet.GetCell(a1)!.Value.Should().Be(new TextValue("foo"));
                sheet.GetCell(a2)!.Value.Should().Be(new TextValue("foo"));
                DialogSourceTestSupport.GetPrivateField<TextBlock>(dialog, "StatusLabel").Text
                    .Should().Be("No replaceable match found.");
            }
            finally
            {
                dialog.Close();
            }
        });
    }
}
