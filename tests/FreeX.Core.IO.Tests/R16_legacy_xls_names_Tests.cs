using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using NPOI.HSSF.UserModel;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R16-defined-name-scope-routing-1: a BIFF NAME record with a non-zero itab (sheet scope) must be
/// registered as a sheet-scoped defined name (<see cref="Workbook.ScopedNamedRanges"/> /
/// <see cref="Workbook.ScopedNamedFormulas"/>) rather than collapsing into workbook-global scope.
/// Uses distinct global/scoped names — NPOI's HSSF writer garbles the RefersToFormula of two
/// same-text names, so same-name shadowing is asserted at the bucket-routing level (a scoped name
/// does not appear in the global dictionary), not via a fragile same-name round-trip.
/// </summary>
public sealed class R16_legacy_xls_names_Tests
{
    [Fact]
    public void Load_SheetScopedName_IsRegisteredUnderSheetScope_NotGlobal()
    {
        using var stream = CreateFixtureWithGlobalAndSheetScopedNames();
        var adapter = new LegacyXlsFileAdapter();

        var workbook = adapter.Load(stream);
        var sheet2 = workbook.GetSheetAt(1);

        // The workbook-global name is registered globally.
        workbook.NamedRanges.Should().ContainKey("GlobalFoo");

        // The Sheet2-scoped name is registered under sheet scope — NOT collapsed into the global
        // NamedRanges dictionary (the pre-fix bug). (Exact refersTo coordinates are not asserted:
        // NPOI's HSSF writer does not round-trip a SheetIndex-scoped name's RefersToFormula
        // faithfully; the routing to the scoped bucket is the behavior this fix guarantees.)
        workbook.ScopedNamedRanges.Should().ContainKey(("ScopedFoo", sheet2.Id));
        workbook.NamedRanges.Should().NotContainKey("ScopedFoo");

        // Sheet-scope-aware lookup resolves the scoped name for Sheet2.
        workbook.TryGetNamedRange("ScopedFoo", sheet2.Id, out _).Should().BeTrue();

        workbook.TryGetScopedNamedRangeMetadata("ScopedFoo", sheet2.Id, out var scopedMetadata).Should().BeTrue();
        scopedMetadata.Scope.Should().Be(sheet2.Name);
    }

    [Fact]
    public void Load_SheetScopedNamedFormula_IsRegisteredUnderSheetScope_NotGlobal()
    {
        using var stream = CreateFixtureWithGlobalAndSheetScopedFormulaNames();
        var adapter = new LegacyXlsFileAdapter();

        var workbook = adapter.Load(stream);
        var sheet2 = workbook.GetSheetAt(1);

        // The workbook-global formula name survives globally.
        workbook.NamedFormulas.Should().ContainKey("GlobalBar").WhoseValue.Should().Be("1+1");

        // The Sheet2-scoped formula name is tracked separately under sheet scope, not merged into
        // (or overwriting) the global NamedFormulas dictionary.
        workbook.ScopedNamedFormulas.Should().ContainKey(("ScopedBar", sheet2.Id));
        workbook.ScopedNamedFormulas[("ScopedBar", sheet2.Id)].Should().Be("2+2");
        workbook.NamedFormulas.Should().NotContainKey("ScopedBar");

        workbook.TryGetNamedFormulaText("ScopedBar", sheet2.Id).Should().Be("2+2");
    }

    private static MemoryStream CreateFixtureWithGlobalAndSheetScopedNames()
    {
        var hssf = new HSSFWorkbook();
        var sheet1 = hssf.CreateSheet("Sheet1");
        var sheet2 = hssf.CreateSheet("Sheet2");
        sheet1.CreateRow(0).CreateCell(0).SetCellValue(1d);
        sheet2.CreateRow(0).CreateCell(0).SetCellValue(2d);

        var globalName = hssf.CreateName();
        globalName.NameName = "GlobalFoo";
        globalName.RefersToFormula = "Sheet1!$A$1";

        var scopedName = hssf.CreateName();
        scopedName.SheetIndex = 1; // Sheet2 (0-based)
        scopedName.NameName = "ScopedFoo";
        scopedName.RefersToFormula = "Sheet2!$A$1";

        var stream = new MemoryStream();
        hssf.Write(stream, leaveOpen: true);
        stream.Position = 0;
        return stream;
    }

    private static MemoryStream CreateFixtureWithGlobalAndSheetScopedFormulaNames()
    {
        var hssf = new HSSFWorkbook();
        hssf.CreateSheet("Sheet1");
        hssf.CreateSheet("Sheet2");

        var globalName = hssf.CreateName();
        globalName.NameName = "GlobalBar";
        globalName.RefersToFormula = "1+1";

        var scopedName = hssf.CreateName();
        scopedName.SheetIndex = 1; // Sheet2 (0-based)
        scopedName.NameName = "ScopedBar";
        scopedName.RefersToFormula = "2+2";

        var stream = new MemoryStream();
        hssf.Write(stream, leaveOpen: true);
        stream.Position = 0;
        return stream;
    }
}
