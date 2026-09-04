using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round-trip regression tests for three XLSX structured-table data-loss bugs fixed in
/// tables/wave-c-roundtrip:
///   1. &lt;calculatedColumnFormula array="1"&gt; dropped on write.
///   2. &lt;totalsRowFormula array="1"&gt; dropped on write.
///   3. Dual-use &lt;tableStyle table="1" pivot="1"&gt; custom style dropped on load.
/// </summary>
public sealed class XlsxStructuredTableRoundTripTests
{
    private static readonly XNamespace MainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

    // -------------------------------------------------------------------------
    // Bug 1 — calculatedColumnFormula/@array round-trip
    // -------------------------------------------------------------------------

    [Fact]
    public void CalculatedColumnFormulaArrayFlag_SurvivesFullRoundTrip()
    {
        // 1. Build a workbook with a table that has a calculated-column formula.
        using var initial = BuildTablePackage(calculatedArray: true, totalsArray: false);

        // 2. Verify the saved package contains array="1" on calculatedColumnFormula.
        AssertFormulaArrayAttribute(initial, "calculatedColumnFormula", expectedValue: "1",
            "initial save should write array=\"1\" on calculatedColumnFormula");

        // 3. Load → save → load again (full round-trip).
        initial.Position = 0;
        var adapter = new XlsxFileAdapter();
        var reloaded = adapter.Load(initial);

        // Confirm the model flag was read back.
        var column = reloaded.GetSheetAt(0).StructuredTables[0].Columns[1];
        column.IsCalculatedColumnFormulaArray.Should().BeTrue(
            "IsCalculatedColumnFormulaArray must be set when array=\"1\" is present in the XML");

        // 4. Save the reloaded workbook and inspect the new package.
        using var resaved = new MemoryStream();
        adapter.Save(reloaded, resaved);

        AssertFormulaArrayAttribute(resaved, "calculatedColumnFormula", expectedValue: "1",
            "re-saved package must still carry array=\"1\" on calculatedColumnFormula");
    }

    [Fact]
    public void CalculatedColumnFormulaWithoutArrayFlag_DoesNotEmitArrayAttribute()
    {
        using var initial = BuildTablePackage(calculatedArray: false, totalsArray: false);

        // When the flag is false the attribute must be absent (not written as "0").
        AssertFormulaArrayAttributeAbsent(initial, "calculatedColumnFormula",
            "array attribute must be absent when IsCalculatedColumnFormulaArray is false");
    }

    // -------------------------------------------------------------------------
    // Bug 2 — totalsRowFormula/@array round-trip
    // -------------------------------------------------------------------------

    [Fact]
    public void TotalsRowFormulaArrayFlag_SurvivesFullRoundTrip()
    {
        using var initial = BuildTablePackage(calculatedArray: false, totalsArray: true);

        AssertFormulaArrayAttribute(initial, "totalsRowFormula", expectedValue: "1",
            "initial save should write array=\"1\" on totalsRowFormula");

        initial.Position = 0;
        var adapter = new XlsxFileAdapter();
        var reloaded = adapter.Load(initial);

        var column = reloaded.GetSheetAt(0).StructuredTables[0].Columns[1];
        column.IsTotalsRowFormulaArray.Should().BeTrue(
            "IsTotalsRowFormulaArray must be set when array=\"1\" is present in the XML");

        using var resaved = new MemoryStream();
        adapter.Save(reloaded, resaved);

        AssertFormulaArrayAttribute(resaved, "totalsRowFormula", expectedValue: "1",
            "re-saved package must still carry array=\"1\" on totalsRowFormula");
    }

    [Fact]
    public void TotalsRowFormulaWithoutArrayFlag_DoesNotEmitArrayAttribute()
    {
        using var initial = BuildTablePackage(calculatedArray: false, totalsArray: false);

        AssertFormulaArrayAttributeAbsent(initial, "totalsRowFormula",
            "array attribute must be absent when IsTotalsRowFormulaArray is false");
    }

    // -------------------------------------------------------------------------
    // Bug 3 — dual-use tableStyle (table="1" pivot="1") round-trip
    // -------------------------------------------------------------------------

