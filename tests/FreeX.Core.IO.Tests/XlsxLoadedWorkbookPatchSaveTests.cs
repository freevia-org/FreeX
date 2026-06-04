using System.IO.Compression;
using System.Xml.Linq;
using ClosedXML.Excel;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxLoadedWorkbookPatchSaveTests
{
    public static TheoryData<ScalarValue, string?, string?> FormulaCachedValueCases => new()
    {
        { new NumberValue(99.5), null, "99.5" },
        { new TextValue("cached text"), "str", "cached text" },
        { new BoolValue(true), "b", "1" },
        { new ErrorValue("#N/A"), "e", "#N/A" },
        { BlankValue.Instance, null, null }
    };

    [Fact]
    public void Save_LoadedWorkbookWithExistingLiteralCellEdit_PatchesSourcePackage()
    {
        var sourceBytes = CreateSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("  patched value  "));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        ReadPackageEntry(savedBytes, "xl/workbook.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/workbook.xml"));
        ReadPackageEntry(savedBytes, "xl/styles.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/styles.xml"));
        ReadCellText(savedBytes, "xl/worksheets/sheet1.xml", "A1")
            .Should()
            .Be("  patched value  ");
        ReadCellTextSpaceMode(savedBytes, "xl/worksheets/sheet1.xml", "A1")
            .Should()
            .Be("preserve");

        using var reloadStream = new MemoryStream(savedBytes, writable: false);
        adapter.Load(reloadStream)
            .GetSheetAt(0)
            .GetCell(1, 1)!
            .Value
            .Should()
            .Be(new TextValue("  patched value  "));
    }

    [Fact]
    public void Save_LoadedUnchangedWorkbook_ReportsSourceCopyDiagnostics()
    {
        var sourceBytes = CreateSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourceCopy);
        adapter.LastSaveDiagnostics.PathLabel.Should().Be("source_copy");
        adapter.LastSaveDiagnostics.Reason.Should().Be("model_unchanged");
    }

    [Fact]
    public void Save_NewWorkbook_ReportsNoSourcePackageFullSaveDiagnostics()
    {
        var adapter = new XlsxFileAdapter();
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("new"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);
        adapter.LastSaveDiagnostics.PathLabel.Should().Be("full_save");
        adapter.LastSaveDiagnostics.Reason.Should().Be("no_source_package");
    }

    [Fact]
    public void Save_LoadedWorkbookWithNewLiteralCellEdit_PatchesSourcePackage()
    {
        var sourceBytes = CreateSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 4, 4), new TextValue("new value"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.PathLabel.Should().Be("source_patch");
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        adapter.LastSaveDiagnostics.CellChangeCount.Should().Be(1);
        adapter.LastSaveDiagnostics.TotalPatchChangeCount.Should().Be(1);
        ReadPackageEntry(savedBytes, "xl/workbook.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/workbook.xml"));
        ReadPackageEntry(savedBytes, "xl/styles.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/styles.xml"));
        ReadWorksheetDimension(savedBytes, "xl/worksheets/sheet1.xml")
            .Should()
            .Be("A1:D4");
        ReadCellText(savedBytes, "xl/worksheets/sheet1.xml", "D4")
            .Should()
            .Be("new value");

        using var reloadStream = new MemoryStream(savedBytes, writable: false);
        adapter.Load(reloadStream)
            .GetSheetAt(0)
            .GetCell(4, 4)!
            .Value
            .Should()
            .Be(new TextValue("new value"));
    }

    [Fact]
    public void Save_LoadedWorkbookWithExistingCellStyleEdit_PatchesSourcePackage()
    {
        var sourceBytes = CreateStyledSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);

        var sheet = workbook.GetSheetAt(0);
        var sourceStyleCell = sheet.GetCell(1, 2);
        sourceStyleCell.Should().NotBeNull();
        sourceStyleCell!.StyleId.Should().NotBe(StyleId.Default);
        var patchedCell = sheet.GetCell(1, 1);
        patchedCell.Should().NotBeNull();
        patchedCell!.StyleId = sourceStyleCell.StyleId;

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        ReadPackageEntry(savedBytes, "xl/workbook.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/workbook.xml"));
        ReadPackageEntry(savedBytes, "xl/styles.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/styles.xml"));
        ReadCellStyleIndex(savedBytes, "xl/worksheets/sheet1.xml", "A1")
            .Should()
            .Be(ReadCellStyleIndex(sourceBytes, "xl/worksheets/sheet1.xml", "B1"));

        using var reloadStream = new MemoryStream(savedBytes, writable: false);
        var reloaded = adapter.Load(reloadStream);
        var reloadedSheet = reloaded.GetSheetAt(0);
        reloadedSheet.GetCell(1, 1)!.Value
            .Should()
            .Be(new TextValue("plain"));
        reloaded.GetStyle(reloadedSheet.GetCell(1, 1)!.StyleId)
            .Should()
            .Be(reloaded.GetStyle(reloadedSheet.GetCell(1, 2)!.StyleId));
    }

    [Fact]
    public void Save_LoadedWorkbookWithExistingStyleOnlyStyleEdit_PatchesSourcePackage()
    {
        var sourceBytes = CreateStyledSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);

        var sheet = workbook.GetSheetAt(0);
        var styleOnlyStyleId = sheet.GetStyleOnly(1, 4);
        styleOnlyStyleId.Should().NotBeNull();
        var patchedCell = sheet.GetCell(1, 1);
        patchedCell.Should().NotBeNull();
        patchedCell!.StyleId = styleOnlyStyleId!.Value;

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        ReadPackageEntry(savedBytes, "xl/workbook.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/workbook.xml"));
        ReadPackageEntry(savedBytes, "xl/styles.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/styles.xml"));
        ReadCellStyleIndex(savedBytes, "xl/worksheets/sheet1.xml", "A1")
            .Should()
            .Be(ReadCellStyleIndex(sourceBytes, "xl/worksheets/sheet1.xml", "D1"));

        using var reloadStream = new MemoryStream(savedBytes, writable: false);
        var reloaded = adapter.Load(reloadStream);
        var reloadedSheet = reloaded.GetSheetAt(0);
        var reloadedStyleOnlyStyleId = reloadedSheet.GetStyleOnly(1, 4);
        reloadedStyleOnlyStyleId.Should().NotBeNull();
        reloaded.GetStyle(reloadedSheet.GetCell(1, 1)!.StyleId)
            .Should()
            .Be(reloaded.GetStyle(reloadedStyleOnlyStyleId!.Value));
    }

    [Fact]
    public void Save_LoadedWorkbookWithNewCellStyleEdit_FallsBackToFullSave()
    {
        var sourceBytes = CreateSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);

        var sheet = workbook.GetSheetAt(0);
        var styleId = workbook.RegisterStyle(new CellStyle
        {
            Bold = true,
            FillColor = new CellColor(221, 235, 247)
        });
        sheet.GetCell(1, 1)!.StyleId = styleId;

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);
        adapter.LastSaveDiagnostics.PathLabel.Should().Be("full_save");
        adapter.LastSaveDiagnostics.Reason.Should().Be("change_new_style");
        ReadPackageEntry(savedBytes, "xl/styles.xml")
            .Should()
            .NotEqual(ReadPackageEntry(sourceBytes, "xl/styles.xml"));

        using var reloadStream = new MemoryStream(savedBytes, writable: false);
        var reloaded = adapter.Load(reloadStream);
        reloaded.GetStyle(reloaded.GetSheetAt(0).GetCell(1, 1)!.StyleId)
            .Bold
            .Should()
            .BeTrue();
    }

    [Fact]
    public void Save_LoadedWorkbookWithRowColumnDimensionEdit_PatchesSourcePackage()
    {
        var sourceBytes = CreateSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);

        var sheet = workbook.GetSheetAt(0);
        sheet.RowHeights[2] = 32;
        sheet.HiddenRows.Add(4);
        sheet.ColumnWidths[2] = 18.5;
        sheet.HiddenCols.Add(3);

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        ReadPackageEntry(savedBytes, "xl/workbook.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/workbook.xml"));
        ReadPackageEntry(savedBytes, "xl/styles.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/styles.xml"));
        ReadRowAttribute(savedBytes, "xl/worksheets/sheet1.xml", 2, "ht")
            .Should()
            .Be("24");
        ReadRowAttribute(savedBytes, "xl/worksheets/sheet1.xml", 2, "customHeight")
            .Should()
            .Be("1");
        ReadRowAttribute(savedBytes, "xl/worksheets/sheet1.xml", 4, "hidden")
            .Should()
            .Be("1");
        ReadColumnAttribute(savedBytes, "xl/worksheets/sheet1.xml", 2, "width")
            .Should()
            .Be("18.5");
        ReadColumnAttribute(savedBytes, "xl/worksheets/sheet1.xml", 2, "customWidth")
            .Should()
            .Be("1");
        ReadColumnAttribute(savedBytes, "xl/worksheets/sheet1.xml", 3, "hidden")
            .Should()
            .Be("1");

        using var reloadStream = new MemoryStream(savedBytes, writable: false);
        var reloaded = adapter.Load(reloadStream).GetSheetAt(0);
        reloaded.RowHeights[2].Should().BeApproximately(32, 0.0001);
        reloaded.HiddenRows.Should().Contain(4u);
        reloaded.ColumnWidths[2].Should().BeApproximately(18.5, 0.0001);
        reloaded.HiddenCols.Should().Contain(3u);
    }

    [Fact]
    public void Save_LoadedWorkbookWithMergedRegionEdit_PatchesSourcePackage()
    {
        var sourceBytes = CreateMergedRegionSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);

        var sheet = workbook.GetSheetAt(0);
        sheet.MergedRegions.Should().HaveCount(2);
        sheet.RemoveMergedRegion(sheet.MergedRegions[0]).Should().BeTrue();
        sheet.AddMergedRegion(new GridRange(
            new CellAddress(sheet.Id, 3, 1),
            new CellAddress(sheet.Id, 4, 2)));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        ReadPackageEntry(savedBytes, "xl/workbook.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/workbook.xml"));
        ReadPackageEntry(savedBytes, "xl/styles.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/styles.xml"));
        ReadMergeCellsAttribute(savedBytes, "xl/worksheets/sheet1.xml", "nativeMergeContainerAttr")
            .Should()
            .Be("kept");
        ReadMergeCellsAttribute(savedBytes, "xl/worksheets/sheet1.xml", "count")
            .Should()
            .Be("2");
        ReadMergeCellReferences(savedBytes, "xl/worksheets/sheet1.xml")
            .Should()
            .Equal("C1:D1", "A3:B4");
        ReadMergeCellAttribute(savedBytes, "xl/worksheets/sheet1.xml", "C1:D1", "nativeMergeCellAttr")
            .Should()
            .Be("kept-C1-D1");
        ReadMergeCellAttribute(savedBytes, "xl/worksheets/sheet1.xml", "A3:B4", "nativeMergeCellAttr")
            .Should()
            .BeNull();

        using var reloadStream = new MemoryStream(savedBytes, writable: false);
        var reloaded = adapter.Load(reloadStream).GetSheetAt(0);
        reloaded.MergedRegions
            .Select(region => region.ToString())
            .Should()
            .Equal("C1:D1", "A3:B4");
    }

    [Fact]
    public void Save_LoadedWorkbookWithInternalHyperlinkEdit_PatchesSourcePackage()
    {
        var sourceBytes = CreateInternalHyperlinkSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);

        var sheet = workbook.GetSheetAt(0);
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.Hyperlinks[address].Should().Be("Data!B2");
        sheet.HyperlinkMetadata[address].Should().Be(new HyperlinkMetadata(
            HyperlinkTargetKind.PlaceInThisDocument,
            "Jump original",
            "Data!B2"));
        sheet.Hyperlinks[address] = "Data!C3";
        sheet.HyperlinkMetadata[address] = new HyperlinkMetadata(
            HyperlinkTargetKind.PlaceInThisDocument,
            "Jump patched",
            "Data!C3");

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        ReadPackageEntry(savedBytes, "xl/workbook.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/workbook.xml"));
        ReadPackageEntry(savedBytes, "xl/styles.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/styles.xml"));
        ReadHyperlinksAttribute(savedBytes, "xl/worksheets/sheet1.xml", "nativeHyperlinksAttr")
            .Should()
            .Be("kept");
        ReadHyperlinkAttribute(savedBytes, "xl/worksheets/sheet1.xml", "A1", "location")
            .Should()
            .Be("Data!C3");
        ReadHyperlinkAttribute(savedBytes, "xl/worksheets/sheet1.xml", "A1", "tooltip")
            .Should()
            .Be("Jump patched");
        ReadHyperlinkAttribute(savedBytes, "xl/worksheets/sheet1.xml", "A1", "display")
            .Should()
            .Be("Jump display");
        ReadHyperlinkAttribute(savedBytes, "xl/worksheets/sheet1.xml", "A1", "customAttr")
            .Should()
            .Be("kept-link");

        using var reloadStream = new MemoryStream(savedBytes, writable: false);
        var reloadedSheet = adapter.Load(reloadStream).GetSheetAt(0);
        var reloadedAddress = new CellAddress(reloadedSheet.Id, 1, 1);
        reloadedSheet.Hyperlinks[reloadedAddress].Should().Be("Data!C3");
        reloadedSheet.HyperlinkMetadata[reloadedAddress].Should().Be(new HyperlinkMetadata(
            HyperlinkTargetKind.PlaceInThisDocument,
            "Jump patched",
            "Data!C3"));
    }

    [Fact]
    public void Save_LoadedWorkbookWithLegacyCommentTextEdit_PatchesSourcePackage()
    {
        var sourceBytes = CreateLegacyCommentSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);

        var sheet = workbook.GetSheetAt(0);
        var address = new CellAddress(sheet.Id, 2, 3);
        sheet.Comments[address].Should().Be("Original note");
        sheet.Comments[address] = "Patched note";

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        ReadPackageEntry(savedBytes, "xl/workbook.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/workbook.xml"));
        ReadPackageEntry(savedBytes, "xl/worksheets/sheet1.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/worksheets/sheet1.xml"));
        ReadPackageEntry(savedBytes, "xl/worksheets/_rels/sheet1.xml.rels")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/worksheets/_rels/sheet1.xml.rels"));
        ReadPackageEntry(savedBytes, "xl/drawings/vmlDrawing1.vml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/drawings/vmlDrawing1.vml"));
        ReadCommentText(savedBytes, "xl/comments1.xml", "C2")
            .Should()
            .Be("Patched note");
        ReadCommentAttribute(savedBytes, "xl/comments1.xml", "C2", "nativeCommentAttr")
            .Should()
            .Be("kept-comment");

        using var reloadStream = new MemoryStream(savedBytes, writable: false);
        var reloadedSheet = adapter.Load(reloadStream).GetSheetAt(0);
        var reloadedAddress = new CellAddress(reloadedSheet.Id, 2, 3);
        reloadedSheet.Comments[reloadedAddress].Should().Be("Patched note");
    }

    [Fact]
    public void Save_LoadedWorkbookWithLegacyCommentAndCellEdit_PatchesSourcePackage()
    {
        var sourceBytes = CreateLegacyCommentSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("cell patched"));
        sheet.Comments[new CellAddress(sheet.Id, 2, 3)] = "Comment patched";

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        ReadPackageEntry(savedBytes, "xl/workbook.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/workbook.xml"));
        ReadCellText(savedBytes, "xl/worksheets/sheet1.xml", "A1")
            .Should()
            .Be("cell patched");
        ReadCommentText(savedBytes, "xl/comments1.xml", "C2")
            .Should()
            .Be("Comment patched");
        ReadPackageEntry(savedBytes, "xl/drawings/vmlDrawing1.vml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/drawings/vmlDrawing1.vml"));
    }

    [Fact]
    public void Save_LoadedWorkbookWithAddedLegacyComment_FallsBackToFullSave()
    {
        var sourceBytes = CreateLegacyCommentSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);

        var sheet = workbook.GetSheetAt(0);
        sheet.Comments[new CellAddress(sheet.Id, 1, 1)] = "New note";

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        ReadPackageEntry(savedBytes, "xl/workbook.xml")
            .Should()
            .NotEqual(ReadPackageEntry(sourceBytes, "xl/workbook.xml"));
    }

    [Fact]
    public void Save_LoadedWorkbookWithNonNoteVmlLegacyCommentEdit_FallsBackToFullSave()
    {
        var sourceBytes = CreateLegacyCommentSourcePackage(vmlObjectType: "Button");
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);

        var sheet = workbook.GetSheetAt(0);
        sheet.Comments[new CellAddress(sheet.Id, 2, 3)] = "Patched note";

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        ReadPackageEntry(savedBytes, "xl/workbook.xml")
            .Should()
            .NotEqual(ReadPackageEntry(sourceBytes, "xl/workbook.xml"));
    }

    [Fact]
    public void Save_LoadedWorkbookWithStructuredTableDataBodyEdit_PatchesSourcePackage()
    {
        var sourceBytes = CreateStructuredTableSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);

        var sheet = workbook.GetSheetAt(0);
        sheet.StructuredTables.Should().ContainSingle();
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(99));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        ReadPackageEntry(savedBytes, "xl/workbook.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/workbook.xml"));
        ReadPackageEntry(savedBytes, "xl/worksheets/_rels/sheet1.xml.rels")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/worksheets/_rels/sheet1.xml.rels"));
        ReadPackageEntry(savedBytes, "xl/tables/table1.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/tables/table1.xml"));
        ReadCellText(savedBytes, "xl/worksheets/sheet1.xml", "B2")
            .Should()
            .Be("99");

        using var reloadStream = new MemoryStream(savedBytes, writable: false);
        var reloadedSheet = adapter.Load(reloadStream).GetSheetAt(0);
        reloadedSheet.GetCell(2, 2)!.Value.Should().Be(new NumberValue(99));
        reloadedSheet.StructuredTables.Should().ContainSingle()
            .Which.Range.ToString().Should().Be("A1:B3");
    }

    [Fact]
    public void Save_LoadedWorkbookWithStructuredTableOutsideTableEdit_PatchesSourcePackage()
    {
        var sourceBytes = CreateStructuredTableSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);

        var sheet = workbook.GetSheetAt(0);
        sheet.StructuredTables.Should().ContainSingle();
        sheet.SetCell(new CellAddress(sheet.Id, 4, 3), new TextValue("outside patched"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        ReadPackageEntry(savedBytes, "xl/workbook.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/workbook.xml"));
        ReadPackageEntry(savedBytes, "xl/worksheets/_rels/sheet1.xml.rels")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/worksheets/_rels/sheet1.xml.rels"));
        ReadPackageEntry(savedBytes, "xl/tables/table1.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/tables/table1.xml"));
        ReadCellText(savedBytes, "xl/worksheets/sheet1.xml", "C4")
            .Should()
            .Be("outside patched");
    }

    [Fact]
    public void Save_LoadedWorkbookWithStructuredTableHeaderEdit_FallsBackToFullSave()
    {
        var sourceBytes = CreateStructuredTableSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Changed"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        ReadPackageEntry(savedBytes, "xl/workbook.xml")
            .Should()
            .NotEqual(ReadPackageEntry(sourceBytes, "xl/workbook.xml"));
    }

    [Fact]
    public void Save_LoadedWorkbookWithFilteredStructuredTableDataBodyEdit_FallsBackToFullSave()
    {
        var sourceBytes = CreateStructuredTableSourcePackage(includeFilter: true);
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);

        var sheet = workbook.GetSheetAt(0);
        sheet.StructuredTables.Should().ContainSingle()
            .Which.FilterColumns.Should().ContainSingle();
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(99));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        ReadPackageEntry(savedBytes, "xl/workbook.xml")
            .Should()
            .NotEqual(ReadPackageEntry(sourceBytes, "xl/workbook.xml"));
    }

    [Fact]
    public void Save_LoadedWorkbookWithChartAndOutsideCellEdit_PatchesSourcePackage()
    {
        var sourceBytes = CreateChartSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);

        var sheet = workbook.GetSheetAt(0);
        sheet.Charts.Should().ContainSingle();
        sheet.SetCell(new CellAddress(sheet.Id, 4, 4), new TextValue("outside patched"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        ReadPackageEntry(savedBytes, "xl/workbook.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/workbook.xml"));
        ReadPackageEntry(savedBytes, "xl/drawings/drawing1.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/drawings/drawing1.xml"));
        ReadPackageEntry(savedBytes, "xl/drawings/_rels/drawing1.xml.rels")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/drawings/_rels/drawing1.xml.rels"));
        ReadPackageEntry(savedBytes, "xl/charts/chart1.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/charts/chart1.xml"));
        ReadCellText(savedBytes, "xl/worksheets/sheet1.xml", "D4")
            .Should()
            .Be("outside patched");

        using var reloadStream = new MemoryStream(savedBytes, writable: false);
        var reloadedSheet = adapter.Load(reloadStream).GetSheetAt(0);
        reloadedSheet.GetCell(4, 4)!.Value.Should().Be(new TextValue("outside patched"));
        reloadedSheet.Charts.Should().ContainSingle();
    }

    [Fact]
    public void Save_LoadedWorkbookWithChartSourceCellEdit_FallsBackToFullSave()
    {
        var sourceBytes = CreateChartSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);

        var sheet = workbook.GetSheetAt(0);
        sheet.Charts.Should().ContainSingle();
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(99));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        saved.Length.Should().BeGreaterThan(0);
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);
        adapter.LastSaveDiagnostics.Reason.Should().Be("change_chart_source_cell");
    }

    [Fact]
    public void Save_LoadedWorkbookWithPivotAndOutsideCellEdit_PatchesSourcePackage()
    {
        var sourceBytes = CreatePivotSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);

        var sheet = workbook.GetSheetAt(0);
        sheet.PivotTables.Should().ContainSingle();
        sheet.SetCell(new CellAddress(sheet.Id, 4, 4), new TextValue("outside patched"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        ReadPackageEntry(savedBytes, "xl/pivotCache/pivotCacheDefinition1.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/pivotCache/pivotCacheDefinition1.xml"));
        ReadPackageEntry(savedBytes, "xl/pivotTables/pivotTable1.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/pivotTables/pivotTable1.xml"));
        ReadCellText(savedBytes, "xl/worksheets/sheet1.xml", "D4")
            .Should()
            .Be("outside patched");

        using var reloadStream = new MemoryStream(savedBytes, writable: false);
        var reloadedSheet = adapter.Load(reloadStream).GetSheetAt(0);
        reloadedSheet.GetCell(4, 4)!.Value.Should().Be(new TextValue("outside patched"));
        reloadedSheet.PivotTables.Should().ContainSingle();
    }

    [Fact]
    public void Save_LoadedWorkbookWithPivotSourceCellEdit_FallsBackToFullSave()
    {
        var sourceBytes = CreatePivotSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);

        var sheet = workbook.GetSheetAt(0);
        sheet.PivotTables.Should().ContainSingle();
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(99));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        saved.Length.Should().BeGreaterThan(0);
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);
        adapter.LastSaveDiagnostics.Reason.Should().Be("change_pivot_source_cell");
    }

    [Fact]
    public void Save_LoadedWorkbookWithClearedLiteralCell_PatchesSourcePackage()
    {
        var sourceBytes = CreateSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);

        var sheet = workbook.GetSheetAt(0);
        sheet.ClearCell(2, 2);

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        ReadPackageEntry(savedBytes, "xl/workbook.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/workbook.xml"));
        ReadPackageEntry(savedBytes, "xl/styles.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/styles.xml"));
        TryReadCellElement(savedBytes, "xl/worksheets/sheet1.xml", "B2")
            .Should()
            .BeNull();

        using var reloadStream = new MemoryStream(savedBytes, writable: false);
        adapter.Load(reloadStream)
            .GetSheetAt(0)
            .GetCell(2, 2)
            .Should()
            .BeNull();
    }

    [Theory]
    [MemberData(nameof(FormulaCachedValueCases))]
    public void Save_LoadedWorkbookWithFormulaCachedValueEdit_PatchesFormulaCache(
        ScalarValue cachedValue,
        string? expectedCellType,
        string? expectedRawValue)
    {
        var sourceBytes = CreateFormulaSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);

        var cell = workbook.GetSheetAt(0).GetCell(1, 1)!;
        cell.FormulaText.Should().Be("1+1");
        cell.Value = cachedValue;

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        ReadPackageEntry(savedBytes, "xl/workbook.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/workbook.xml"));
        ReadPackageEntry(savedBytes, "xl/calcChain.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/calcChain.xml"));
        ReadCellFormula(savedBytes, "xl/worksheets/sheet1.xml", "A1")
            .Should()
            .Be("1+1");
        ReadCellType(savedBytes, "xl/worksheets/sheet1.xml", "A1")
            .Should()
            .Be(expectedCellType);
        ReadCellText(savedBytes, "xl/worksheets/sheet1.xml", "A1")
            .Should()
            .Be(expectedRawValue);
    }

    [Fact]
    public void Save_LoadedWorkbookWithFormulaTextEdit_PatchesFormulaAndDropsCalcChain()
    {
        var sourceBytes = CreateFormulaSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);

        var cell = workbook.GetSheetAt(0).GetCell(1, 1)!;
        cell.FormulaText = "1+2";
        cell.Value = new NumberValue(3);

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        ReadPackageEntry(savedBytes, "xl/workbook.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/workbook.xml"));
        PackageHasEntry(savedBytes, "xl/calcChain.xml").Should().BeFalse();
        ReadContentTypeOverrides(savedBytes).Should().NotContain("/xl/calcChain.xml");
        ReadWorkbookRelationshipTypes(savedBytes)
            .Should()
            .NotContain("http://schemas.openxmlformats.org/officeDocument/2006/relationships/calcChain");
        ReadCellFormula(savedBytes, "xl/worksheets/sheet1.xml", "A1")
            .Should()
            .Be("1+2");
        ReadCellText(savedBytes, "xl/worksheets/sheet1.xml", "A1")
            .Should()
            .Be("3");
    }

    [Fact]
    public void Save_LoadedWorkbookWithClearedFormulaCell_PatchesSourcePackageAndDropsCalcChain()
    {
        var sourceBytes = CreateFormulaSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);

        var sheet = workbook.GetSheetAt(0);
        sheet.ClearCell(1, 1);

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        ReadPackageEntry(savedBytes, "xl/workbook.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/workbook.xml"));
        PackageHasEntry(savedBytes, "xl/calcChain.xml").Should().BeFalse();
        ReadContentTypeOverrides(savedBytes).Should().NotContain("/xl/calcChain.xml");
        TryReadCellElement(savedBytes, "xl/worksheets/sheet1.xml", "A1")
            .Should()
            .BeNull();
    }

    [Fact]
    public void Save_LoadedWorkbookWithAttributedFormulaTextEdit_FallsBackToFullSave()
    {
        var sourceBytes = CreateFormulaSourcePackage("""<f t="shared" si="0">1+1</f>""");
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);

        var cell = workbook.GetSheetAt(0).GetCell(1, 1)!;
        cell.FormulaText = "1+2";
        cell.Value = new NumberValue(3);

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        ReadPackageEntry(savedBytes, "xl/workbook.xml")
            .Should()
            .NotEqual(ReadPackageEntry(sourceBytes, "xl/workbook.xml"));
        ReadCellFormula(savedBytes, "xl/worksheets/sheet1.xml", "A1")
            .Should()
            .Be("1+2");
    }

    [Fact]
    public void Save_LoadedWorkbookWithWorksheetMetadataEdit_FallsBackToFullSave()
    {
        var sourceBytes = CreateSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);

        var sheet = workbook.GetSheetAt(0);
        sheet.ShowGridlines = false;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("patched value"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        var reloaded = adapter.Load(saved).GetSheetAt(0);
        reloaded.ShowGridlines.Should().BeFalse();
        reloaded.GetCell(1, 1)!.Value.Should().Be(new TextValue("patched value"));
    }

    [Fact]
    public void Save_LoadedWorkbookWithCustomWorkbookViewsAndCellEdit_PatchesSourcePackageAndRemovesCustomViews()
    {
        var sourceBytes = AddCustomWorkbookViews(CreateSourcePackage());
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("patched value"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        ReadWorkbookCustomWorkbookViews(savedBytes).Should().BeNull();
        ReadCellText(savedBytes, "xl/worksheets/sheet1.xml", "A1")
            .Should()
            .Be("patched value");
        ReadPackageEntry(savedBytes, "xl/styles.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/styles.xml"));
    }

    [Fact]
    public void Save_LoadedWorkbookWithNativeOnlyCustomSheetViewsAndCellEdit_PatchesSourcePackageAndRemovesCustomViews()
    {
        var sourceBytes = AddMinimalCustomSheetViews(CreateSourcePackage());
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);

        workbook.CustomViews.Should().BeEmpty();
        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("patched value"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        ReadWorksheetCustomSheetViews(savedBytes, "xl/worksheets/sheet1.xml").Should().BeNull();
        ReadCellText(savedBytes, "xl/worksheets/sheet1.xml", "A1")
            .Should()
            .Be("patched value");
    }

    [Fact]
    public void Save_LoadedWorkbookWithModeledCustomViewsAndCellEdit_PatchesSourcePackageAndPreservesCustomViews()
    {
        var sourceBytes = AddMatchedCustomViews(CreateSourcePackage());
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);

        workbook.CustomViews.Should().ContainSingle();
        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("patched value"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        ReadWorkbookCustomWorkbookViews(savedBytes).Should().NotBeNull();
        ReadWorksheetCustomSheetViews(savedBytes, "xl/worksheets/sheet1.xml").Should().NotBeNull();
        ReadCellText(savedBytes, "xl/worksheets/sheet1.xml", "A1")
            .Should()
            .Be("patched value");

        using var reloadStream = new MemoryStream(savedBytes, writable: false);
        adapter.Load(reloadStream).CustomViews.Should().ContainSingle();
    }

    [Fact]
    public void Save_LoadedWorkbookWithRichSharedStringFontAndCellEdit_PatchesSourcePackageAndSanitizesFont()
    {
        var sourceBytes = AddCssFontFamilyRichSharedString(CreateSourcePackage());
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(456));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        ReadRichSharedStringFontValue(savedBytes, "original value")
            .Should()
            .Be("Google Sans");
    }

    private static byte[] CreateSourcePackage()
    {
        using var stream = new MemoryStream();
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.AddWorksheet("Data");
            sheet.Cell("A1").Value = "original value";
            sheet.Cell("B2").Value = 123.45;
            sheet.Cell("C3").Value = true;
            workbook.SaveAs(stream);
        }

        return stream.ToArray();
    }

    private static byte[] AddCssFontFamilyRichSharedString(byte[] sourceBytes)
    {
        using var stream = new MemoryStream();
        stream.Write(sourceBytes, 0, sourceBytes.Length);
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            var sharedStringsEntry = archive.GetEntry("xl/sharedStrings.xml");
            sharedStringsEntry.Should().NotBeNull();
            XDocument sharedStringsXml;
            using (var sharedStringsStream = sharedStringsEntry!.Open())
                sharedStringsXml = XDocument.Load(sharedStringsStream);

            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var sharedString = sharedStringsXml.Root!
                .Elements(workbookNs + "si")
                .Single(element => element.Element(workbookNs + "t")?.Value == "original value");
            sharedString.ReplaceNodes(new XElement(
                workbookNs + "r",
                new XElement(
                    workbookNs + "rPr",
                    new XElement(workbookNs + "rFont", new XAttribute("val", "\"Google Sans\", Roboto, sans-serif")),
                    new XElement(workbookNs + "sz", new XAttribute("val", "11"))),
                new XElement(workbookNs + "t", "original value")));

            sharedStringsEntry.Delete();
            var replacement = archive.CreateEntry("xl/sharedStrings.xml");
            using var replacementStream = replacement.Open();
            sharedStringsXml.Save(replacementStream, System.Xml.Linq.SaveOptions.DisableFormatting);
        }

        return stream.ToArray();
    }

    private static byte[] AddMinimalCustomSheetViews(byte[] sourceBytes)
    {
        return UpdateWorksheetXml(sourceBytes, worksheetXml =>
        {
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            worksheetXml.Root!.AddFirst(new XElement(
                worksheetNs + "customSheetViews",
                new XElement(
                    worksheetNs + "customSheetView",
                    new XAttribute("guid", "{11111111-1111-1111-1111-111111111111}"),
                    new XAttribute("view", "normal"),
                    new XAttribute("scale", "120"),
                    new XAttribute("state", "visible"))));
        });
    }

    private static byte[] AddMatchedCustomViews(byte[] sourceBytes)
    {
        var withWorkbookViews = AddCustomWorkbookViews(sourceBytes);
        return UpdateWorksheetXml(withWorkbookViews, worksheetXml =>
        {
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            worksheetXml.Root!.AddFirst(new XElement(
                worksheetNs + "customSheetViews",
                new XElement(
                    worksheetNs + "customSheetView",
                    new XAttribute("guid", "{22222222-2222-2222-2222-222222222222}"),
                    new XAttribute("view", "normal"),
                    new XAttribute("scale", "120"),
                    new XAttribute("showGridLines", "0"),
                    new XAttribute("state", "visible"),
                    new XElement(
                        worksheetNs + "pane",
                        new XAttribute("xSplit", "1"),
                        new XAttribute("ySplit", "1"),
                        new XAttribute("state", "split")))));
        });
    }

    private static byte[] UpdateWorksheetXml(byte[] sourceBytes, Action<XDocument> update)
    {
        using var stream = new MemoryStream();
        stream.Write(sourceBytes, 0, sourceBytes.Length);
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            var worksheetEntry = archive.GetEntry("xl/worksheets/sheet1.xml");
            worksheetEntry.Should().NotBeNull();
            XDocument worksheetXml;
            using (var worksheetStream = worksheetEntry!.Open())
                worksheetXml = XDocument.Load(worksheetStream);

            update(worksheetXml);

            worksheetEntry.Delete();
            var replacement = archive.CreateEntry("xl/worksheets/sheet1.xml");
            using var replacementStream = replacement.Open();
            worksheetXml.Save(replacementStream, System.Xml.Linq.SaveOptions.DisableFormatting);
        }

        return stream.ToArray();
    }

    private static byte[] AddCustomWorkbookViews(byte[] sourceBytes)
    {
        using var stream = new MemoryStream();
        stream.Write(sourceBytes, 0, sourceBytes.Length);
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            var workbookEntry = archive.GetEntry("xl/workbook.xml");
            workbookEntry.Should().NotBeNull();
            XDocument workbookXml;
            using (var workbookStream = workbookEntry!.Open())
                workbookXml = XDocument.Load(workbookStream);

            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            workbookXml.Root!.Add(new XElement(
                workbookNs + "customWorkbookViews",
                new XElement(
                    workbookNs + "customWorkbookView",
                    new XAttribute("name", "FreeXView"),
                    new XAttribute("guid", "{22222222-2222-2222-2222-222222222222}"),
                    new XAttribute("autoUpdate", "0"),
                    new XAttribute("mergeInterval", "0"),
                    new XAttribute("personalView", "0"),
                    new XAttribute("includePrintSettings", "1"),
                    new XAttribute("includeHiddenRowCol", "1"))));

            workbookEntry.Delete();
            var replacement = archive.CreateEntry("xl/workbook.xml");
            using var replacementStream = replacement.Open();
            workbookXml.Save(replacementStream, System.Xml.Linq.SaveOptions.DisableFormatting);
        }

        return stream.ToArray();
    }

    private static XElement? ReadWorkbookCustomWorkbookViews(byte[] packageBytes)
    {
        using var stream = new MemoryStream(packageBytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var entry = archive.GetEntry("xl/workbook.xml");
        entry.Should().NotBeNull();
        using var entryStream = entry!.Open();
        var document = XDocument.Load(entryStream);
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        return document.Root!.Element(workbookNs + "customWorkbookViews");
    }

    private static XElement? ReadWorksheetCustomSheetViews(byte[] packageBytes, string worksheetPath)
    {
        using var stream = new MemoryStream(packageBytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var entry = archive.GetEntry(worksheetPath);
        entry.Should().NotBeNull();
        using var entryStream = entry!.Open();
        var document = XDocument.Load(entryStream);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        return document.Root!.Element(worksheetNs + "customSheetViews");
    }

    private static string? ReadRichSharedStringFontValue(byte[] packageBytes, string plainText)
    {
        using var stream = new MemoryStream(packageBytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        entry.Should().NotBeNull();
        using var entryStream = entry!.Open();
        var document = XDocument.Load(entryStream);
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        return document.Root!
            .Elements(workbookNs + "si")
            .Single(element => string.Concat(element.Descendants(workbookNs + "t").Select(text => text.Value)) == plainText)
            .Element(workbookNs + "r")?
            .Element(workbookNs + "rPr")?
            .Element(workbookNs + "rFont")?
            .Attribute("val")
            ?.Value;
    }

    private static byte[] CreateStyledSourcePackage()
    {
        using var stream = new MemoryStream();
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.AddWorksheet("Data");
            sheet.Cell("A1").Value = "plain";
            sheet.Cell("B1").Value = "styled";
            sheet.Cell("B1").Style.Font.Bold = true;
            sheet.Cell("B1").Style.Fill.BackgroundColor = XLColor.FromArgb(221, 235, 247);
            sheet.Cell("D1").Style.Font.Italic = true;
            workbook.SaveAs(stream);
        }

        return stream.ToArray();
    }

    private static byte[] CreateMergedRegionSourcePackage()
    {
        using var stream = new MemoryStream();
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.AddWorksheet("Data");
            sheet.Cell("A1").Value = "merged 1";
            sheet.Cell("C1").Value = "merged 2";
            sheet.Range("A1:B1").Merge();
            sheet.Range("C1:D1").Merge();
            workbook.SaveAs(stream);
        }

        stream.Position = 0;
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            var worksheetEntry = archive.GetEntry("xl/worksheets/sheet1.xml");
            worksheetEntry.Should().NotBeNull();
            XDocument worksheetXml;
            using (var worksheetStream = worksheetEntry!.Open())
                worksheetXml = XDocument.Load(worksheetStream);

            var worksheetNs = worksheetXml.Root!.Name.Namespace;
            var mergeCells = worksheetXml.Root.Element(worksheetNs + "mergeCells");
            mergeCells.Should().NotBeNull();
            mergeCells!.SetAttributeValue("nativeMergeContainerAttr", "kept");
            foreach (var mergeCell in mergeCells.Elements(worksheetNs + "mergeCell"))
            {
                var reference = mergeCell.Attribute("ref")?.Value;
                if (reference == "A1:B1")
                    mergeCell.SetAttributeValue("nativeMergeCellAttr", "kept-A1-B1");
                else if (reference == "C1:D1")
                    mergeCell.SetAttributeValue("nativeMergeCellAttr", "kept-C1-D1");
            }

            worksheetEntry.Delete();
            var replacement = archive.CreateEntry("xl/worksheets/sheet1.xml");
            using var replacementStream = replacement.Open();
            worksheetXml.Save(replacementStream, System.Xml.Linq.SaveOptions.DisableFormatting);
        }

        return stream.ToArray();
    }

    private static byte[] CreateInternalHyperlinkSourcePackage()
    {
        using var package = XlsxPackageTestFixtures.CreatePackage(
            (
                "[Content_Types].xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                  <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
                  <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                </Types>
                """),
            (
                "_rels/.rels",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
                </Relationships>
                """),
            (
                "xl/workbook.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets>
                    <sheet name="Data" sheetId="1" r:id="rId1"/>
                  </sheets>
                </workbook>
                """),
            (
                "xl/_rels/workbook.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
                  <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
                </Relationships>
                """),
            (
                "xl/styles.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <fonts count="1"><font><sz val="11"/><color theme="1"/><name val="Calibri"/><family val="2"/><scheme val="minor"/></font></fonts>
                  <fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills>
                  <borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders>
                  <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
                  <cellXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/></cellXfs>
                  <cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles>
                  <dxfs count="0"/>
                  <tableStyles count="0" defaultTableStyle="TableStyleMedium2" defaultPivotStyle="PivotStyleLight16"/>
                </styleSheet>
                """),
            (
                "xl/worksheets/sheet1.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <dimension ref="A1:C3"/>
                  <sheetData>
                    <row r="1"><c r="A1" t="inlineStr"><is><t>Jump</t></is></c></row>
                    <row r="2"><c r="B2"><v>1</v></c></row>
                    <row r="3"><c r="C3"><v>2</v></c></row>
                  </sheetData>
                  <hyperlinks nativeHyperlinksAttr="kept">
                    <hyperlink ref="A1" location="Data!B2" tooltip="Jump original" display="Jump display" customAttr="kept-link"/>
                  </hyperlinks>
                </worksheet>
                """));

        return package.ToArray();
    }

    private static byte[] CreateLegacyCommentSourcePackage(string vmlObjectType = "Note")
    {
        using var package = XlsxPackageTestFixtures.CreatePackage(
            (
                "[Content_Types].xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Default Extension="vml" ContentType="application/vnd.openxmlformats-officedocument.vmlDrawing"/>
                  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                  <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
                  <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                  <Override PartName="/xl/comments1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.comments+xml"/>
                </Types>
                """),
            (
                "_rels/.rels",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
                </Relationships>
                """),
            (
                "xl/workbook.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets>
                    <sheet name="Data" sheetId="1" r:id="rId1"/>
                  </sheets>
                </workbook>
                """),
            (
                "xl/_rels/workbook.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
                  <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
                </Relationships>
                """),
            (
                "xl/styles.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <fonts count="1"><font><sz val="11"/><color theme="1"/><name val="Calibri"/><family val="2"/><scheme val="minor"/></font></fonts>
                  <fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills>
                  <borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders>
                  <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
                  <cellXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/></cellXfs>
                  <cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles>
                  <dxfs count="0"/>
                  <tableStyles count="0" defaultTableStyle="TableStyleMedium2" defaultPivotStyle="TableStyleLight16"/>
                </styleSheet>
                """),
            (
                "xl/worksheets/sheet1.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <dimension ref="A1:C2"/>
                  <sheetData>
                    <row r="1"><c r="A1" t="inlineStr"><is><t>source</t></is></c></row>
                    <row r="2"><c r="C2" t="inlineStr"><is><t>review</t></is></c></row>
                  </sheetData>
                  <legacyDrawing r:id="rId2"/>
                </worksheet>
                """),
            (
                "xl/worksheets/_rels/sheet1.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/comments" Target="../comments1.xml"/>
                  <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/vmlDrawing" Target="../drawings/vmlDrawing1.vml"/>
                </Relationships>
                """),
            (
                "xl/comments1.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <comments xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <authors>
                    <author>Excel Reviewer</author>
                  </authors>
                  <commentList nativeCommentListAttr="kept-list">
                    <comment ref="C2" authorId="0" nativeCommentAttr="kept-comment">
                      <text><r><t>Original note</t></r></text>
                    </comment>
                  </commentList>
                </comments>
                """),
            (
                "xl/drawings/vmlDrawing1.vml",
                $$"""
                <?xml version="1.0" encoding="UTF-8"?>
                <xml xmlns:v="urn:schemas-microsoft-com:vml" xmlns:o="urn:schemas-microsoft-com:office:office" xmlns:x="urn:schemas-microsoft-com:office:excel">
                  <v:shape id="_x0000_s1025" type="#_x0000_t202" style="position:absolute;margin-left:80pt;margin-top:6pt;width:108pt;height:59.25pt;z-index:1;visibility:hidden" fillcolor="#ffffe1" o:insetmode="auto">
                    <v:fill color2="#ffffe1"/>
                    <v:shadow color="black" obscured="t"/>
                    <v:path o:connecttype="none"/>
                    <v:textbox style="mso-direction-alt:auto"><div style="text-align:left"/></v:textbox>
                    <x:ClientData ObjectType="{{vmlObjectType}}">
                      <x:MoveWithCells/>
                      <x:SizeWithCells/>
                      <x:Anchor>2, 15, 1, 2, 4, 15, 5, 3</x:Anchor>
                      <x:AutoFill>False</x:AutoFill>
                      <x:Row>1</x:Row>
                      <x:Column>2</x:Column>
                    </x:ClientData>
                  </v:shape>
                </xml>
                """));

        return package.ToArray();
    }

    private static byte[] CreateStructuredTableSourcePackage(bool includeFilter = false)
    {
        using var package = XlsxPackageTestFixtures.CreatePackage(
            (
                "[Content_Types].xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                  <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
                  <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                  <Override PartName="/xl/tables/table1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.table+xml"/>
                </Types>
                """),
            (
                "_rels/.rels",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
                </Relationships>
                """),
            (
                "xl/workbook.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets>
                    <sheet name="Data" sheetId="1" r:id="rId1"/>
                  </sheets>
                </workbook>
                """),
            (
                "xl/_rels/workbook.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
                  <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
                </Relationships>
                """),
            (
                "xl/styles.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <fonts count="1"><font><sz val="11"/><color theme="1"/><name val="Calibri"/><family val="2"/><scheme val="minor"/></font></fonts>
                  <fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills>
                  <borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders>
                  <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
                  <cellXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/></cellXfs>
                  <cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles>
                  <dxfs count="0"/>
                  <tableStyles count="0" defaultTableStyle="TableStyleMedium2" defaultPivotStyle="TableStyleLight16"/>
                </styleSheet>
                """),
            (
                "xl/worksheets/sheet1.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <dimension ref="A1:C4"/>
                  <sheetData>
                    <row r="1"><c r="A1" t="inlineStr"><is><t>Category</t></is></c><c r="B1" t="inlineStr"><is><t>Amount</t></is></c></row>
                    <row r="2"><c r="A2" t="inlineStr"><is><t>East</t></is></c><c r="B2"><v>10</v></c></row>
                    <row r="3"><c r="A3" t="inlineStr"><is><t>West</t></is></c><c r="B3"><v>20</v></c></row>
                    <row r="4"><c r="C4" t="inlineStr"><is><t>outside</t></is></c></row>
                  </sheetData>
                  <tableParts count="1"><tablePart r:id="rId1"/></tableParts>
                </worksheet>
                """),
            (
                "xl/worksheets/_rels/sheet1.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/table" Target="../tables/table1.xml"/>
                </Relationships>
                """),
            (
                "xl/tables/table1.xml",
                CreateStructuredTableXml(includeFilter)));

        return package.ToArray();
    }

    private static byte[] CreateChartSourcePackage()
    {
        using var package = XlsxPackageTestFixtures.CreatePackage(
            (
                "[Content_Types].xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                  <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
                  <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                  <Override PartName="/xl/drawings/drawing1.xml" ContentType="application/vnd.openxmlformats-officedocument.drawing+xml"/>
                  <Override PartName="/xl/charts/chart1.xml" ContentType="application/vnd.openxmlformats-officedocument.drawingml.chart+xml"/>
                </Types>
                """),
            (
                "_rels/.rels",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
                </Relationships>
                """),
            (
                "xl/workbook.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets>
                    <sheet name="Data" sheetId="1" r:id="rId1"/>
                  </sheets>
                </workbook>
                """),
            (
                "xl/_rels/workbook.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
                  <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
                </Relationships>
                """),
            (
                "xl/styles.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <fonts count="1"><font><sz val="11"/><color theme="1"/><name val="Calibri"/><family val="2"/><scheme val="minor"/></font></fonts>
                  <fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills>
                  <borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders>
                  <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
                  <cellXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/></cellXfs>
                  <cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles>
                  <dxfs count="0"/>
                  <tableStyles count="0" defaultTableStyle="TableStyleMedium2" defaultPivotStyle="PivotStyleLight16"/>
                </styleSheet>
                """),
            (
                "xl/worksheets/sheet1.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <dimension ref="A1:D4"/>
                  <sheetData>
                    <row r="1"><c r="A1" t="inlineStr"><is><t>Region</t></is></c><c r="B1" t="inlineStr"><is><t>Sales</t></is></c></row>
                    <row r="2"><c r="A2" t="inlineStr"><is><t>East</t></is></c><c r="B2"><v>10</v></c></row>
                    <row r="3"><c r="A3" t="inlineStr"><is><t>West</t></is></c><c r="B3"><v>20</v></c></row>
                    <row r="4"><c r="D4" t="inlineStr"><is><t>outside</t></is></c></row>
                  </sheetData>
                  <drawing r:id="rId1"/>
                </worksheet>
                """),
            (
                "xl/worksheets/_rels/sheet1.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing" Target="../drawings/drawing1.xml"/>
                </Relationships>
                """),
            (
                "xl/drawings/drawing1.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <xdr:wsDr xmlns:xdr="http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"
                          xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                          xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <xdr:twoCellAnchor>
                    <xdr:from><xdr:col>3</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>0</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from>
                    <xdr:to><xdr:col>8</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>10</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:to>
                    <xdr:graphicFrame macro="">
                      <xdr:nvGraphicFramePr>
                        <xdr:cNvPr id="2" name="Sales Chart"/>
                        <xdr:cNvGraphicFramePr/>
                      </xdr:nvGraphicFramePr>
                      <xdr:xfrm>
                        <a:off x="0" y="0"/>
                        <a:ext cx="0" cy="0"/>
                      </xdr:xfrm>
                      <a:graphic>
                        <a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/chart">
                          <c:chart r:id="rId1"/>
                        </a:graphicData>
                      </a:graphic>
                    </xdr:graphicFrame>
                    <xdr:clientData/>
                  </xdr:twoCellAnchor>
                </xdr:wsDr>
                """),
            (
                "xl/drawings/_rels/drawing1.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart" Target="../charts/chart1.xml"/>
                </Relationships>
                """),
            (
                "xl/charts/chart1.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                              xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
                  <c:chart>
                    <c:title><c:tx><c:rich><a:p><a:r><a:t>Sales</a:t></a:r></a:p></c:rich></c:tx></c:title>
                    <c:plotArea>
                      <c:barChart>
                        <c:barDir val="col"/>
                        <c:ser>
                          <c:idx val="0"/>
                          <c:order val="0"/>
                          <c:tx><c:strRef><c:f>Data!$B$1</c:f></c:strRef></c:tx>
                          <c:cat><c:strRef><c:f>Data!$A$2:$A$3</c:f></c:strRef></c:cat>
                          <c:val><c:numRef><c:f>Data!$B$2:$B$3</c:f></c:numRef></c:val>
                        </c:ser>
                      </c:barChart>
                    </c:plotArea>
                  </c:chart>
                </c:chartSpace>
                """));

        return package.ToArray();
    }

    private static byte[] CreatePivotSourcePackage()
    {
        var workbook = new Workbook("PivotPatch");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("B"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 4), new TextValue("outside"));

        var sourceRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 2));
        var cache = new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = sheet.Name,
            SourceReference = sourceRange.ToString(),
            PackagePart = "xl/pivotCache/pivotCacheDefinition1.xml",
            RecordCount = 2,
            CreatedVersion = 8,
            MinRefreshableVersion = 4
        };
        cache.Fields.Add(new PivotCacheFieldModel("Category"));
        cache.Fields.Add(new PivotCacheFieldModel("Amount", 4));
        workbook.PivotCaches.Add(cache);

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = sourceRange,
            TargetRange = new GridRange(
                new CellAddress(sheet.Id, 6, 4),
                new CellAddress(sheet.Id, 9, 5)),
            PackagePart = "xl/pivotTables/pivotTable1.xml"
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum", 4));
        sheet.PivotTables.Add(pivot);

        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        return stream.ToArray();
    }

    private static string CreateStructuredTableXml(bool includeFilter) =>
        """
        <?xml version="1.0" encoding="UTF-8"?>
        <table xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" id="1" name="Table1" displayName="Table1" ref="A1:B3" totalsRowShown="0">
          AUTOFILTER
          <tableColumns count="2">
            <tableColumn id="1" name="Category"/>
            <tableColumn id="2" name="Amount"/>
          </tableColumns>
          <tableStyleInfo name="TableStyleMedium2" showFirstColumn="0" showLastColumn="0" showRowStripes="1" showColumnStripes="0"/>
        </table>
        """.Replace(
            "AUTOFILTER",
            includeFilter
                ? """
                  <autoFilter ref="A1:B3"><filterColumn colId="0"><filters><filter val="East"/></filters></filterColumn></autoFilter>
                  """
                : """<autoFilter ref="A1:B3"/>""",
            StringComparison.Ordinal);

    private static byte[] CreateFormulaSourcePackage(string formulaElement = "<f>1+1</f>")
    {
        using var package = XlsxPackageTestFixtures.CreatePackage(
            (
                "[Content_Types].xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                  <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
                  <Override PartName="/xl/calcChain.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.calcChain+xml"/>
                  <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                </Types>
                """),
            (
                "_rels/.rels",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
                </Relationships>
                """),
            (
                "xl/workbook.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets>
                    <sheet name="Data" sheetId="1" r:id="rId1"/>
                  </sheets>
                  <calcPr calcId="191029"/>
                </workbook>
                """),
            (
                "xl/_rels/workbook.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
                  <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
                  <Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/calcChain" Target="calcChain.xml"/>
                </Relationships>
                """),
            (
                "xl/styles.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <fonts count="1"><font><sz val="11"/><color theme="1"/><name val="Calibri"/><family val="2"/><scheme val="minor"/></font></fonts>
                  <fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills>
                  <borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders>
                  <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
                  <cellXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/></cellXfs>
                  <cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles>
                  <dxfs count="0"/>
                  <tableStyles count="0" defaultTableStyle="TableStyleMedium2" defaultPivotStyle="PivotStyleLight16"/>
                </styleSheet>
                """),
            (
                "xl/calcChain.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <calcChain xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <c r="A1" i="1"/>
                </calcChain>
                """),
            (
                "xl/worksheets/sheet1.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <dimension ref="A1:A1"/>
                  <sheetData>
                    <row r="1"><c r="A1">FORMULA_ELEMENT<v>2</v></c></row>
                  </sheetData>
                </worksheet>
                """.Replace("FORMULA_ELEMENT", formulaElement, StringComparison.Ordinal)));

        return package.ToArray();
    }

    private static byte[] ReadPackageEntry(byte[] packageBytes, string path)
    {
        using var stream = new MemoryStream(packageBytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var entry = archive.GetEntry(path);
        entry.Should().NotBeNull();
        using var entryStream = entry!.Open();
        using var bytes = new MemoryStream();
        entryStream.CopyTo(bytes);
        return bytes.ToArray();
    }

    private static bool PackageHasEntry(byte[] packageBytes, string path)
    {
        using var stream = new MemoryStream(packageBytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        return archive.GetEntry(path) is not null;
    }

    private static IReadOnlyList<string> ReadContentTypeOverrides(byte[] packageBytes)
    {
        using var stream = new MemoryStream(packageBytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var document = XlsxPackageTestFixtures.LoadPackageXml(archive, "[Content_Types].xml");
        var ns = document.Root!.Name.Namespace;
        return document.Root!
            .Elements(ns + "Override")
            .Select(element => element.Attribute("PartName")?.Value ?? "")
            .ToList();
    }

    private static IReadOnlyList<string> ReadWorkbookRelationshipTypes(byte[] packageBytes)
    {
        using var stream = new MemoryStream(packageBytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var document = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/_rels/workbook.xml.rels");
        var ns = document.Root!.Name.Namespace;
        return document.Root!
            .Elements(ns + "Relationship")
            .Select(element => element.Attribute("Type")?.Value ?? "")
            .ToList();
    }

    private static string? ReadCellText(byte[] packageBytes, string worksheetPath, string reference)
    {
        var cell = ReadCellElement(packageBytes, worksheetPath, reference);
        var ns = cell.Name.Namespace;
        if (string.Equals(cell.Attribute("t")?.Value, "inlineStr", StringComparison.Ordinal))
            return cell.Element(ns + "is")?.Element(ns + "t")?.Value;

        return cell.Element(ns + "v")?.Value;
    }

    private static string? ReadCellFormula(byte[] packageBytes, string worksheetPath, string reference)
    {
        var cell = ReadCellElement(packageBytes, worksheetPath, reference);
        var ns = cell.Name.Namespace;
        return cell.Element(ns + "f")?.Value;
    }

    private static string? ReadCellType(byte[] packageBytes, string worksheetPath, string reference) =>
        ReadCellElement(packageBytes, worksheetPath, reference).Attribute("t")?.Value;

    private static string? ReadCellStyleIndex(byte[] packageBytes, string worksheetPath, string reference) =>
        ReadCellElement(packageBytes, worksheetPath, reference).Attribute("s")?.Value;

    private static string? ReadRowAttribute(byte[] packageBytes, string worksheetPath, uint row, string attributeName)
    {
        using var stream = new MemoryStream(packageBytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var entry = archive.GetEntry(worksheetPath);
        entry.Should().NotBeNull();
        using var entryStream = entry!.Open();
        var document = XDocument.Load(entryStream);
        var ns = document.Root!.Name.Namespace;
        return document
            .Descendants(ns + "row")
            .SingleOrDefault(element => element.Attribute("r")?.Value == row.ToString(System.Globalization.CultureInfo.InvariantCulture))
            ?.Attribute(attributeName)
            ?.Value;
    }

    private static string? ReadColumnAttribute(byte[] packageBytes, string worksheetPath, uint column, string attributeName)
    {
        using var stream = new MemoryStream(packageBytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var entry = archive.GetEntry(worksheetPath);
        entry.Should().NotBeNull();
        using var entryStream = entry!.Open();
        var document = XDocument.Load(entryStream);
        var ns = document.Root!.Name.Namespace;
        foreach (var element in document.Descendants(ns + "col"))
        {
            if (!uint.TryParse(element.Attribute("min")?.Value, out var min) ||
                !uint.TryParse(element.Attribute("max")?.Value, out var max) ||
                column < min ||
                column > max)
            {
                continue;
            }

            return element.Attribute(attributeName)?.Value;
        }

        return null;
    }

    private static string? ReadMergeCellsAttribute(byte[] packageBytes, string worksheetPath, string attributeName)
    {
        var mergeCells = ReadMergeCellsElement(packageBytes, worksheetPath);
        return mergeCells?.Attribute(attributeName)?.Value;
    }

    private static IReadOnlyList<string> ReadMergeCellReferences(byte[] packageBytes, string worksheetPath)
    {
        var mergeCells = ReadMergeCellsElement(packageBytes, worksheetPath);
        if (mergeCells is null)
            return [];

        var ns = mergeCells.Name.Namespace;
        return mergeCells
            .Elements(ns + "mergeCell")
            .Select(element => element.Attribute("ref")?.Value ?? "")
            .ToList();
    }

    private static string? ReadMergeCellAttribute(
        byte[] packageBytes,
        string worksheetPath,
        string reference,
        string attributeName)
    {
        var mergeCells = ReadMergeCellsElement(packageBytes, worksheetPath);
        if (mergeCells is null)
            return null;

        var ns = mergeCells.Name.Namespace;
        return mergeCells
            .Elements(ns + "mergeCell")
            .SingleOrDefault(element => string.Equals(element.Attribute("ref")?.Value, reference, StringComparison.OrdinalIgnoreCase))
            ?.Attribute(attributeName)
            ?.Value;
    }

    private static XElement? ReadMergeCellsElement(byte[] packageBytes, string worksheetPath)
    {
        using var stream = new MemoryStream(packageBytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var entry = archive.GetEntry(worksheetPath);
        entry.Should().NotBeNull();
        using var entryStream = entry!.Open();
        var document = XDocument.Load(entryStream);
        var ns = document.Root!.Name.Namespace;
        return document.Root.Element(ns + "mergeCells");
    }

    private static string? ReadHyperlinksAttribute(byte[] packageBytes, string worksheetPath, string attributeName)
    {
        var hyperlinks = ReadHyperlinksElement(packageBytes, worksheetPath);
        return hyperlinks?.Attribute(attributeName)?.Value;
    }

    private static string? ReadHyperlinkAttribute(
        byte[] packageBytes,
        string worksheetPath,
        string reference,
        string attributeName)
    {
        var hyperlinks = ReadHyperlinksElement(packageBytes, worksheetPath);
        if (hyperlinks is null)
            return null;

        var ns = hyperlinks.Name.Namespace;
        return hyperlinks
            .Elements(ns + "hyperlink")
            .SingleOrDefault(element => string.Equals(element.Attribute("ref")?.Value, reference, StringComparison.OrdinalIgnoreCase))
            ?.Attribute(attributeName)
            ?.Value;
    }

    private static XElement? ReadHyperlinksElement(byte[] packageBytes, string worksheetPath)
    {
        using var stream = new MemoryStream(packageBytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var entry = archive.GetEntry(worksheetPath);
        entry.Should().NotBeNull();
        using var entryStream = entry!.Open();
        var document = XDocument.Load(entryStream);
        var ns = document.Root!.Name.Namespace;
        return document.Root.Element(ns + "hyperlinks");
    }

    private static string? ReadCommentText(byte[] packageBytes, string commentsPath, string reference)
    {
        var comment = ReadCommentElement(packageBytes, commentsPath, reference);
        var ns = comment.Name.Namespace;
        return string.Concat(comment
            .Element(ns + "text")?
            .Descendants(ns + "t")
            .Select(element => element.Value) ?? []);
    }

    private static string? ReadCommentAttribute(
        byte[] packageBytes,
        string commentsPath,
        string reference,
        string attributeName) =>
        ReadCommentElement(packageBytes, commentsPath, reference)
            .Attribute(attributeName)
            ?.Value;

    private static XElement ReadCommentElement(byte[] packageBytes, string commentsPath, string reference)
    {
        using var stream = new MemoryStream(packageBytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var entry = archive.GetEntry(commentsPath);
        entry.Should().NotBeNull();
        using var entryStream = entry!.Open();
        var document = XDocument.Load(entryStream);
        var ns = document.Root!.Name.Namespace;
        var comment = document
            .Descendants(ns + "comment")
            .SingleOrDefault(element => string.Equals(element.Attribute("ref")?.Value, reference, StringComparison.OrdinalIgnoreCase));
        comment.Should().NotBeNull();
        return comment!;
    }

    private static string? ReadCellTextSpaceMode(byte[] packageBytes, string worksheetPath, string reference)
    {
        var cell = ReadCellElement(packageBytes, worksheetPath, reference);
        var ns = cell.Name.Namespace;
        return cell.Element(ns + "is")?.Element(ns + "t")?.Attribute(XNamespace.Xml + "space")?.Value;
    }

    private static string? ReadWorksheetDimension(byte[] packageBytes, string worksheetPath)
    {
        using var stream = new MemoryStream(packageBytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var entry = archive.GetEntry(worksheetPath);
        entry.Should().NotBeNull();
        using var entryStream = entry!.Open();
        var document = XDocument.Load(entryStream);
        var ns = document.Root!.Name.Namespace;
        return document.Root.Element(ns + "dimension")?.Attribute("ref")?.Value;
    }

    private static XElement ReadCellElement(byte[] packageBytes, string worksheetPath, string reference)
    {
        var cell = TryReadCellElement(packageBytes, worksheetPath, reference);
        cell.Should().NotBeNull();
        return cell!;
    }

    private static XElement? TryReadCellElement(byte[] packageBytes, string worksheetPath, string reference)
    {
        using var stream = new MemoryStream(packageBytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var entry = archive.GetEntry(worksheetPath);
        entry.Should().NotBeNull();
        using var entryStream = entry!.Open();
        var document = XDocument.Load(entryStream);
        var ns = document.Root!.Name.Namespace;
        var cell = document
            .Descendants(ns + "c")
            .SingleOrDefault(element => string.Equals(element.Attribute("r")?.Value, reference, StringComparison.Ordinal));

        return cell;
    }
}
