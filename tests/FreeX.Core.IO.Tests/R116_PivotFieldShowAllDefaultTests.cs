using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R116-io-pivot-showall: CT_PivotField's showAll attribute defaults to TRUE when omitted
/// (ECMA-376 18.3.1.66). The reader correctly leaves <see cref="PivotFieldModel.ShowAll"/> null when the
/// source file omits the attribute (relying on the true default), but the writer used to collapse that
/// null to an explicit showAll="0" via an unconditional `metadataField?.ShowAll == true ? "1" : "0"`
/// ternary -- unlike every other optional boolean attribute on the same element
/// (includeNewItemsInFilter, multipleItemSelectionAllowed, dragToRow/Col/Page/Data, showDropDowns), which
/// all correctly omit the attribute via ToOptionalBoolAttribute when the model value is null. This
/// silently flipped any field whose source file legitimately omitted showAll to explicit false on the
/// very next FreeX save.
/// </summary>
public sealed class R116_PivotFieldShowAllDefaultTests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static (Workbook Workbook, PivotTableModel Pivot) BuildWorkbookWithRowFieldPivot(PivotFieldModel rowField)
    {
        var workbook = new Workbook("PivotShowAllDefaultTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));

        workbook.PivotCaches.Add(new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = "Data",
            SourceReference = "A1:B2",
        });
        workbook.PivotCaches[0].Fields.Add(new PivotCacheFieldModel("Region"));
        workbook.PivotCaches[0].Fields.Add(new PivotCacheFieldModel("Amount"));

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 2)),
            TargetRange = new GridRange(new CellAddress(sheet.Id, 5, 1), new CellAddress(sheet.Id, 8, 2)),
        };
        pivot.RowFields.Add(rowField);
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum", 1));
        sheet.PivotTables.Add(pivot);

        return (workbook, pivot);
    }

    private static XElement SaveAndReadRowPivotFieldXml(Workbook workbook)
    {
        var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry("xl/pivotTables/pivotTable1.xml")!;
        using var entryStream = entry.Open();
        var pivotXml = XDocument.Load(entryStream);

        return pivotXml.Root!
            .Element(WorkbookNs + "pivotFields")!
            .Elements(WorkbookNs + "pivotField")
            .First(f => f.Attribute("axis")?.Value == "axisRow");
    }

    [Fact]
    public void Save_RowFieldWithNullShowAll_OmitsShowAllAttribute()
    {
        // This is the failing-before-fix case: ShowAll left at its natural default (null), meaning the
        // source file never recorded an explicit setting -- Excel's true default must be preserved by
        // omitting the attribute, not by writing showAll="0".
        var (workbook, _) = BuildWorkbookWithRowFieldPivot(new PivotFieldModel(0));

        var rowFieldXml = SaveAndReadRowPivotFieldXml(workbook);

        rowFieldXml.Attribute("showAll").Should().BeNull(
            "an unset ShowAll (unknown/default-true) must stay omitted so real Excel keeps applying the " +
            "true default on reopen, instead of being forced to explicit false");
    }

    [Fact]
    public void Save_RowFieldWithNullShowAll_RoundTripsAsNullNotFalse()
    {
        var (workbook, _) = BuildWorkbookWithRowFieldPivot(new PivotFieldModel(0));

        var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        var reloaded = new XlsxFileAdapter().Load(saved);
        var reloadedPivot = reloaded.GetSheetAt(0).PivotTables.Single();
        reloadedPivot.RowFields.Single().ShowAll.Should().BeNull(
            "re-loading a save of an unset ShowAll must not have silently baked in explicit false");
    }

    [Fact]
    public void Save_RowFieldWithShowAllExplicitlyFalse_StillEmitsExplicitZero()
    {
        // No-regression sibling: when the user (or source file) DID explicitly turn off "Show all items"
        // for the field, that explicit false must still be written -- only the null/unknown case changes.
        var (workbook, _) = BuildWorkbookWithRowFieldPivot(new PivotFieldModel(0, ShowAll: false));

        var rowFieldXml = SaveAndReadRowPivotFieldXml(workbook);

        rowFieldXml.Attribute("showAll")?.Value.Should().Be("0",
            "an explicit ShowAll=false in the model must still be written as showAll=\"0\", not omitted");
    }

    [Fact]
    public void Save_RowFieldWithShowAllExplicitlyTrue_StillEmitsExplicitOne()
    {
        // No-regression sibling: an explicit true must still round-trip as an explicit "1", matching the
        // pattern of every other optional boolean attribute on this element.
        var (workbook, _) = BuildWorkbookWithRowFieldPivot(new PivotFieldModel(0, ShowAll: true));

        var rowFieldXml = SaveAndReadRowPivotFieldXml(workbook);

        rowFieldXml.Attribute("showAll")?.Value.Should().Be("1");
    }
}
