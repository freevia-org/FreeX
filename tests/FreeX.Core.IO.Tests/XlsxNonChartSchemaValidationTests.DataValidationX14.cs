using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Tests for x14 (cross-sheet) List data validation: read, write, and round-trip.
///
/// B-2 / RT-1: Excel 2010+ stores List validations whose source formula references another sheet
/// in an &lt;x14:dataValidation&gt; block inside the worksheet extLst under the ext URI
/// {CCE6A557-97BC-4b89-ADB6-D9C93CAAB3DF}. The formula is in &lt;xm:f&gt; and the target cells
/// in &lt;xm:sqref&gt;. The legacy &lt;dataValidation&gt; for the same cell has an empty formula1.
///
/// Enforcement and list-item resolution are tested in FreeX.Core.Calc.Tests
/// (DataValidationTests.X14CrossSheetListRules.cs).
/// </summary>
public sealed partial class XlsxNonChartSchemaValidationTests
{
    // ── Helpers specific to x14 DV tests ─────────────────────────────────────

    private static readonly XNamespace X14Ns = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";
    private static readonly XNamespace XmNs = "http://schemas.microsoft.com/office/excel/2006/main";
    private const string X14DvUri = "{CCE6A557-97BC-4b89-ADB6-D9C93CAAB3DF}";

