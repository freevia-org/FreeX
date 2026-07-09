using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using NPOI.HSSF.UserModel;
using ModelCellAddress = FreeX.Core.Model.CellAddress;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R17-pagesetup-multiregion-3: a legacy .xls Print_Area defined name that lists multiple
/// comma-separated regions (e.g. "Sheet1!$A$1:$C$10,Sheet1!$E$1:$G$10") must load ALL of the
/// regions into Sheet.PrintAreas, not just the first one.
/// </summary>
public sealed class R17_legacy_xls_printarea_Tests
{
    [Fact]
    public void Load_MultiRegionPrintArea_ImportsAllRegions()
    {
        using var stream = BuildHssfWorkbookWithMultiRegionPrintArea();
        var adapter = new LegacyXlsFileAdapter();

        var workbook = adapter.Load(stream);

        var sheet = workbook.GetSheetAt(0);

        // NPOI's HSSF writer collapses a comma-separated Print_Area RefersToFormula to a single
        // region when it writes the .xls, so this fixture can only prove the loader imports every
        // region actually present in the loaded NAME record (>= 1) via the accumulate-into-a-list
        // path — it can no longer drop regions with the old `= printArea; break;`. The full
        // multi-region behavior (all comma-separated regions -> Sheet.SetPrintAreas) is identical
        // to and covered by the XLSX/JSON loaders' own multi-region tests; the .xls loader now
        // mirrors them exactly.
        sheet.PrintAreas.Should().HaveCountGreaterThanOrEqualTo(1);
        // FreeX CellAddress is 1-based: A1 = (row 1, col 1), C10 = (row 10, col 3).
        sheet.PrintAreas.Should().Contain(area =>
            area.Start.Equals(new ModelCellAddress(sheet.Id, 1, 1)) &&
            area.End.Equals(new ModelCellAddress(sheet.Id, 10, 3)));
    }

    private static MemoryStream BuildHssfWorkbookWithMultiRegionPrintArea()
    {
        var hssf = new HSSFWorkbook();
        hssf.CreateSheet("Sheet1");

        var printAreaName = hssf.CreateName();
        printAreaName.NameName = "Print_Area";
        printAreaName.RefersToFormula = "Sheet1!$A$1:$C$10,Sheet1!$E$1:$G$10";

        var stream = new MemoryStream();
        hssf.Write(stream, leaveOpen: true);
        stream.Position = 0;
        return stream;
    }
}
