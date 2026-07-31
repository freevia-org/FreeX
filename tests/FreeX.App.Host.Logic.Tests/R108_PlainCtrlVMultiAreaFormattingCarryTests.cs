using FluentAssertions;
using FreeX.App.Presentation.Editing;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for the R108 "wiring left undone" fix
/// (src/FreeX.App.Host/MainWindow.ClipboardCommands.cs, ExecutePaste's CreatePasteCommand local
/// function, around the plain Ctrl+V PasteCommandFactory.CreateInternalPasteCommand call site).
///
/// r107 made a plain Ctrl+V carry the source's conditional-formatting and data-validation rules
/// along with it (PasteCommandFactory.CreateInternalPasteCommand's extraCommands/specialExtraCommands/
/// tiledExtraCommands branches). Both call sites determined "was this rule copied?" purely from
/// sourceRange -- the BOUNDING BOX of the copied selection -- with no way to know that a Ctrl+click
/// multi-area copy's bounding box can span an untouched GAP between two disjoint areas that was
/// never actually selected or copied. A rule anchored only in that gap would still be swept up and
/// pasted.
///
/// A `sourceAreas` parameter was already threaded all the way through
/// PasteCommandFactory.CreateInternalPasteCommand (both overloads) down to every
/// PasteDataValidationCommand construction site, AND PasteConditionalFormatsCommand was newly given
/// the identical `sourceAreas` mechanism (constructor parameter + per-area IntersectWithSource
/// helper, mirroring PasteDataValidationCommand.IntersectWithSource) as part of this same fix -- but
/// neither ever reached the real product entry point: ExecutePaste's plain-Ctrl+V call to
/// CreateInternalPasteCommand passed no sourceAreas argument, so the parameter always defaulted to
/// null and end-user behavior was unchanged regardless of how complete the parameter's plumbing was.
///
/// These tests go through the REAL ExecuteCopy/ExecutePaste entry points (not the factory directly)
/// specifically because a factory-level test cannot prove the wiring gap this fix closes -- the
/// factory always worked correctly once sourceAreas was supplied; the bug was that the real UI path
/// never supplied it.
/// </summary>
public sealed class R108_PlainCtrlVMultiAreaFormattingCarryTests
{
    private static DataValidation MakeDv(GridRange appliesTo) => new()
    {
        AppliesTo = appliesTo,
        Type = DvType.List,
        Formula1 = "\"A,B,C\""
    };

    private static ConditionalFormat MakeCf(GridRange appliesTo, string value1) => new()
    {
        AppliesTo = appliesTo,
        RuleType = CfRuleType.CellValue,
        Operator = CfOperator.GreaterThan,
        Value1 = value1,
        FormatIfTrue = new CellStyle { Bold = true }
    };

    /// <summary>
    /// The core failing-before-fix case, through the real Ctrl+C/Ctrl+V entry points: a Ctrl+click
    /// multi-area copy of A1:A2 and A4:A5 (bounding box A1:A5, with row 3 as the untouched gap) has
    /// a data-validation rule AND a conditional-format rule each anchored purely in the gap cell A3
    /// -- never part of either copied area. A plain Ctrl+V of that multi-area selection must NOT
    /// paste either rule to the destination.
    /// </summary>
    [Fact]
    public void PlainCtrlV_NonTiled_MultiArea_ExcludesGapCellRules()
    {
        StaTestRunner.RunClipboardIsolated(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var sheetId = sheet.Id;

                var area1 = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 2, 1)); // A1:A2
                var gapCell = new CellAddress(sheetId, 3, 1); // A3
                var area2 = new GridRange(new CellAddress(sheetId, 4, 1), new CellAddress(sheetId, 5, 1)); // A4:A5

