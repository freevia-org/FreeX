using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R99-clipboard-paste-merge-cf-tile: WorkbookSession.ShouldFillSelectedDestinationRange (and its
/// WPF-host counterpart FreeX.App.Presentation.Editing.ClipboardPastePlanner.ShouldFillSelectedDestinationRange)
/// unconditionally excluded PasteSpecialContentKind.AllMergingConditionalFormats, forcing the
/// destination down to a single cell no matter how large a rectangle the user actually selected.
/// That directly contradicted Core.Commands' PasteCommandFactory, which was fixed
/// (R25-clipboard-paste-remaining-2) to tile this content kind across a larger destination range
/// exactly like every other Paste Special mode -- but the caller-level gating meant that tiling
/// code path could never actually be reached from a real paste gesture in either shell.
/// </summary>
public sealed class R99_PasteMergeCfTileTests
{
    [Fact]
    public void PasteSpecialAllMergingConditionalFormats_OverMultiCellSelection_TilesAcrossWholeDestinationRange()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var source = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(source, new NumberValue(42));

        var destinationCells = Enumerable.Range(1, 3)
            .Select(row => new CellAddress(sheet.Id, (uint)row, 2))
            .ToList();

        var session = CreateSession(workbook);
        session.SelectCell(source);
        var clipboardText = session.CopySelectedRangeText();

        // Select B1:B3 (a 1x3 destination -- an exact whole multiple of the copied 1x1 source) and
        // Paste Special > "All merging conditional formats".
        session.SelectRange(new GridRange(destinationCells[0], destinationCells[^1]));
        var options = new PasteSpecialOptions(ContentKind: PasteSpecialContentKind.AllMergingConditionalFormats);

        var result = session.PasteSpecialClipboardAtActiveCell(clipboardText, PasteCellsMode.All, options);

        result.Success.Should().BeTrue(result.ErrorMessage);

        // Real Excel tiles the copied source across the whole selected destination regardless of
        // which Paste Special facet was chosen -- every destination cell must get the copied value,
        // not just the anchor B1.
        foreach (var cell in destinationCells)
            sheet.GetValue(cell).Should().Be(new NumberValue(42));
    }

    [Fact]
    public void PasteSpecialAllMergingConditionalFormats_SingleCellDestination_StillMergesRuleAtAnchor()
    {
        // Sibling already-working case: pasting into a destination that matches the copied source's
        // own footprint (no tiling required) must keep working exactly as before this fix, including
        // actually merging in the conditional-format rule that travels with the copied cell.
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var source = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(source, new NumberValue(7));
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(source, source),
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "5",
            FormatIfTrue = new CellStyle { Bold = true },
            Priority = 1
        });

        var session = CreateSession(workbook);
        session.SelectCell(source);
        var clipboardText = session.CopySelectedRangeText();

        var destination = new CellAddress(sheet.Id, 4, 3);
        session.SelectCell(destination);
        var options = new PasteSpecialOptions(ContentKind: PasteSpecialContentKind.AllMergingConditionalFormats);

        var result = session.PasteSpecialClipboardAtActiveCell(clipboardText, PasteCellsMode.All, options);

        result.Success.Should().BeTrue(result.ErrorMessage);
        sheet.GetValue(destination).Should().Be(new NumberValue(7));
        sheet.ConditionalFormats.Should().HaveCount(2);
        sheet.ConditionalFormats.Should().Contain(rule =>
            rule.AppliesTo == new GridRange(destination, destination) && rule.Value1 == "5");
    }

    private static WorkbookSession CreateSession(Workbook workbook) =>
        new WorkbookSessionFactory().Create(
            new StartupWorkbookLoadResult(workbook, workbook.Name, "Opened.", IsFallback: false),
            viewportHeight: 240,
            viewportWidth: 320);

    private static Workbook CreateWorkbook(string name = "Book")
    {
        var workbook = new Workbook(name);
        workbook.AddSheet("Sheet1");
        workbook.ActiveSheetIndex = 0;
        return workbook;
    }
}