    /// <summary>
    /// Craft an XLSX byte stream containing a cross-sheet x14 List DV.
    /// The workbook has two sheets:
    ///   Sheet1 — B2 has a list validation that reads from Sheet2!$A$1:$A$5.
    ///   Sheet2 — A1:A5 contains the 5 valid items (Apple … Elderberry).
    /// The x14 extLst block is injected directly into the worksheet XML, matching what
    /// Excel 2010+ produces for a cross-sheet List validation.
    /// </summary>
    private static MemoryStream CreateX14CrossSheetDvXlsx()
    {
        // Build a base workbook with two sheets.
        var workbook = new Workbook("X14DvRoundTrip");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");

        // Populate the source items on Sheet2.
        string[] items = ["Apple", "Banana", "Cherry", "Durian", "Elderberry"];
        for (var i = 0; i < items.Length; i++)
            sheet2.SetCell(new CellAddress(sheet2.Id, (uint)(i + 1), 1), new TextValue(items[i]));

        // Add a legacy (empty-formula1) List DV on Sheet1 B2 so ClosedXML writes the element.
        sheet1.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(
                new CellAddress(sheet1.Id, 2, 2),
                new CellAddress(sheet1.Id, 2, 2)),
            Type = DvType.List,
            // Empty formula1 — the real formula lives in the x14 block we inject below.
            Formula1 = null,
            AllowBlank = true,
            ShowDropdown = true,
        });

        using var baseStream = XlsxPackageTestHelper.SaveWorkbook(workbook);

        // Inject the x14 DV extLst block into sheet1's worksheet XML.
        baseStream.Position = 0;
        using (var archive = new ZipArchive(baseStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            var worksheetXml = LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
            InjectX14DvExtLst(worksheetXml, "Sheet2!$A$1:$A$5", "B2");
            ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
        }

        // Return an expandable MemoryStream (not a fixed-buffer one) so tests can update it.
        var result = new MemoryStream();
        result.Write(baseStream.ToArray());
        result.Position = 0;
        return result;
    }

    /// <summary>Injects an x14 dataValidations ext block into the worksheet XML.</summary>
    private static void InjectX14DvExtLst(XDocument worksheetXml, string formula1, string sqref)
    {
        var root = worksheetXml.Root!;
        XNamespace wsNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        var x14DvBlock = new XElement(
            wsNs + "extLst",
            new XElement(
                wsNs + "ext",
                new XAttribute(XNamespace.Xmlns + "x14", X14Ns.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "xm", XmNs.NamespaceName),
                new XAttribute("uri", X14DvUri),
                new XElement(
                    X14Ns + "dataValidations",
                    new XAttribute("count", "1"),
                    new XElement(
                        X14Ns + "dataValidation",
                        new XAttribute("type", "list"),
                        new XAttribute("allowBlank", "1"),
                        new XElement(X14Ns + "formula1",
                            new XElement(XmNs + "f", formula1)),
                        new XElement(XmNs + "sqref", sqref)))));

        // Append after <tableParts> if present, otherwise at end of root.
        var tableParts = root.Elements().LastOrDefault(e => e.Name.LocalName == "tableParts");
        if (tableParts is not null)
            tableParts.AddAfterSelf(x14DvBlock);
        else
            root.Add(x14DvBlock);
    }

    // ── Test: READ ──────────────────────────────────────────────────────────────

    [Fact]
    public void X14DataValidation_Load_ParsesFormula1FromXmF()
    {
        // The x14 DV block carries the cross-sheet formula; the legacy element has empty formula1.
        // After loading, the DataValidation.Formula1 must equal the cross-sheet ref.
        using var xlsx = CreateX14CrossSheetDvXlsx();
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(xlsx);
        var sheet1 = workbook.GetSheet("Sheet1")!;

        sheet1.DataValidations.Should().ContainSingle("exactly one list DV on Sheet1");

        var dv = sheet1.DataValidations[0];
        dv.Type.Should().Be(DvType.List);
        // The x14 <xm:f> text (no leading '=' on disk) must override the empty legacy formula1,
        // AND gain FreeX's in-memory '=' marker (mirroring XlsxDataValidationClosedXmlMapper.Load's
        // legacy-path convention, R74-io-data-validation-xml-4-1) so
        // DataValidationService.ListSources resolves the cross-sheet range instead of treating the
        // raw reference text as one literal dropdown item.
        dv.Formula1.Should().Be("=Sheet2!$A$1:$A$5",
            "x14 formula1 from <xm:f> must override the empty legacy formula1 and carry the same " +
            "in-memory '=' marker the legacy List loader adds for a range/name source");
        dv.IsX14.Should().BeTrue("rule read from x14 block must be flagged IsX14");
        dv.AllowBlank.Should().BeTrue();
    }

    [Fact]
    public void X14DataValidation_Load_SetsIsX14Flag()
    {
        using var xlsx = CreateX14CrossSheetDvXlsx();
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(xlsx);
        var dv = workbook.GetSheet("Sheet1")!.DataValidations[0];

        dv.IsX14.Should().BeTrue();
    }

    // ── Test: WRITE / ROUND-TRIP ────────────────────────────────────────────────

    [Fact]
    public void X14DataValidation_Save_EmitsX14ExtLstBlock()
    {
        // A DataValidation with IsX14=true must produce an x14 extLst block in the saved XML.
        var workbook = new Workbook("X14DvWrite");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");
        for (var i = 1; i <= 5; i++)
            sheet2.SetCell(new CellAddress(sheet2.Id, (uint)i, 1), new TextValue($"Item{i}"));

        sheet1.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(
                new CellAddress(sheet1.Id, 2, 2),
                new CellAddress(sheet1.Id, 2, 2)),
            Type = DvType.List,
            // The leading '=' is the in-memory marker Load() always adds for a genuine range/
            // cross-sheet reference (see XlsxDataValidationClosedXmlMapper.Load and
            // XlsxX14DataValidationReader.NormalizeX14ListFormula1) -- NormalizeListFormulaForSave
            // treats this as the ONLY literal-vs-reference authority (R95/R96), so a hand-built
            // model must carry it too in order to reflect a real reference instead of a literal
            // whose text happens to look like one.
            Formula1 = "=Sheet2!$A$1:$A$5",
            AllowBlank = true,
            ShowDropdown = true,
            IsX14 = true,
        });

        using var saved = XlsxPackageTestHelper.SaveWorkbook(workbook);

        var x14ExtBlock = ReadX14DvExtBlock(saved, "xl/worksheets/sheet1.xml");
        x14ExtBlock.Should().NotBeNull("x14 DV block must be written to worksheet extLst");

        var x14Dv = x14ExtBlock!
            .Element(X14Ns + "dataValidations")?
            .Element(X14Ns + "dataValidation");
        x14Dv.Should().NotBeNull("x14:dataValidation element must exist");
        x14Dv!.Attribute("type")!.Value.Should().Be("list");

        var formula = x14Dv
            .Element(X14Ns + "formula1")?
            .Element(XmNs + "f")?
            .Value;
        formula.Should().Be("Sheet2!$A$1:$A$5",
            "cross-sheet formula must be serialised into <xm:f> child of <x14:formula1>");

        var sqref = x14Dv.Element(XmNs + "sqref")?.Value;
        sqref.Should().Be("B2", "target cell must be written as <xm:sqref> child element");
    }

    [Fact]
    public void X14DataValidation_Save_KeepsEmptyLegacyElement()
    {
        // The legacy <dataValidation> for the same cell must be kept (empty formula1) so
        // older readers don't trip over a missing element.
        XNamespace wsNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var workbook = new Workbook("X14DvLegacy");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");
        for (var i = 1; i <= 3; i++)
            sheet2.SetCell(new CellAddress(sheet2.Id, (uint)i, 1), new TextValue($"Val{i}"));

        sheet1.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(
                new CellAddress(sheet1.Id, 3, 1),
                new CellAddress(sheet1.Id, 3, 1)),
            Type = DvType.List,
            // See the marker note in X14DataValidation_Save_EmitsX14ExtLstBlock above.
            Formula1 = "=Sheet2!$A$1:$A$3",
            AllowBlank = true,
            IsX14 = true,
        });

        using var saved = XlsxPackageTestHelper.SaveWorkbook(workbook);

        // The legacy <dataValidations> element should still be present.
        saved.Position = 0;
        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var worksheetXml = LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
        var legacyDvs = worksheetXml.Root!.Element(wsNs + "dataValidations");
        legacyDvs.Should().NotBeNull("legacy <dataValidations> must be present for old readers");

        var legacyDv = legacyDvs!.Element(wsNs + "dataValidation");
        legacyDv.Should().NotBeNull();
        // formula1 must be absent or empty (x14 block carries the real formula).
        var legacyFormula1 = legacyDv!.Element(wsNs + "formula1");
        if (legacyFormula1 is not null)
            legacyFormula1.Value.Should().BeEmpty(
                "legacy formula1 must be empty for x14 rules; real formula lives in x14 block");
    }

    [Fact]
    public void X14DataValidation_RoundTrip_FormulaAndSqrefSurvive()
    {
        // Load an x14 DV XLSX → make an unrelated edit → save → reload → verify.
        using var source = CreateX14CrossSheetDvXlsx();
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should().BeTrue(blockReason);

        // Unrelated edit to trigger a save cycle.
        var sheet1 = workbook.GetSheet("Sheet1")!;
        sheet1.SetCell(new CellAddress(sheet1.Id, 10, 1), new NumberValue(42));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        // Verify x14 block is in the saved XML.
        var x14Block = ReadX14DvExtBlock(saved, "xl/worksheets/sheet1.xml");
        x14Block.Should().NotBeNull("x14 DV ext block must survive the save");

        var formula = x14Block!
            .Element(X14Ns + "dataValidations")?
            .Element(X14Ns + "dataValidation")?
            .Element(X14Ns + "formula1")?
            .Element(XmNs + "f")?
            .Value;
        formula.Should().Be("Sheet2!$A$1:$A$5",
            "cross-sheet formula must survive load → save round-trip");

        // Reload and verify model is intact.
        saved.Position = 0;
        var reloaded = adapter.Load(saved);
        var reloadedSheet1 = reloaded.GetSheet("Sheet1")!;
        var reloadedDv = reloadedSheet1.DataValidations.Should().ContainSingle().Subject;
        reloadedDv.Type.Should().Be(DvType.List);
        // Reloading re-adds the in-memory '=' marker (see the Load assertion above); the on-disk
        // <xm:f> text checked just above stays marker-free.
        reloadedDv.Formula1.Should().Be("=Sheet2!$A$1:$A$5");
        reloadedDv.IsX14.Should().BeTrue();
    }

    [Fact]
    public void X14DataValidation_RoundTrip_PreservesOtherExtLstChildren()
    {
        // Any pre-existing non-DV extLst ext children must survive the save.
        XNamespace wsNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        using var source = CreateX14CrossSheetDvXlsx();

        // Inject an extra (unrelated) extLst ext block.
        source.Position = 0;
        using (var archive = new ZipArchive(source, ZipArchiveMode.Update, leaveOpen: true))
        {
            var worksheetXml = LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
            var root = worksheetXml.Root!;
            // Add a spurious extension with a different URI to the existing extLst.
            var existingExtLst = root.Elements()
                .LastOrDefault(e => e.Name.LocalName == "extLst");
            existingExtLst?.Add(new XElement(
                wsNs + "ext",
                new XAttribute("uri", "{FREEX-UNRELATED-EXT}"),
                new XElement(X14Ns + "someFeature")));
            ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
        }

        source.Position = 0;
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out _);

        workbook.GetSheet("Sheet1")!
            .SetCell(new CellAddress(workbook.GetSheet("Sheet1")!.Id, 10, 1), new NumberValue(1));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        // Both the DV ext and the unrelated ext must be present.
        saved.Position = 0;
        using var savedArchive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var savedWsXml = LoadPackageXml(savedArchive, "xl/worksheets/sheet1.xml");
        var allExtUris = savedWsXml.Root!
            .Elements()
            .Where(e => e.Name.LocalName == "extLst")
            .SelectMany(el => el.Elements())
            .Where(e => e.Name.LocalName == "ext")
            .Select(e => e.Attribute("uri")?.Value)
            .ToList();

        allExtUris.Should().Contain(X14DvUri, "x14 DV ext must be present after save");
        allExtUris.Should().Contain("{FREEX-UNRELATED-EXT}",
            "unrelated ext children must be preserved through round-trip");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads the x14 DV ext element from the given worksheet entry in the ZIP, or null if absent.
    /// </summary>
    private static XElement? ReadX14DvExtBlock(Stream xlsxStream, string worksheetPath)
    {
        xlsxStream.Position = 0;
        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Read, leaveOpen: true);
        var worksheetXml = LoadPackageXml(archive, worksheetPath);

        return worksheetXml.Root!
            .Elements()
            .Where(e => e.Name.LocalName == "extLst")
            .SelectMany(el => el.Elements())
            .Where(e => e.Name.LocalName == "ext")
            .FirstOrDefault(e => e.Attribute("uri")?.Value == X14DvUri);
    }
}