                foreach (var address in new[]
                         {
                             new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 2, 1),
                             gapCell,
                             new CellAddress(sheetId, 4, 1), new CellAddress(sheetId, 5, 1)
                         })
                {
                    sheet.SetCell(address, Cell.FromValue(new NumberValue(address.Row)));
                }

                var gapDv = MakeDv(new GridRange(gapCell, gapCell));
                var gapCf = MakeCf(new GridRange(gapCell, gapCell), "100");
                sheet.DataValidations.Add(gapDv);
                sheet.ConditionalFormats.Add(gapCf);

                window.SheetGrid.SelectedRanges = new[] { area1, area2 };
                window.SheetGrid.SelectedRange = area2;
                R49MainWindowTestHarness.Invoke(window, "ExecuteCopy", false);

                var destinationStart = new CellAddress(sheetId, 1, 5); // E1
                window.SheetGrid.SelectedRanges = null;
                window.SheetGrid.SelectedRange = new GridRange(destinationStart, destinationStart);

                R49MainWindowTestHarness.Invoke(
                    window, "ExecutePaste", PasteMode.All, default(PasteSpecialOptions), false, false);

                // The values themselves still carry correctly (proving the paste actually ran and
                // this isn't a false negative from a no-op).
                sheet.GetCell(1, 5)!.Value.Should().Be(new NumberValue(1)); // area1's A1 -> E1

                // Neither the gap-only DV rule nor the gap-only CF rule was pasted -- only the
                // original two rules (still anchored at A3) remain.
                sheet.DataValidations.Should().ContainSingle();
                sheet.DataValidations[0].Id.Should().Be(gapDv.Id);
                sheet.ConditionalFormats.Should().ContainSingle();
                sheet.ConditionalFormats[0].Id.Should().Be(gapCf.Id);
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    /// <summary>
    /// Tiled counterpart: the same disjoint multi-area copy pasted onto a destination selection that
    /// is a whole multiple of the copied bounding box (tiling the values across it) must still
    /// exclude the gap-only rules, covering CreateTiledInternalPasteCommand's own CF/DV construction
    /// sites.
    /// </summary>
    [Fact]
    public void PlainCtrlV_Tiled_MultiArea_ExcludesGapCellRules()
    {
        StaTestRunner.RunClipboardIsolated(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var sheetId = sheet.Id;

                var area1 = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 1, 1)); // A1
                var gapCell = new CellAddress(sheetId, 2, 1); // A2
                var area2 = new GridRange(new CellAddress(sheetId, 3, 1), new CellAddress(sheetId, 3, 1)); // A3

                foreach (var address in new[] { new CellAddress(sheetId, 1, 1), gapCell, new CellAddress(sheetId, 3, 1) })
                    sheet.SetCell(address, Cell.FromValue(new NumberValue(address.Row)));

                var gapDv = MakeDv(new GridRange(gapCell, gapCell));
                var gapCf = MakeCf(new GridRange(gapCell, gapCell), "100");
                sheet.DataValidations.Add(gapDv);
                sheet.ConditionalFormats.Add(gapCf);

                window.SheetGrid.SelectedRanges = new[] { area1, area2 };
                window.SheetGrid.SelectedRange = area2;
                R49MainWindowTestHarness.Invoke(window, "ExecuteCopy", false);

                // 6-row destination selection = exactly 2 whole tiles of the 3-row bounding source
                // range (A1:A3).
                var destinationRange = new GridRange(new CellAddress(sheetId, 10, 5), new CellAddress(sheetId, 15, 5));
                window.SheetGrid.SelectedRanges = null;
                window.SheetGrid.SelectedRange = destinationRange;

                R49MainWindowTestHarness.Invoke(
                    window, "ExecutePaste", PasteMode.All, default(PasteSpecialOptions), false, false);

                sheet.GetCell(10, 5)!.Value.Should().Be(new NumberValue(1));

                sheet.DataValidations.Should().ContainSingle();
                sheet.DataValidations[0].Id.Should().Be(gapDv.Id);
                sheet.ConditionalFormats.Should().ContainSingle();
                sheet.ConditionalFormats[0].Id.Should().Be(gapCf.Id);
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    /// <summary>
    /// No-regression sibling: rules anchored fully INSIDE one of the actual copied areas (not the
    /// gap) must still be carried on a plain multi-area Ctrl+V, proving the fix only suppresses
    /// gap-only overlaps and does not regress genuine multi-area CF/DV carrying.
    /// </summary>
    [Fact]
    public void PlainCtrlV_NonTiled_MultiArea_StillCarriesRulesInsideCopiedAreas()
    {
        StaTestRunner.RunClipboardIsolated(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var sheetId = sheet.Id;

                var area1Cell = new CellAddress(sheetId, 1, 1); // A1
                var area2Cell = new CellAddress(sheetId, 3, 1); // A3 (row 2 is the gap, empty of rules here)
                var area1 = new GridRange(area1Cell, area1Cell);
                var area2 = new GridRange(area2Cell, area2Cell);

                sheet.SetCell(area1Cell, Cell.FromValue(new NumberValue(1)));
                sheet.SetCell(area2Cell, Cell.FromValue(new NumberValue(3)));

                var dvInArea1 = MakeDv(area1);
                var cfInArea2 = MakeCf(area2, "100");
                sheet.DataValidations.Add(dvInArea1);
                sheet.ConditionalFormats.Add(cfInArea2);

                window.SheetGrid.SelectedRanges = new[] { area1, area2 };
                window.SheetGrid.SelectedRange = area2;
                R49MainWindowTestHarness.Invoke(window, "ExecuteCopy", false);

                var destinationStart = new CellAddress(sheetId, 1, 5); // E1
                window.SheetGrid.SelectedRanges = null;
                window.SheetGrid.SelectedRange = new GridRange(destinationStart, destinationStart);

                R49MainWindowTestHarness.Invoke(
                    window, "ExecutePaste", PasteMode.All, default(PasteSpecialOptions), false, false);

                // area1's DV rule was carried to E1 (offset from area1's own A1 anchor).
                sheet.DataValidations.Should().HaveCount(2);
                var pastedDv = sheet.DataValidations.Should().ContainSingle(r => r.Id != dvInArea1.Id).Subject;
                pastedDv.AppliesTo.Should().Be(new GridRange(destinationStart, destinationStart));

                // area2's CF rule was carried too (offset from area2's own A3 anchor -- also maps
                // onto E1 since PasteCommandFactory anchors the whole formatting-carry payload at
                // the single paste destination, same as the existing non-multi-area CF/DV carry
                // behavior).
                sheet.ConditionalFormats.Should().HaveCount(2);
                var pastedCf = sheet.ConditionalFormats.Should().ContainSingle(r => r.Id != cfInArea2.Id).Subject;
                pastedCf.Value1.Should().Be("100");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    /// <summary>
    /// Straddling case: a rule whose range spans BOTH an actual copied area and the untouched gap
    /// (e.g. a DV rule over A2:A3 when only A1:A2 and A4:A5 were Ctrl+clicked, so A2 is copied but
    /// A3 is the gap) is carried only for the portion that overlaps an actual copied area -- the
    /// gap portion is excluded, exactly like the wholly-in-the-gap case above. This mirrors
    /// PasteDataValidationCommand's pre-existing per-area IntersectWithSource behavior
    /// (R78-commands-paste-special-5-4): Excel itself only ever lets a Ctrl+click multi-area copy
    /// consist of whole selected cells, so a rule is copied exactly to the extent its own range
    /// coincides with a SELECTED (not merely bounding-box-covered) cell -- never partially "rounded
    /// up" to include an unselected gap cell just because the rule happens to span both.
    /// </summary>
    [Fact]
    public void PlainCtrlV_NonTiled_MultiArea_StraddlingRuleCarriesOnlyCopiedAreaPortion()
    {
        StaTestRunner.RunClipboardIsolated(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var sheetId = sheet.Id;

                var area1 = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 2, 1)); // A1:A2
                var gapCell = new CellAddress(sheetId, 3, 1); // A3 (gap)
                var area2 = new GridRange(new CellAddress(sheetId, 4, 1), new CellAddress(sheetId, 5, 1)); // A4:A5

                foreach (var address in new[]
                         {
                             new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 2, 1),
                             gapCell,
                             new CellAddress(sheetId, 4, 1), new CellAddress(sheetId, 5, 1)
                         })
                {
                    sheet.SetCell(address, Cell.FromValue(new NumberValue(address.Row)));
                }

                // Straddles the area1/gap boundary: A2 (inside area1) through A3 (the gap).
                var straddlingRange = new GridRange(new CellAddress(sheetId, 2, 1), new CellAddress(sheetId, 3, 1));
                var straddlingDv = MakeDv(straddlingRange);
                sheet.DataValidations.Add(straddlingDv);

                window.SheetGrid.SelectedRanges = new[] { area1, area2 };
                window.SheetGrid.SelectedRange = area2;
                R49MainWindowTestHarness.Invoke(window, "ExecuteCopy", false);

                var destinationStart = new CellAddress(sheetId, 1, 5); // E1
                window.SheetGrid.SelectedRanges = null;
                window.SheetGrid.SelectedRange = new GridRange(destinationStart, destinationStart);

                R49MainWindowTestHarness.Invoke(
                    window, "ExecutePaste", PasteMode.All, default(PasteSpecialOptions), false, false);

                sheet.DataValidations.Should().HaveCount(2);
                var pasted = sheet.DataValidations.Should().ContainSingle(r => r.Id != straddlingDv.Id).Subject;
                // Only the A2 portion (inside area1) maps to the destination. The rule's offset is
                // computed relative to the whole bounding box's own start (A1, row 1) -- A2 is row
                // offset +1 from that anchor -- so it lands at E2 (destinationStart's row + 1), a
                // single cell, not a two-cell E1:E2 range that would result from also carrying the
                // gap's A3.
                var expectedPasted = new CellAddress(sheetId, destinationStart.Row + 1, destinationStart.Col);
                pasted.AppliesTo.Should().Be(new GridRange(expectedPasted, expectedPasted));
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    /// <summary>
    /// No-regression sibling: an ordinary single-area copy (the overwhelmingly common case, and
    /// what sourceAreas normalizes multi-area lists of Count &lt;= 1 down to) must still carry a
    /// CF/DV rule to the destination exactly as before this fix.
    /// </summary>
    [Fact]
    public void PlainCtrlV_SingleArea_StillCarriesRules()
    {
        StaTestRunner.RunClipboardIsolated(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var sheetId = sheet.Id;

                var sourceCell = new CellAddress(sheetId, 1, 1); // A1
                sheet.SetCell(sourceCell, Cell.FromValue(new NumberValue(42)));
                var dv = MakeDv(new GridRange(sourceCell, sourceCell));
                var cf = MakeCf(new GridRange(sourceCell, sourceCell), "100");
                sheet.DataValidations.Add(dv);
                sheet.ConditionalFormats.Add(cf);

                window.SheetGrid.SelectedRanges = null;
                window.SheetGrid.SelectedRange = new GridRange(sourceCell, sourceCell);
                R49MainWindowTestHarness.Invoke(window, "ExecuteCopy", false);

                var destinationStart = new CellAddress(sheetId, 1, 5); // E1
                window.SheetGrid.SelectedRange = new GridRange(destinationStart, destinationStart);

                R49MainWindowTestHarness.Invoke(
                    window, "ExecutePaste", PasteMode.All, default(PasteSpecialOptions), false, false);

                sheet.DataValidations.Should().HaveCount(2);
                sheet.ConditionalFormats.Should().HaveCount(2);
                var pastedDv = sheet.DataValidations.Should().ContainSingle(r => r.Id != dv.Id).Subject;
                pastedDv.AppliesTo.Should().Be(new GridRange(destinationStart, destinationStart));
                var pastedCf = sheet.ConditionalFormats.Should().ContainSingle(r => r.Id != cf.Id).Subject;
                pastedCf.AppliesTo.Should().Be(new GridRange(destinationStart, destinationStart));
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }
}
