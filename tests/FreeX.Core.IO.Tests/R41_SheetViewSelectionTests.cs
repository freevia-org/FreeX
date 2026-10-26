using System.IO;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using NPOI.HSSF.Record;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using NPOI.SS.Util;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression coverage for round 41 findings in the sheetview-selection bucket:
///
/// R41-io-sheetview-selection-activecell-3-1: XlsxWorksheetSheetViewNormalizer.NormalizeSqref
/// stripped a whole-row/whole-column selection sqref ("A:A"/"3:3"/"C:E"/"3:5") on every save
/// because IsCellOrRangeReference required each colon-separated token to parse as a full
/// CellAddress (column letters + row digits), which a bare column-only or row-only token never
/// does. NormalizeAttribute then removed the sqref attribute entirely when normalization failed.
///
/// R41-io-sheetview-selection-activecell-3-3: LegacyXlsFileAdapter.TryGetSelectionRecord (now
/// GetSelectionRecords) used FindFirstRecordBySid, which only returns the first BIFF SELECTION
/// record even though a frozen/split sheet writes one per pane. The fabricated &lt;selection&gt;
/// metadata never carried a pane attribute either, so every downstream consumer treated it as
/// "topLeft" regardless of which pane it actually came from, and any other pane's selection
/// extent/activeCellId was silently discarded.
/// </summary>
public sealed class R41_SheetViewSelectionTests
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static XElement CreateSelectionElement(string activeCell, string sqref) =>
        new(
            WorksheetNs + "selection",
            new XAttribute("activeCell", activeCell),
            new XAttribute("sqref", sqref));

    private static XElement CreateSheetViewElement(XElement selection) =>
        new(
            WorksheetNs + "sheetView",
            new XAttribute("workbookViewId", "0"),
            selection);

    // --- R41-io-sheetview-selection-activecell-3-1 -------------------------------------------

    [Theory]
    [InlineData("C1", "C:C")]
    [InlineData("C1", "C:E")]
    [InlineData("A3", "3:3")]
    [InlineData("A3", "3:5")]
    public void NormalizeSheetViewElement_WholeRowOrColumnSelection_PreservesSqref(string activeCell, string sqref)
    {
        var selection = CreateSelectionElement(activeCell, sqref);
        var sheetView = CreateSheetViewElement(selection);

        var changed = XlsxWorksheetSheetViewNormalizer.NormalizeSheetViewElement(sheetView);

        changed.Should().BeFalse("a whole-row/whole-column selection is already valid and should not be reported as modified");
        selection.Attribute("sqref")!.Value.Should().Be(sqref);
        selection.Attribute("activeCell")!.Value.Should().Be(activeCell);
    }

    [Fact]
    public void NormalizeSheetViewElement_OrdinaryMultiCellSqref_StillPreserved()
    {
        // Sibling no-regression case: a normal multi-cell range sqref (not a whole-row/column
        // selection) must continue to round-trip unchanged through the same code path.
        var selection = CreateSelectionElement("B2", "A1:C3");
        var sheetView = CreateSheetViewElement(selection);

        var changed = XlsxWorksheetSheetViewNormalizer.NormalizeSheetViewElement(sheetView);

        changed.Should().BeFalse();
        selection.Attribute("sqref")!.Value.Should().Be("A1:C3");
    }

    [Fact]
    public void NormalizeSheetViewElement_InvalidSqrefToken_StillStripped()
    {
        // Sibling no-regression case: genuinely malformed sqref tokens (neither a cell/range nor a
        // whole row/column reference) must still be stripped, exactly as before this fix.
        var selection = CreateSelectionElement("A1", "NotAReference");
        var sheetView = CreateSheetViewElement(selection);

        var changed = XlsxWorksheetSheetViewNormalizer.NormalizeSheetViewElement(sheetView);

        changed.Should().BeTrue();
        selection.Attribute("sqref").Should().BeNull();
    }

    [Fact]
    public void NormalizeSheetViewElement_ColumnBeyondMaxCol_StillStripped()
    {
        // A column-letter run that exceeds the max supported column (XFD = 16384) is not a valid
        // whole-column reference and must still be rejected.
        var selection = CreateSelectionElement("A1", "XFE:XFE");
        var sheetView = CreateSheetViewElement(selection);

        var changed = XlsxWorksheetSheetViewNormalizer.NormalizeSheetViewElement(sheetView);

        changed.Should().BeTrue();
        selection.Attribute("sqref").Should().BeNull();
    }

    // --- R41-io-sheetview-selection-activecell-3-3 -------------------------------------------

    private static string GetPrimaryViewMetadata(Sheet sheet)
    {
        var metadata = sheet.PrimaryViewMetadata?.Get("sheetView");
        metadata.Should().NotBeNullOrWhiteSpace("the legacy-.xls import should have produced sheetView passthrough metadata");
        return metadata!;
    }

    private static XElement[] GetSelectionElements(Sheet sheet)
    {
        var (_, children) = XmlNativeBagSerializer.Deserialize(GetPrimaryViewMetadata(sheet));
        return children
            .Where(child => !string.IsNullOrWhiteSpace(child))
            .Select(XElement.Parse)
            .Where(element => string.Equals(element.Name.LocalName, "selection", StringComparison.Ordinal))
            .ToArray();
    }

    [Fact]
    public void Load_FrozenPaneWithMultipleSelectionRecords_PreservesEachPanesSelectionExtent()
    {
        var hssf = new HSSFWorkbook();
        var sheet = hssf.CreateSheet("Data");

        // Freeze at column 1 / row 1 (both non-zero splits -> ActivePane 0 == bottomRight),
        // which creates the sheet's single built-in SelectionRecord with Pane == 0.
        sheet.CreateFreezePane(1, 1);

        var hssfSheet = (HSSFSheet)sheet;
        var bottomRightSelection = hssfSheet.Sheet.FindFirstRecordBySid(SelectionRecord.sid) as SelectionRecord ??
            throw new InvalidOperationException("Expected CreateFreezePane to create a SelectionRecord.");
        bottomRightSelection.Pane.Should().Be((byte)0);

        // The truly-active bottom pane's cursor is B10 with an extended B10:B12 selection.
        bottomRightSelection.ActiveCellRow = 9;
        bottomRightSelection.ActiveCellCol = 1;
        bottomRightSelection.CellReferences = [new CellRangeAddress8Bit(9, 11, 1, 1)];

        // A second SELECTION record for the frozen top-left pane, with its own distinct extended
        // selection (A1:C1), inserted ahead of the bottomRight one so it round-trips first.
        var topLeftSelection = new SelectionRecord(0, 0)
        {
            CellReferences = [new CellRangeAddress8Bit(0, 0, 0, 2)]
        };
        var records = hssfSheet.Sheet.Records;
        var insertionIndex = records.IndexOf(bottomRightSelection);
        records.Insert(insertionIndex, topLeftSelection);

        using var stream = new MemoryStream();
        hssf.Write(stream, leaveOpen: true);
        stream.Position = 0;

        var workbook = new LegacyXlsFileAdapter().Load(stream);
        var loadedSheet = workbook.GetSheetAt(0);

        // The resolved active cell (NPOI's own ActiveCell, from whichever SelectionRecord is
        // parsed last) must still be the truly-active bottom pane's B10.
        loadedSheet.ActiveRow.Should().Be(10);
        loadedSheet.ActiveCol.Should().Be(2);

        var selections = GetSelectionElements(loadedSheet);
        selections.Should().HaveCount(2, "both panes' SELECTION records must be preserved, not just the first one found");

        var topLeft = selections.Single(element => element.Attribute("pane")?.Value == "topLeft");
        topLeft.Attribute("activeCell")!.Value.Should().Be("A1");
        topLeft.Attribute("sqref")!.Value.Should().Be("A1:C1");

        var bottomRight = selections.Single(element => element.Attribute("pane")?.Value == "bottomRight");
        bottomRight.Attribute("activeCell")!.Value.Should().Be("B10");
        bottomRight.Attribute("sqref")!.Value.Should().Be("B10:B12");
    }

    [Fact]
    public void Load_SingleSelectionRecordNoPanes_StillOmitsPaneAttribute()
    {
        // Sibling no-regression case: an ordinary sheet with exactly one SELECTION record (no
        // freeze/split) must keep behaving exactly as before -- no spurious "pane" attribute, and
        // the extended selection is still preserved.
        var hssf = new HSSFWorkbook();
        var sheet = hssf.CreateSheet("Data");
        var hssfSheet = (HSSFSheet)sheet;
        var selection = hssfSheet.Sheet.FindFirstRecordBySid(SelectionRecord.sid) as SelectionRecord ??
            throw new InvalidOperationException("Expected a default SelectionRecord on a fresh sheet.");

        selection.ActiveCellRow = 4;
        selection.ActiveCellCol = 2;
        selection.CellReferences = [new CellRangeAddress8Bit(4, 6, 2, 2)];

        using var stream = new MemoryStream();
        hssf.Write(stream, leaveOpen: true);
        stream.Position = 0;

        var workbook = new LegacyXlsFileAdapter().Load(stream);
        var loadedSheet = workbook.GetSheetAt(0);

        var selections = GetSelectionElements(loadedSheet);
        selections.Should().ContainSingle();
        selections[0].Attribute("pane").Should().BeNull();
        selections[0].Attribute("activeCell")!.Value.Should().Be("C5");
        selections[0].Attribute("sqref")!.Value.Should().Be("C5:C7");
    }
}