    [Fact]
    public void DualUseTableStyle_SurvivesFullRoundTrip()
    {
        // Build a package with a dual-use custom style injected into styles.xml.
        using var initial = BuildDualUseStylePackage(pivot: true, table: true);

        var adapter = new XlsxFileAdapter();

        // Load: the dual-use style must be present in the workbook model.
        initial.Position = 0;
        var workbook = adapter.Load(initial);
        workbook.StructuredTableStyles
            .Should().Contain(s => s.Name == "CustomDualUseStyle",
                "dual-use style (table=1,pivot=1) must be loaded, not skipped");
        var loaded = workbook.StructuredTableStyles.First(s => s.Name == "CustomDualUseStyle");
        loaded.AppliesToTables.Should().BeTrue();
        loaded.AppliesToPivotTables.Should().BeTrue(
            "AppliesToPivotTables must be true for a dual-use style");

        // Save → reload and assert style is still there.
        using var resaved = new MemoryStream();
        adapter.Save(workbook, resaved);

        resaved.Position = 0;
        var reloaded = adapter.Load(resaved);
        reloaded.StructuredTableStyles
            .Should().Contain(s => s.Name == "CustomDualUseStyle",
                "dual-use style must survive a full save+reload cycle");
    }

    [Fact]
    public void PivotOnlyTableStyle_IsStillSkippedByTableStyleReader()
    {
        // A pivot-only style (pivot="1", table="0") must NOT be loaded as a table style.
        using var initial = BuildDualUseStylePackage(pivot: true, table: false);

        var adapter = new XlsxFileAdapter();
        initial.Position = 0;
        var workbook = adapter.Load(initial);

        workbook.StructuredTableStyles
            .Should().NotContain(s => s.Name == "CustomDualUseStyle",
                "pivot-only style (table=0,pivot=1) must not be loaded as a table style");
    }

