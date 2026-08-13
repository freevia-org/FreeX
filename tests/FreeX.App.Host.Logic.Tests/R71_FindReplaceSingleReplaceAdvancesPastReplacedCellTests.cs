using System.Windows.Controls;

using FluentAssertions;

using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// R71-commands-find-replace-4-2: after a successful single Replace, the WPF FindReplaceDialog
/// re-ran Find() and then UNCONDITIONALLY jumped to match #1 (_currentIndex = 0) instead of
/// advancing past the just-replaced cell. When the replacement text still contains the search
/// term (e.g. "Report" -&gt; "Report_v2"), the just-edited cell remains match #1 after the refind,
/// so every subsequent Replace click re-edited the SAME cell ("Report_v2_v2_v2...") and later
/// matches were never reached. The fix advances to the first remaining match whose address sorts
/// strictly after the just-replaced cell (in the current search order), wrapping to the top only
/// when none remain -- mirroring the Avalonia shell's WorkbookSession.ReplaceNextValue /
/// FindNextResultIndexAtSameAddress fallback (FindFirstResultAfterActiveCell).
/// </summary>
public sealed class R71_FindReplaceSingleReplaceAdvancesPastReplacedCellTests
{
    [Fact]
    public void ReplaceOne_WhenReplacementStillMatches_AdvancesThroughEachDistinctCellOnce()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Book1");
            var sheet = workbook.AddSheet("Sheet1");
            var a1 = new CellAddress(sheet.Id, 1, 1);
            var a2 = new CellAddress(sheet.Id, 2, 1);
            var a3 = new CellAddress(sheet.Id, 3, 1);
            sheet.SetCell(a1, new TextValue("Report"));
            sheet.SetCell(a2, new TextValue("Report"));
            sheet.SetCell(a3, new TextValue("Report"));

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
                DialogSourceTestSupport.GetPrivateField<TextBox>(dialog, "ReplaceFindBox").Text = "Report";
                // The replacement text still contains the search term, so the just-replaced cell
                // remains a match after the refind -- the exact reproduction of the bug.
                DialogSourceTestSupport.GetPrivateField<TextBox>(dialog, "ReplaceBox").Text = "Report_v2";

                DialogSourceTestSupport.InvokePrivateHandler(dialog, "FindNext_Click");
                navigated.Should().ContainSingle().Which.Should().Be(a1, "Find Next with no active cell starts at the first sheet-order match");

                DialogSourceTestSupport.InvokePrivateHandler(dialog, "Replace_Click");
                sheet.GetCell(a1)!.Value.Should().Be(new TextValue("Report_v2"), "the first Replace click must edit A1 exactly once");
                sheet.GetCell(a2)!.Value.Should().Be(new TextValue("Report"));
                sheet.GetCell(a3)!.Value.Should().Be(new TextValue("Report"));

                DialogSourceTestSupport.InvokePrivateHandler(dialog, "Replace_Click");
                sheet.GetCell(a1)!.Value.Should().Be(
                    new TextValue("Report_v2"),
                    "pre-fix, this second Replace click re-edited A1 again instead of advancing to A2");
                sheet.GetCell(a2)!.Value.Should().Be(new TextValue("Report_v2"), "the second Replace click must advance to A2");
                sheet.GetCell(a3)!.Value.Should().Be(new TextValue("Report"));

                DialogSourceTestSupport.InvokePrivateHandler(dialog, "Replace_Click");
                sheet.GetCell(a1)!.Value.Should().Be(new TextValue("Report_v2"));
                sheet.GetCell(a2)!.Value.Should().Be(new TextValue("Report_v2"));
                sheet.GetCell(a3)!.Value.Should().Be(new TextValue("Report_v2"), "the third Replace click must advance to A3");

                navigated.Should().ContainInOrder(a1, a2, a3);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void ReplaceOne_WhenReplacementNoLongerMatches_StillAdvancesToNextMatch()
    {
        // Sibling no-regression case: when the replacement text does NOT still match the search
        // (the common case, e.g. "foo" -> "bar"), the just-replaced cell drops out of the refind
        // results entirely, so both the old and the new logic land on the next remaining match --
        // this must keep working exactly as before the fix.
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Book1");
            var sheet = workbook.AddSheet("Sheet1");
            var a1 = new CellAddress(sheet.Id, 1, 1);
            var b1 = new CellAddress(sheet.Id, 1, 2);
            sheet.SetCell(a1, new TextValue("foo one"));
            sheet.SetCell(b1, new TextValue("foo two"));

            var commandBus = new CommandBus(_ => new TestCommandContext(workbook));
            var navigated = new List<CellAddress>();
            var refreshCount = 0;
            var dialog = new FindReplaceDialog(
                () => workbook,
                command => commandBus.Execute(workbook.Id, command),
                navigated.Add,
                replaceMode: true,
                getCurrentSheetId: () => sheet.Id,
                onWorkbookChanged: () => refreshCount++);
            dialog.Show();
            try
            {
                DialogSourceTestSupport.GetPrivateField<TextBox>(dialog, "ReplaceFindBox").Text = "foo";
                DialogSourceTestSupport.GetPrivateField<TextBox>(dialog, "ReplaceBox").Text = "bar";

                DialogSourceTestSupport.InvokePrivateHandler(dialog, "FindNext_Click");
                DialogSourceTestSupport.InvokePrivateHandler(dialog, "Replace_Click");

                refreshCount.Should().Be(1);
                sheet.GetCell(a1)!.Value.Should().Be(new TextValue("bar one"));
                sheet.GetCell(b1)!.Value.Should().Be(new TextValue("foo two"));
                navigated.Should().ContainInOrder(a1, b1);

                DialogSourceTestSupport.InvokePrivateHandler(dialog, "Replace_Click");
                sheet.GetCell(b1)!.Value.Should().Be(new TextValue("bar two"), "the last remaining match must still be replaced");
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void ReplaceOne_OnLastMatch_WrapsToFirstMatchWhenNoneRemainAfterIt()
    {
        // The just-replaced cell is the LAST in search order and still matches after replacement,
        // so no result sorts strictly after it -- the fix must wrap to the top (index 0) rather
        // than getting stuck or throwing.
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Book1");
            var sheet = workbook.AddSheet("Sheet1");
            var a1 = new CellAddress(sheet.Id, 1, 1);
            sheet.SetCell(a1, new TextValue("Report"));

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
                DialogSourceTestSupport.GetPrivateField<TextBox>(dialog, "ReplaceFindBox").Text = "Report";
                DialogSourceTestSupport.GetPrivateField<TextBox>(dialog, "ReplaceBox").Text = "Report_v2";

                DialogSourceTestSupport.InvokePrivateHandler(dialog, "FindNext_Click");
                DialogSourceTestSupport.InvokePrivateHandler(dialog, "Replace_Click");

                sheet.GetCell(a1)!.Value.Should().Be(new TextValue("Report_v2"));
                navigated.Should().HaveCount(2);
                navigated[1].Should().Be(a1, "the only match wraps back to itself when it is both first and last in the (single-result) set");

                DialogSourceTestSupport.InvokePrivateHandler(dialog, "Replace_Click");
                sheet.GetCell(a1)!.Value.Should().Be(new TextValue("Report_v2_v2"), "a second Replace click on the same sole remaining match replaces it again");
            }
            finally
            {
                dialog.Close();
            }
        });
    }
}
