using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Round-12 fix bucket Q10 — focused regression tests for two adversarially-verified findings:
///   R12-clipboard-interop-1: external-clipboard Transpose paste mapped source axes using the
///     TRANSPOSED period, corrupting non-square blocks.
///   R12-xlsx-defined-names-2: deleting a sheet left sheet-scoped named RANGES that TARGET the
///     deleted sheet dangling (only the scope-side deletion was handled).
/// </summary>
public class FreeXR12Q10Tests
{
    // ── R12-clipboard-interop-1 ──────────────────────────────────────────────

    [Fact]
    public void ExternalTextPasteSpecial_WithTranspose_NonSquareColumnBlock_TransposesIntoRow()
    {
        // A single external column of 3 cells ["a";"b";"c"] (sourceRowCount=3, sourceColCount=1,
        // so sourceRowCount != sourceColCount — the square 2x2 case in the pre-existing test
        // masks this defect). Paste Special > Transpose must produce A1="a", B1="b", C1="c".
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var destination = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 1, 1));

        var rows = new List<IReadOnlyList<string>>
        {
            new List<string> { "a" },
            new List<string> { "b" },
            new List<string> { "c" },
        };

        var command = PasteCommandFactory.CreateExternalTextPasteCommand(
            sheet.Id,
            destination,
            rows,
            preserveText: true,
            new PasteSpecialOptions(Transpose: true));

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        sheet.GetValue(1, 1).Should().Be(new TextValue("a"));
        sheet.GetValue(1, 2).Should().Be(new TextValue("b"));
        sheet.GetValue(1, 3).Should().Be(new TextValue("c"));
    }

    [Fact]
    public void ExternalTextPasteSpecial_WithTranspose_NonSquare4x2Block_TransposesEachDistinctRow()
    {
        // A 4-row x 2-col external block. Before the fix, destination columns 2 and 3 wrongly
        // re-read source rows 0 and 1 (wrap period taken from the transposed paste geometry
        // instead of the source's own row count) instead of reading rows 2 and 3.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var destination = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 1, 1));

        var rows = new List<IReadOnlyList<string>>
        {
            new List<string> { "r0c0", "r0c1" },
            new List<string> { "r1c0", "r1c1" },
            new List<string> { "r2c0", "r2c1" },
            new List<string> { "r3c0", "r3c1" },
        };

        var command = PasteCommandFactory.CreateExternalTextPasteCommand(
            sheet.Id,
            destination,
            rows,
            preserveText: true,
            new PasteSpecialOptions(Transpose: true));

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        // Transposed: destination row = source column, destination column = source row.
        // Row 1 (dest) holds column 0 of every source row; row 2 (dest) holds column 1.
        sheet.GetValue(1, 1).Should().Be(new TextValue("r0c0"));
        sheet.GetValue(1, 2).Should().Be(new TextValue("r1c0"));
        sheet.GetValue(1, 3).Should().Be(new TextValue("r2c0"));
        sheet.GetValue(1, 4).Should().Be(new TextValue("r3c0"));
        sheet.GetValue(2, 1).Should().Be(new TextValue("r0c1"));
        sheet.GetValue(2, 2).Should().Be(new TextValue("r1c1"));
        sheet.GetValue(2, 3).Should().Be(new TextValue("r2c1"));
        sheet.GetValue(2, 4).Should().Be(new TextValue("r3c1"));
    }

    // ── R12-xlsx-defined-names-2 ─────────────────────────────────────────────

    [Fact]
    public void RemoveSheet_DropsScopedNamedRange_WhenItsTargetIsTheDeletedSheet_NotItsScope()
    {
        // 'Ref' is scoped to Sheet1 but its refers-to target is Sheet2!$A$1 (Excel and
        // XlsxNamedRangeMapper both allow a scoped name's target to differ from its scope sheet).
        // Deleting Sheet2 (the TARGET, not the scope) must drop 'Ref' too — a scoped range can't
        // be rewritten to "#REF!" the way a named-formula string can, so — mirroring the existing
        // workbook-global-range branch — the dangling scoped range must be removed rather than
        // silently surviving with a target sheet that no longer exists.
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");

        var targetOnSheet2 = new GridRange(
            new CellAddress(sheet2.Id, 1, 1),
            new CellAddress(sheet2.Id, 1, 1));
        wb.DefineNamedRange("Ref", targetOnSheet2, metadata: null, sheet1.Id);

        wb.ScopedNamedRanges.Should().ContainKey(("Ref", sheet1.Id));

        wb.RemoveSheet(sheet2.Id);

        wb.ScopedNamedRanges.Should().NotContainKey(("Ref", sheet1.Id));
    }

    [Fact]
    public void RemoveSheet_KeepsScopedNamedRange_WhenNeitherScopeNorTargetIsTheDeletedSheet()
    {
        // Sanity check: a scoped name unrelated to the deleted sheet (different scope, different
        // target) must survive the deletion untouched.
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");
        var sheet3 = wb.AddSheet("Sheet3");

        var targetOnSheet1 = new GridRange(
            new CellAddress(sheet1.Id, 2, 2),
            new CellAddress(sheet1.Id, 2, 2));
        wb.DefineNamedRange("Local", targetOnSheet1, metadata: null, sheet1.Id);

        wb.RemoveSheet(sheet3.Id);

        wb.ScopedNamedRanges.Should().ContainKey(("Local", sheet1.Id));
        wb.ScopedNamedRanges[("Local", sheet1.Id)].Should().Be(targetOnSheet1);
    }
}