    [Fact]
    public void TableOnlyStyle_ContinuesToLoad()
    {
        // Existing path: table="1" pivot="0" — must still load (regression guard).
        using var initial = BuildDualUseStylePackage(pivot: false, table: true);

        var adapter = new XlsxFileAdapter();
        initial.Position = 0;
        var workbook = adapter.Load(initial);

        workbook.StructuredTableStyles
            .Should().Contain(s => s.Name == "CustomDualUseStyle",
                "table-only style (table=1,pivot=0) must continue to load");
        var loaded = workbook.StructuredTableStyles.First(s => s.Name == "CustomDualUseStyle");
        loaded.AppliesToPivotTables.Should().BeFalse(
            "AppliesToPivotTables must be false for a table-only style");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Builds a minimal XLSX in memory containing a 3-column table where:
    ///   column 2 has a CalculatedColumnFormula (optionally array-flagged), and
    ///   column 2 also has a TotalsRowFormula (optionally array-flagged).
    /// </summary>
    private static MemoryStream BuildTablePackage(bool calculatedArray, bool totalsArray)
    {
        var workbook = new Workbook("TableArrayTest");
        var sheet = workbook.AddSheet("Data");

        // Populate header + data rows so the table ref is valid.
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Id"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Value"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Result"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(100));

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "TestTable",
            DisplayName = "TestTable",
            Range = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 2, 3)),
            HasAutoFilter = true,
            TotalsRowShown = false,
            PackagePart = "xl/tables/table1.xml"
        };

        table.Columns.Add(new StructuredTableColumnModel(1, "Id"));
        table.Columns.Add(new StructuredTableColumnModel(
            Id: 2,
            Name: "Value",
            CalculatedColumnFormula: "TRANSPOSE(A2:A2)",
            TotalsRowFormula: "SUMPRODUCT((A2:A2))",
            IsCalculatedColumnFormulaArray: calculatedArray,
            IsTotalsRowFormulaArray: totalsArray));
        table.Columns.Add(new StructuredTableColumnModel(3, "Result"));

        sheet.StructuredTables.Add(table);

        var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;
        return stream;
    }

    /// <summary>
    /// Builds a minimal XLSX and then injects a custom tableStyle into styles.xml with the
    /// specified table/pivot attribute combination.
    /// </summary>
    private static MemoryStream BuildDualUseStylePackage(bool pivot, bool table)
    {
        // Create a base workbook.
        var workbook = new Workbook("DualUseStyleTest");
        workbook.AddSheet("Data");

        var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);

        // Patch styles.xml to inject the custom tableStyle.
        stream.Position = 0;
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            var stylesEntry = archive.GetEntry("xl/styles.xml");
            stylesEntry.Should().NotBeNull("styles.xml must exist in the package");

            XDocument stylesXml;
            using (var s = stylesEntry!.Open())
                stylesXml = XDocument.Load(s);

            var tableStyles = stylesXml.Root!.Element(MainNs + "tableStyles");
            if (tableStyles is null)
            {
                tableStyles = new XElement(MainNs + "tableStyles", new XAttribute("count", "0"));
                stylesXml.Root.Add(tableStyles);
            }

            // Inject a minimal dxf so dxfId=0 resolves.
            var dxfs = stylesXml.Root.Element(MainNs + "dxfs");
            if (dxfs is null)
            {
                dxfs = new XElement(MainNs + "dxfs", new XAttribute("count", "1"),
                    new XElement(MainNs + "dxf",
                        new XElement(MainNs + "fill",
                            new XElement(MainNs + "patternFill",
                                new XAttribute("patternType", "solid"),
                                new XElement(MainNs + "fgColor", new XAttribute("rgb", "FFFFCC00"))))));
                stylesXml.Root.Add(dxfs);
            }

            tableStyles.Add(new XElement(
                MainNs + "tableStyle",
                new XAttribute("name", "CustomDualUseStyle"),
                new XAttribute("pivot", pivot ? "1" : "0"),
                new XAttribute("table", table ? "1" : "0"),
                new XAttribute("count", "1"),
                new XElement(
                    MainNs + "tableStyleElement",
                    new XAttribute("type", "wholeTable"),
                    new XAttribute("dxfId", "0"))));

            tableStyles.SetAttributeValue("count",
                tableStyles.Elements(MainNs + "tableStyle").Count().ToString());

            stylesEntry.Delete();
            var newEntry = archive.CreateEntry("xl/styles.xml", CompressionLevel.Optimal);
            using var ws = newEntry.Open();
            stylesXml.Save(ws, SaveOptions.DisableFormatting);
        }

        stream.Position = 0;
        return stream;
    }

    private static void AssertFormulaArrayAttribute(
        MemoryStream stream,
        string elementName,
        string expectedValue,
        string because)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var tableEntry = archive.Entries.FirstOrDefault(e =>
            e.FullName.StartsWith("xl/tables/", StringComparison.OrdinalIgnoreCase) &&
            e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
        tableEntry.Should().NotBeNull("there must be a table XML part in the package");

        XDocument tableXml;
        using (var s = tableEntry!.Open())
            tableXml = XDocument.Load(s);

        var formulaElement = tableXml.Root!
            .Element(MainNs + "tableColumns")?
            .Elements(MainNs + "tableColumn")
            .SelectMany(c => c.Elements(MainNs + elementName))
            .FirstOrDefault();

        formulaElement.Should().NotBeNull($"<{elementName}> must be present in the table XML");
        formulaElement!.Attribute("array")!.Value.Should().Be(expectedValue, because);
    }

    private static void AssertFormulaArrayAttributeAbsent(
        MemoryStream stream,
        string elementName,
        string because)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var tableEntry = archive.Entries.FirstOrDefault(e =>
            e.FullName.StartsWith("xl/tables/", StringComparison.OrdinalIgnoreCase) &&
            e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));

        // If no table part exists at all, there's nothing to check — the formula wasn't written.
        if (tableEntry is null)
            return;

        XDocument tableXml;
        using (var s = tableEntry.Open())
            tableXml = XDocument.Load(s);

        var formulaElement = tableXml.Root!
            .Element(MainNs + "tableColumns")?
            .Elements(MainNs + "tableColumn")
            .SelectMany(c => c.Elements(MainNs + elementName))
            .FirstOrDefault();

        if (formulaElement is null)
            return; // element not emitted — absence of attribute is implicitly satisfied

        formulaElement.Attribute("array").Should().BeNull(because);
    }
}
