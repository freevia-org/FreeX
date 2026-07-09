using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression coverage for round 18 findings:
///
/// R18-autofilter-sort-state-io-1: a filterColumn with no filter criterion but a button attribute
/// (e.g. &lt;filterColumn colId="0" showButton="0"/&gt;) must NOT be materialized as a value-list
/// state with an empty allowed-set, because <see cref="XlsxWorksheetAutoFilterMaterializer"/>'s
/// RowMatchesAllFilters treats an empty allowed-set as "matches nothing", hiding every data row.
///
/// R18-autofilter-sort-state-io-2: a legal ST_Xstring whitespace-only &lt;filter val=" "/&gt;
/// criterion must survive <see cref="XlsxWorksheetAutoFilterXmlMapper.Read"/> instead of being
/// dropped by an over-eager IsNullOrWhiteSpace filter.
/// </summary>
public sealed class R18_autofilter_Tests
{
    [Fact]
    public void MaterializeFilters_ButtonOnlyFilterColumn_HidesNoRows()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("X"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Y"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("Z"));

        sheet.AutoFilter = new WorksheetAutoFilterModel("A1:A4", null);
        // A filterColumn with zero Values and IncludeBlank=false mirrors what ReadFilterColumns
        // produces for a button-only <filterColumn colId="0" showButton="0"/> (no <filters> child at
        // all, only a native showButton attribute that keeps it from being dropped entirely).
        sheet.AutoFilter.FilterColumns.Add(new WorksheetAutoFilterColumnModel(0, [], IncludeBlank: false));

        XlsxWorksheetAutoFilterMaterializer.MaterializeFilters(sheet);

        sheet.FilterHiddenRows.Should().BeEmpty();
        sheet.ValueFilterHiddenRows.Should().BeEmpty();
    }

    [Fact]
    public void MaterializeFilters_ButtonOnlyColumn_AlongsideRealFilterOnOtherColumn_StillAppliesRealFilter()
    {
        // A button-only column must not cause the whole sheet's filtering to be abandoned: a real
        // value-list filter on another column in the same AutoFilter should still be applied.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("X"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("Keep"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Y"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new TextValue("Drop"));

        sheet.AutoFilter = new WorksheetAutoFilterModel("A1:B3", null);
        sheet.AutoFilter.FilterColumns.Add(new WorksheetAutoFilterColumnModel(0, [], IncludeBlank: false));
        sheet.AutoFilter.FilterColumns.Add(new WorksheetAutoFilterColumnModel(1, ["Keep"], IncludeBlank: false));

        XlsxWorksheetAutoFilterMaterializer.MaterializeFilters(sheet);

        sheet.FilterHiddenRows.Should().BeEquivalentTo([3u]);
    }

    [Fact]
    public void Read_WhitespaceOnlyFilterValue_IsPreservedNotDropped()
    {
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var autoFilterXml =
            "<autoFilter xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" ref=\"A1:A3\">" +
            "<filterColumn colId=\"0\">" +
            "<filters><filter val=\" \"/><filter val=\"X\"/></filters>" +
            "</filterColumn>" +
            "</autoFilter>";
        var element = XElement.Parse(autoFilterXml);

        var model = XlsxWorksheetAutoFilterXmlMapper.Read(element);

        model.Should().NotBeNull();
        model!.FilterColumns.Should().HaveCount(1);
        model.FilterColumns[0].Values.Should().BeEquivalentTo([" ", "X"]);
    }
}
