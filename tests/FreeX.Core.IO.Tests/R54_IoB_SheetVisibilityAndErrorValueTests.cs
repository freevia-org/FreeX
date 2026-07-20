using ClosedXML.Excel;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round-54 io-b bucket: patch-save sheet visibility/tab-color/active-tab round-trip
/// (R54-io-sheet-tab-order-visibility-4-1) and error-value round-trip for the extended
/// Excel-365 error codes (R54-io-cell-error-value-4-1 / -4-2).
/// </summary>
public sealed class R54_IoB_SheetVisibilityAndErrorValueTests
{
    private static byte[] CreateTwoSheetSourcePackage()
    {
        using var stream = new MemoryStream();
        using (var workbook = new XLWorkbook())
        {
            var sheet1 = workbook.AddWorksheet("Data");
            sheet1.Cell("A1").Value = "original value";
            var sheet2 = workbook.AddWorksheet("Extra");
            sheet2.Cell("A1").Value = "extra value";
            workbook.SaveAs(stream);
        }

        return stream.ToArray();
    }

    private static void PrepareLoadedWorkbookForEdit(Workbook workbook)
    {
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);
    }

    [Fact]
    public void Save_LoadedWorkbookWithSheetHiddenOnlyEdit_PersistsHiddenFlag()
    {
        var sourceBytes = CreateTwoSheetSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        // Sole edit: hide the second sheet. No cell/dimension/merge/hyperlink/comment/view
        // change accompanies it, so this must not be silently discarded by the patch-save
        // "model unchanged" shortcut.
        var sheet2 = workbook.GetSheetAt(1);
        sheet2.IsHidden = true;

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();
        var diagPath = adapter.LastSaveDiagnostics.Path;
        var diagReason = adapter.LastSaveDiagnostics.Reason;

        using var reloadStream = new MemoryStream(savedBytes, writable: false);
        var reloaded = adapter.Load(reloadStream);
        reloaded.GetSheetAt(1).IsHidden.Should().BeTrue(
            $"hiding a sheet with no other edits must survive save/reload, matching Excel's behavior (path={diagPath}, reason={diagReason})");
    }

    [Fact]
    public void Save_LoadedWorkbookWithTabColorOnlyEdit_PersistsTabColor()
    {
        var sourceBytes = CreateTwoSheetSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet2 = workbook.GetSheetAt(1);
        sheet2.TabColor = new CellColor(255, 0, 0);

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        using var reloadStream = new MemoryStream(savedBytes, writable: false);
        var reloaded = adapter.Load(reloadStream);
        reloaded.GetSheetAt(1).TabColor.Should().Be(new CellColor(255, 0, 0),
            "setting a tab color with no other edits must survive save/reload");
    }

    [Fact]
    public void Save_LoadedWorkbookWithActiveSheetIndexOnlyEdit_PersistsActiveTab()
    {
        var sourceBytes = CreateTwoSheetSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        workbook.ActiveSheetIndex = 1;

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        using var reloadStream = new MemoryStream(savedBytes, writable: false);
        var reloaded = adapter.Load(reloadStream);
        reloaded.ActiveSheetIndex.Should().Be(1,
            "switching the active sheet with no other edits must survive save/reload");
    }

    [Fact]
    public void Save_LoadedWorkbookWithCellEditAndNoVisibilityChange_StillPatchesNormally()
    {
        // Sibling no-regression test: an ordinary cell-value-only edit (no sheet
        // hide/tab-color/active-tab change) must still patch-save and round-trip correctly.
        var sourceBytes = CreateTwoSheetSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet1 = workbook.GetSheetAt(0);
        sheet1.SetCell(new CellAddress(sheet1.Id, 1, 1), new TextValue("patched value"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        using var reloadStream = new MemoryStream(savedBytes, writable: false);
        var reloaded = adapter.Load(reloadStream);
        reloaded.GetSheetAt(0).GetCell(1, 1)!.Value.Should().Be(new TextValue("patched value"));
        reloaded.GetSheetAt(1).IsHidden.Should().BeFalse();
    }

    // Matches the established, deliberately-tested design for #SPILL!/#CALC! in
    // XlsxClosedXmlCellMapperErrorRoundTripTests.cs: ClosedXML's XLError enum has no member for
    // these codes, so MapValueInverse preserves the exact code as visible TEXT on save rather than
    // silently downgrading to a different, wrong-but-valid error (#N/A). A saved cell that reads
    // literally "#FIELD!"/"#GETTING_DATA"/etc. is honest about what happened; #N/A is not.
    [Theory]
    [InlineData("#FIELD!")]
    [InlineData("#CONNECT!")]
    [InlineData("#UNKNOWN!")]
    [InlineData("#BLOCKED!")]
    [InlineData("#GETTING_DATA")]
    public void Save_PlainValueCellWithExtendedErrorCode_RoundTripsAsVisibleTextNotNA(string errorCode)
    {
        var adapter = new XlsxFileAdapter();
        var workbook = new Workbook();
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new ErrorValue(errorCode));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        using var reloadStream = new MemoryStream(saved.ToArray(), writable: false);
        var reloaded = adapter.Load(reloadStream);
        var reloadedValue = reloaded.GetSheetAt(0).GetCell(1, 1)!.Value;
        reloadedValue.Should().Be(new TextValue(errorCode),
            $"{errorCode} is a real Excel-365 error code FreeX already models and must not be downgraded to #N/A");
        reloadedValue.Should().NotBe(new ErrorValue("#N/A"));
    }

    [Fact]
    public void Save_PlainValueCellWithClassicErrorCode_StillRoundTrips()
    {
        // Sibling no-regression test: the already-correctly-handled classic error codes (which DO
        // have a matching XLError member) must keep round-tripping as a true error value, not text
        // -- not accidentally rerouted by the extended-code fix.
        var adapter = new XlsxFileAdapter();
        var workbook = new Workbook();
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new ErrorValue("#DIV/0!"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        using var reloadStream = new MemoryStream(saved.ToArray(), writable: false);
        var reloaded = adapter.Load(reloadStream);
        reloaded.GetSheetAt(0).GetCell(1, 1)!.Value.Should().Be(new ErrorValue("#DIV/0!"));
    }

    [Theory]
    [InlineData("#FIELD!")]
    [InlineData("#CONNECT!")]
    [InlineData("#UNKNOWN!")]
    [InlineData("#BLOCKED!")]
    [InlineData("#GETTING_DATA")]
    public void SlkRoundTrip_ExtendedErrorCode_StaysAnErrorValue(string errorCode)
    {
        var adapter = new SlkFileAdapter();
        var workbook = new Workbook();
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new ErrorValue(errorCode));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        using var reloadStream = new MemoryStream(saved.ToArray(), writable: false);
        var reloaded = adapter.Load(reloadStream);
        reloaded.GetSheetAt(0).GetCell(1, 1)!.Value.Should().Be(new ErrorValue(errorCode),
            $"{errorCode} must survive SYLK round-trip as an error value, not be reclassified as text");
    }

    [Fact]
    public void SlkRoundTrip_ClassicErrorCode_StillAnErrorValue()
    {
        // Sibling no-regression test for the SLK adapter's existing classic-code handling.
        var adapter = new SlkFileAdapter();
        var workbook = new Workbook();
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new ErrorValue("#VALUE!"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        using var reloadStream = new MemoryStream(saved.ToArray(), writable: false);
        var reloaded = adapter.Load(reloadStream);
        reloaded.GetSheetAt(0).GetCell(1, 1)!.Value.Should().Be(new ErrorValue("#VALUE!"));
    }
}
