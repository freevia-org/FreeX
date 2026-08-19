using System.Windows.Controls;

using FluentAssertions;

using Free.Shared.AppServices;
using Free.Shared.Shell;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// find-replace F1: FreeX (Excel-parity) intentionally supports a blank "Find what" combined
/// with a Find-Format criterion -- FindNext, FindAll, and ReplaceAll's WPF handlers all gate
/// their blank-search warning behind `_findFormatDiff is null`, but ReplaceOne() (behind the
/// single "Replace" button) omitted that check and always warned/blocked on a blank search, even
/// with a Find-Format criterion set. Fixed by adding the same `_findFormatDiff is null` guard
/// ReplaceOne() already applies everywhere else in this file.
/// </summary>
public sealed class FindReplaceF1_ReplaceOneAllowsBlankSearchWithFindFormatTests
{
    [Fact]
    public void ReplaceOne_WithBlankSearchAndFindFormatCriterion_ReplacesWithoutBlankSearchWarning()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Book1");
            var sheet = workbook.AddSheet("Sheet1");
            var a1 = new CellAddress(sheet.Id, 1, 1);
            var a2 = new CellAddress(sheet.Id, 2, 1);
            var boldStyle = workbook.RegisterStyle(new CellStyle { Bold = true });
            var italicStyle = workbook.RegisterStyle(new CellStyle { Italic = true });
            var boldCell = Cell.FromValue(new TextValue("Alpha"));
            boldCell.StyleId = boldStyle;
            sheet.SetCell(a1, boldCell);
            var italicSourceCell = Cell.FromValue(new TextValue("ItalicSource"));
            italicSourceCell.StyleId = italicStyle;
            sheet.SetCell(a2, italicSourceCell);

            var commandBus = new CommandBus(_ => new TestCommandContext(workbook));
            var refreshCount = 0;
            CellAddress? activeSelection = a1;
            var dialog = new FindReplaceDialog(
                () => workbook,
                command => commandBus.Execute(workbook.Id, command),
                _ => { },
                replaceMode: true,
                getCurrentSheetId: () => sheet.Id,
                getActiveSelectionCell: () => activeSelection,
                onWorkbookChanged: () => refreshCount++);
            dialog.Show();

            var warnings = new List<string?>();
            var previousHandler = HeadlessMessageBox.Handler;
            HeadlessMessageBox.Handler = (message, _) =>
            {
                warnings.Add(message);
                return UserMessageResult.Ok;
            };
            try
            {
                // Set the Find-Format criterion from A1 (Bold) via the real "choose format from
                // cell" entry point, exactly like the user's Format... picker.
                DialogSourceTestSupport.InvokePrivateHandler(dialog, "ChooseFindFormatFromCellButton_Click");
                DialogSourceTestSupport.GetPrivateField<StyleDiff>(dialog, "_findFormatDiff").Should().NotBeNull();

                // Set the Replace-with format from A2 (Italic) the same way -- this is what
                // makes the format-only replace actually reformat the matched cell without a
                // "Find what"/"Replace with" text substitution.
                activeSelection = a2;
                DialogSourceTestSupport.InvokePrivateHandler(dialog, "ChooseReplaceWithFormatFromCellButton_Click");
                DialogSourceTestSupport.GetPrivateField<StyleDiff>(dialog, "_replaceFormatDiff").Should().NotBeNull();
                activeSelection = a1;

                // "Find what" and "Replace with" both stay blank -- the format-only scenario.
                DialogSourceTestSupport.GetPrivateField<TextBox>(dialog, "ReplaceFindBox").Text.Should().BeEmpty();
                DialogSourceTestSupport.GetPrivateField<TextBox>(dialog, "ReplaceBox").Text.Should().BeEmpty();

                DialogSourceTestSupport.InvokePrivateHandler(dialog, "Replace_Click");

                warnings.Should().BeEmpty(
                    "a Find-Format criterion makes a blank search meaningful (format-only replace), " +
                    "so the single Replace button must not show 'Enter text in Find what.' -- exactly " +
                    "like FindNext/FindAll/ReplaceAll already behave for the same configuration");
                refreshCount.Should().Be(1, "the format-only replace must actually reach _workflow.ReplaceNext and apply an edit");
                sheet.GetCell(a1)!.Value.Should().Be(new TextValue("Alpha"), "format-only replace must leave cell text untouched");
                sheet.GetCell(a1)!.StyleId.Should().Be(italicStyle, "the matched Bold cell must be reformatted to the chosen Replace-with format");
            }
            finally
            {
                HeadlessMessageBox.Handler = previousHandler;
                dialog.Close();
            }
        });
    }

    [Fact]
    public void ReplaceOne_WithBlankSearchAndNoFindFormatCriterion_StillShowsBlankSearchWarning()
    {
        // Sibling no-regression case: a genuinely blank search with NO format criterion at all
        // must keep showing the "Find text is required" warning and must NOT reach the workflow
        // (matches FindNext/FindAll/ReplaceAll's existing behavior for the same configuration).
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Book1");
            var sheet = workbook.AddSheet("Sheet1");
            var a1 = new CellAddress(sheet.Id, 1, 1);
            sheet.SetCell(a1, new TextValue("Alpha"));

            var commandBus = new CommandBus(_ => new TestCommandContext(workbook));
            var refreshCount = 0;
            var dialog = new FindReplaceDialog(
                () => workbook,
                command => commandBus.Execute(workbook.Id, command),
                _ => { },
                replaceMode: true,
                getCurrentSheetId: () => sheet.Id,
                onWorkbookChanged: () => refreshCount++);
            dialog.Show();

            var warnings = new List<string?>();
            var previousHandler = HeadlessMessageBox.Handler;
            HeadlessMessageBox.Handler = (message, _) =>
            {
                warnings.Add(message);
                return UserMessageResult.Ok;
            };
            try
            {
                // No Find-Format criterion was ever set on this dialog, so _findFormatDiff stays
                // null -- this is the genuinely-blank-search case, unlike the sibling test above.
                DialogSourceTestSupport.GetPrivateField<TextBox>(dialog, "ReplaceFindBox").Text.Should().BeEmpty();

                DialogSourceTestSupport.InvokePrivateHandler(dialog, "Replace_Click");

                warnings.Should().ContainSingle("a plain blank search with no format criterion must still warn, exactly as before this fix");
                refreshCount.Should().Be(0, "the workflow must never be reached for a genuinely blank search");
                sheet.GetCell(a1)!.Value.Should().Be(new TextValue("Alpha"));
            }
            finally
            {
                HeadlessMessageBox.Handler = previousHandler;
                dialog.Close();
            }
        });
    }
}
