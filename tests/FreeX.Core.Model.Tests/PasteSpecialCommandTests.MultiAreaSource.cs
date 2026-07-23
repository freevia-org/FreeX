using System.Collections.Generic;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R78-commands-paste-special-5-1..5-4: a multi-area (Ctrl+click) source selection's
/// <c>GridRange</c> passed to these commands is only the BOUNDING BOX of the actually-selected
/// areas (e.g. Ctrl+clicking columns A and C excludes B, but the bounding box is A:C). Before the
/// fix, every one of these commands walked the whole bounding box as if it had all been selected,
/// silently clobbering/leaking into the destination cells aligned with the never-selected gap.
/// After the fix, passing the real per-area list (mirroring
/// MainWindow.ClipboardCommands.cs's InternalClipboard.SourceAreas) makes each command skip gap
/// cells entirely.
/// </summary>
public sealed partial class PasteSpecialCommandTests
{
    [Fact]
    public void PasteColumnWidthsCommand_MultiAreaSource_LeavesGapColumnAtDestinationUntouched()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        sheet.ColumnWidths[1] = 20; // A -- selected
        sheet.ColumnWidths[3] = 30; // C -- selected
        // B (col 2) intentionally left without a width -- the gap, never Ctrl+clicked.
        sheet.ColumnWidths[6] = 99; // F -- destination's gap column already has a custom width.

        var sourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 3)); // bounding box A:C
        var sourceAreas = new List<GridRange>
        {
            new(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1)), // A
            new(new CellAddress(sheet.Id, 1, 3), new CellAddress(sheet.Id, 1, 3)), // C
        };

        var command = new PasteColumnWidthsCommand(sheet.Id, sourceRange, destinationStartCol: 5, destinationColCount: 3, sourceAreas);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.ColumnWidths[5].Should().Be(20, "E is aligned with selected column A");
        sheet.ColumnWidths[6].Should().Be(99, "F aligns with the never-selected gap column B and must be left untouched");
        sheet.ColumnWidths[7].Should().Be(30, "G is aligned with selected column C");
    }

    [Fact]
    public void PasteColumnWidthsCommand_ContiguousSource_StillAppliesToEveryDestinationColumn()
    {
        // No-regression sibling: an ordinary contiguous (single-area) copy has no gap to preserve
        // -- every column in the bounding box really was part of the selection, so the destination
        // footprint must still be fully overwritten exactly as before this fix.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        sheet.ColumnWidths[1] = 20; // A
        sheet.ColumnWidths[3] = 30; // C
        // B has no custom width.
        sheet.ColumnWidths[6] = 99; // F pre-existing width.

        var sourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 3));

        var command = new PasteColumnWidthsCommand(sheet.Id, sourceRange, destinationStartCol: 5, destinationColCount: 3);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.ColumnWidths[5].Should().Be(20);
        sheet.ColumnWidths.Should().NotContainKey(6, "B (the middle of a genuinely contiguous copy) has no width, so F is cleared to match");
        sheet.ColumnWidths[7].Should().Be(30);
    }

    [Fact]
    public void PasteLinkService_MultiAreaSource_SkipsGapCellLink()
    {
        var sheetId = SheetId.New();
        var sourceRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 1, 3)); // bounding box A1:C1
        var sourceAreas = new List<GridRange>
        {
            new(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 1, 1)), // A1
            new(new CellAddress(sheetId, 1, 3), new CellAddress(sheetId, 1, 3)), // C1
        };
        var destinationRange = new GridRange(new CellAddress(sheetId, 5, 5), new CellAddress(sheetId, 5, 7)); // E5:G5

        var linkedCells = PasteLinkService.CreateLinkedCells(
            sourceRange,
            destination: destinationRange.Start,
            destinationRange: destinationRange,
            sourceSheetName: "Sheet1",
            transpose: false,
            sourceAreas: sourceAreas);

        linkedCells.Should().HaveCount(2, "only the two actually-selected source cells should produce a link");
        linkedCells.Should().Contain(c => c.Address == new CellAddress(sheetId, 5, 5) && c.Cell.FormulaText == "Sheet1!A1");
        linkedCells.Should().Contain(c => c.Address == new CellAddress(sheetId, 5, 7) && c.Cell.FormulaText == "Sheet1!C1");
        linkedCells.Should().NotContain(c => c.Address == new CellAddress(sheetId, 5, 6), "F5 aligns with the never-selected gap column B and must not get a spurious link");
    }

    [Fact]
    public void PasteLinkService_ContiguousSource_LinksEveryDestinationCell()
    {
        // No-regression sibling: without multi-area info (or a single area), every offset in the
        // bounding box is legitimately part of the copy, so every destination cell must still get
        // linked exactly as before this fix.
        var sheetId = SheetId.New();
        var sourceRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 1, 3));
        var destinationRange = new GridRange(new CellAddress(sheetId, 5, 5), new CellAddress(sheetId, 5, 7));

        var linkedCells = PasteLinkService.CreateLinkedCells(
            sourceRange,
            destination: destinationRange.Start,
            destinationRange: destinationRange,
            sourceSheetName: "Sheet1",
            transpose: false,
            sourceAreas: null);

        linkedCells.Should().HaveCount(3);
        linkedCells.Should().Contain(c => c.Address == new CellAddress(sheetId, 5, 6) && c.Cell.FormulaText == "Sheet1!B1");
    }

    [Fact]
    public void PasteCommentsCommand_MultiAreaSource_DoesNotLeakGapComment()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var a2 = new CellAddress(sheet.Id, 2, 1); // A2 -- selected, no comment
        var b2 = new CellAddress(sheet.Id, 2, 2); // B2 -- gap, never selected
        var c2 = new CellAddress(sheet.Id, 2, 3); // C2 -- selected
        sheet.Comments[b2] = "INTERNAL DRAFT - do not share";
        sheet.Comments[c2] = "keep me";

        var sourceRange = new GridRange(a2, c2); // bounding box A2:C2
        var sourceAreas = new List<GridRange> { new(a2, a2), new(c2, c2) };
        var destinationRange = new GridRange(new CellAddress(sheet.Id, 2, 5), new CellAddress(sheet.Id, 2, 7)); // E2:G2

        var command = new PasteCommentsCommand(sheet.Id, sourceRange, destinationRange, transpose: false, sourceAreas);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.Comments.Should().NotContainKey(new CellAddress(sheet.Id, 2, 6), "F2 aligns with the never-selected gap column B and must not receive its comment");
        sheet.Comments[new CellAddress(sheet.Id, 2, 7)].Should().Be("keep me");
    }

    [Fact]
    public void PasteCommentsCommand_ContiguousSource_StillCopiesEveryCommentInRange()
    {
        // No-regression sibling: an ordinary contiguous (single-area) copy has no gap -- every
        // commented cell in the bounding box really was selected, so it must still be copied
        // exactly as before this fix.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var a2 = new CellAddress(sheet.Id, 2, 1);
        var b2 = new CellAddress(sheet.Id, 2, 2);
        var c2 = new CellAddress(sheet.Id, 2, 3);
        sheet.Comments[b2] = "note";
        sheet.Comments[c2] = "keep me";

        var sourceRange = new GridRange(a2, c2);
        var destinationRange = new GridRange(new CellAddress(sheet.Id, 2, 5), new CellAddress(sheet.Id, 2, 7));

        var command = new PasteCommentsCommand(sheet.Id, sourceRange, destinationRange, transpose: false);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.Comments[new CellAddress(sheet.Id, 2, 6)].Should().Be("note", "a genuinely contiguous copy must still carry B2's comment to F2");
        sheet.Comments[new CellAddress(sheet.Id, 2, 7)].Should().Be("keep me");
    }

    [Fact]
    public void PasteDataValidationCommand_MultiAreaSource_DoesNotCopyGapOnlyRule()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 2), new CellAddress(sheet.Id, 1, 2)), // B1 -- gap, never selected
            Type = DvType.List,
            Formula1 = "Yes,No"
        });
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 3), new CellAddress(sheet.Id, 1, 3)), // C1 -- selected
            Type = DvType.List,
            Formula1 = "Red,Blue"
        });

        var sourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 3)); // bounding box A1:C1
        var sourceAreas = new List<GridRange>
        {
            new(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1)), // A1
            new(new CellAddress(sheet.Id, 1, 3), new CellAddress(sheet.Id, 1, 3)), // C1
        };
        var destinationRange = new GridRange(new CellAddress(sheet.Id, 1, 5), new CellAddress(sheet.Id, 1, 7)); // E1:G1

        var command = new PasteDataValidationCommand(sheet.Id, sourceRange, destinationRange, transpose: false, sourceAreas);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.DataValidations.Should().NotContain(rule => rule.Formula1 == "Yes,No" && rule.AppliesTo.Start.Col == 6,
            "F1 aligns with the never-selected gap column B and must not receive its validation rule");
        sheet.DataValidations.Should().Contain(rule =>
            rule.AppliesTo == new GridRange(new CellAddress(sheet.Id, 1, 7), new CellAddress(sheet.Id, 1, 7))
            && rule.Formula1 == "Red,Blue");
    }

    [Fact]
    public void PasteDataValidationCommand_ContiguousSource_StillCopiesRuleAnywhereInSourceRange()
    {
        // No-regression sibling: without multi-area info, a rule anywhere in the bounding box
        // (including its middle) really was part of the copy, so it must still be pasted exactly
        // as before this fix.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 2), new CellAddress(sheet.Id, 1, 2)), // B1
            Type = DvType.List,
            Formula1 = "Yes,No"
        });

        var sourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 3));
        var destinationRange = new GridRange(new CellAddress(sheet.Id, 1, 5), new CellAddress(sheet.Id, 1, 7));

        var command = new PasteDataValidationCommand(sheet.Id, sourceRange, destinationRange, transpose: false);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.DataValidations.Should().Contain(rule => rule.Formula1 == "Yes,No" && rule.AppliesTo.Start.Col == 6);
    }
}
