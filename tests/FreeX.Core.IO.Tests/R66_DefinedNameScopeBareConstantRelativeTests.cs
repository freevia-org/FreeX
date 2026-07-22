using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Formula;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression coverage for round-66 io-defined-names-scope findings 6-1/6-2/6-3.
/// </summary>
public sealed class R66_DefinedNameScopeBareConstantRelativeTests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    // ── 6-1: bare Excel-reserved-LOOKING names must load/save as ordinary user names ──────────

    [Fact]
    public void Load_BareDatabaseName_LoadsAsOrdinaryRangeInsteadOfBeingDropped()
    {
        // Pre-fix: "Database" was in ExcelReservedDefinedNames, so IsExcelReservedDefinedName
        // returned true for the bare name and LoadDefinedNames silently dropped it.
        using var source = BuildSourcePackageWithDefinedName("Database", "Sheet1!$A$1:$A$5");

        var loaded = new XlsxFileAdapter().Load(source);
        var sheet1 = loaded.GetSheetAt(0);

        loaded.NamedRanges.Should().ContainKey("Database");
        loaded.NamedRanges["Database"].Should().Be(new GridRange(
            new CellAddress(sheet1.Id, 1, 1),
            new CellAddress(sheet1.Id, 5, 1)));
    }

    [Theory]
    [InlineData("Criteria")]
    [InlineData("Database")]
    [InlineData("Extract")]
    [InlineData("Consolidate_Area")]
    public void LoadThenSave_BareReservedLookingNames_RoundTrip(string name)
    {
        using var source = BuildSourcePackageWithDefinedName(name, "Sheet1!$A$1:$A$5");

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);
        loaded.NamedRanges.Should().ContainKey(name);

        var sheet = loaded.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 9, 9), new NumberValue(1));

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        var reloaded = new XlsxFileAdapter().Load(saved);
        reloaded.NamedRanges.Should().ContainKey(name);
    }

    [Fact]
    public void Load_BareDatabaseName_FormulaResolvesAgainstIt()
    {
        using var source = BuildSourcePackageWithDefinedName("Database", "Sheet1!$A$1:$A$1");

        var loaded = new XlsxFileAdapter().Load(source);
        var sheet = loaded.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(10));
        sheet.SetFormula(new CellAddress(sheet.Id, 2, 1), "Database*2");

        var engine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());
        engine.RecalculateAllFormulas(loaded);

        sheet.GetValue(2, 1).Should().Be(new NumberValue(20));
    }

    /// <summary>
    /// Sibling no-regression: the genuine _xlnm.-prefixed Print_Area built-in (dedicated
    /// ClosedXML PageSetup handling) must still be excluded from the model round-trip, not
    /// doubly-emitted as an ordinary named range.
    /// </summary>
    [Fact]
    public void Save_PrintAreaBuiltIn_StillExcludedFromOrdinaryNamedRangeHandling()
    {
        var workbook = new Workbook("PrintAreaStillReserved");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.PrintArea = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 2, 2));

        using var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        List<XElement> definedNameElements;
        using (var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true))
        {
            var entry = archive.GetEntry("xl/workbook.xml")!;
            var xml = XlsxPackageXmlEditor.LoadXml(entry);
            definedNameElements = xml.Root!
                .Element(WorkbookNs + "definedNames")?
                .Elements(WorkbookNs + "definedName")
                .Where(e => string.Equals(e.Attribute("name")?.Value, "_xlnm.Print_Area", StringComparison.OrdinalIgnoreCase))
                .ToList() ?? new List<XElement>();
        }

        // Exactly one <definedName name="_xlnm.Print_Area"> — never doubly-emitted via the
        // ordinary NamedRanges/NamedFormulas save path (which must keep skipping it).
        definedNameElements.Should().HaveCount(1);

        saved.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(saved);
        reloaded.NamedRanges.Should().NotContainKey("Print_Area");
        reloaded.NamedRanges.Should().NotContainKey("_xlnm.Print_Area");
    }

    // ── 6-2: constant-literal RefersTo must load/resolve/round-trip instead of being dropped ──

    [Fact]
    public void Load_NumericConstantDefinedName_LoadsIntoNamedFormulas()
    {
        using var source = BuildSourcePackageWithDefinedName("TaxRate", "0.21");

        var loaded = new XlsxFileAdapter().Load(source);

        // Pre-fix: dropped entirely (present in neither collection) because "0.21" fails
        // ValidateNamedRangeName (digit-leading) and ClosedXML's Ranges enumerates to zero items.
        loaded.NamedFormulas.Should().ContainKey("TaxRate");
        loaded.NamedFormulas["TaxRate"].Should().Be("0.21");
        loaded.NamedRanges.Should().NotContainKey("TaxRate");
    }

    [Fact]
    public void Load_NumericConstantDefinedName_FormulaResolvesUsingConstant()
    {
        using var source = BuildSourcePackageWithDefinedName("TaxRate", "0.21");

        var loaded = new XlsxFileAdapter().Load(source);
        var sheet = loaded.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(100));
        sheet.SetFormula(new CellAddress(sheet.Id, 3, 2), "B2*TaxRate");

        var engine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());
        engine.RecalculateAllFormulas(loaded);

        sheet.GetValue(3, 2).Should().Be(new NumberValue(21));
    }

    [Fact]
    public void Load_TextConstantDefinedName_ResolvesAsText()
    {
        using var source = BuildSourcePackageWithDefinedName("Greeting", "\"Hello\"");

        var loaded = new XlsxFileAdapter().Load(source);
        var sheet = loaded.GetSheetAt(0);
        sheet.SetFormula(new CellAddress(sheet.Id, 1, 1), "Greeting");

        var engine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());
        engine.RecalculateAllFormulas(loaded);

        sheet.GetValue(1, 1).Should().Be(new TextValue("Hello"));
    }

    [Fact]
    public void LoadThenSave_NumericConstantDefinedName_RoundTripsUnchanged()
    {
        using var source = BuildSourcePackageWithDefinedName("TaxRate", "0.21");

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);
        loaded.NamedFormulas.Should().ContainKey("TaxRate");

        var sheet = loaded.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 5, 5), new NumberValue(42));

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        var reloaded = new XlsxFileAdapter().Load(saved);
        reloaded.NamedFormulas.Should().ContainKey("TaxRate");
        reloaded.NamedFormulas["TaxRate"].Should().Be("0.21");
        reloaded.NamedRanges.Should().NotContainKey("TaxRate");
    }

    /// <summary>Sibling no-regression: an ordinary plain range name must still load as a GridRange.</summary>
    [Fact]
    public void Load_PlainRangeDefinedName_StillResolvesToGridRange_NoRegression()
    {
        using var source = BuildSourcePackageWithDefinedName("PlainRange", "Sheet1!$B$2:$C$4");

        var loaded = new XlsxFileAdapter().Load(source);
        var sheet1 = loaded.GetSheetAt(0);

        loaded.NamedRanges.Should().ContainKey("PlainRange");
        loaded.NamedFormulas.Should().NotContainKey("PlainRange");
        loaded.NamedRanges["PlainRange"].Should().Be(new GridRange(
            new CellAddress(sheet1.Id, 2, 2),
            new CellAddress(sheet1.Id, 4, 3)));
    }

    // ── 6-3: a relative-reference defined name must round-trip instead of freezing absolute ──

    [Fact]
    public void Load_RelativeReferenceDefinedName_PreservesRawTextInsteadOfFreezingAbsolute()
    {
        // Pre-fix: this resolved straight into a GridRange via ClosedXML and, on the very next
        // save, ToAbsoluteA1 would have re-emitted it as "Sheet1!$A$1" — permanently freezing away
        // the relative (no-$) semantics.
        using var source = BuildSourcePackageWithDefinedName("RelName", "Sheet1!A1");

        var loaded = new XlsxFileAdapter().Load(source);

        loaded.NamedFormulas.Should().ContainKey("RelName");
        loaded.NamedFormulas["RelName"].Should().Be("Sheet1!A1");
        loaded.NamedRanges.Should().NotContainKey("RelName");
    }

    [Fact]
    public void LoadThenSave_RelativeReferenceDefinedName_RoundTripsUnchanged()
    {
        using var source = BuildSourcePackageWithDefinedName("RelName", "Sheet1!A1");

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);
        loaded.NamedFormulas.Should().ContainKey("RelName");

        var sheet = loaded.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 9, 9), new NumberValue(1));

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        var reloaded = new XlsxFileAdapter().Load(saved);
        reloaded.NamedFormulas.Should().ContainKey("RelName");
        reloaded.NamedFormulas["RelName"].Should().Be("Sheet1!A1");
        reloaded.NamedRanges.Should().NotContainKey("RelName");
    }

    [Fact]
    public void Load_RelativeReferenceDefinedName_ResolvesRelativeToUsingCell()
    {
        // RelName's RefersTo ("Sheet1!A2", implicit A1-of-using-sheet anchor) shifted by the delta
        // between the using cell (B2, i.e. +1 row/+1 col from A1) and the anchor lands on B3 when
        // used FROM B2 — matching Excel's per-cell relative-name evaluation.
        using var source = BuildSourcePackageWithDefinedName("RelName", "Sheet1!A2");

        var loaded = new XlsxFileAdapter().Load(source);
        var sheet = loaded.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(7)); // B3
        sheet.SetFormula(new CellAddress(sheet.Id, 2, 2), "RelName"); // B2

        var engine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());
        engine.RecalculateAllFormulas(loaded);

        sheet.GetValue(2, 2).Should().Be(new NumberValue(7));
    }

    /// <summary>Sibling no-regression: a fully-absolute reference still resolves to a GridRange.</summary>
    [Fact]
    public void Load_FullyAbsoluteReferenceDefinedName_StillResolvesToGridRange_NoRegression()
    {
        using var source = BuildSourcePackageWithDefinedName("AbsName", "Sheet1!$A$1:$A$5");

        var loaded = new XlsxFileAdapter().Load(source);
        var sheet1 = loaded.GetSheetAt(0);

        loaded.NamedRanges.Should().ContainKey("AbsName");
        loaded.NamedFormulas.Should().NotContainKey("AbsName");
        loaded.NamedRanges["AbsName"].Should().Be(new GridRange(
            new CellAddress(sheet1.Id, 1, 1),
            new CellAddress(sheet1.Id, 5, 1)));
    }

    /// <summary>
    /// Builds a single-sheet workbook via the real save path, then injects a raw
    /// &lt;definedName&gt; element directly into xl/workbook.xml — mirroring how a real Excel-
    /// authored file carries a defined name, since ClosedXML's own DefinedNames.Add API cannot be
    /// relied on to author these unusual RefersTo shapes.
    /// </summary>
    private static MemoryStream BuildSourcePackageWithDefinedName(string name, string refersTo)
    {
        var workbook = new Workbook("R66DefinedNameScope");
        workbook.AddSheet("Sheet1");

        var package = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, package);

        package.Position = 0;
        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = archive.GetEntry("xl/workbook.xml")!;
            var workbookXml = XlsxPackageXmlEditor.LoadXml(entry);
            var root = workbookXml.Root!;

            var definedNames = root.Element(WorkbookNs + "definedNames");
            if (definedNames is null)
            {
                definedNames = new XElement(WorkbookNs + "definedNames");
                var sheets = root.Element(WorkbookNs + "sheets");
                if (sheets is not null)
                    sheets.AddAfterSelf(definedNames);
                else
                    root.Add(definedNames);
            }

            var definedNameElement = new XElement(
                WorkbookNs + "definedName",
                new XAttribute("name", name),
                refersTo);
            definedNames.Add(definedNameElement);
            XlsxPackageXmlEditor.ReplaceXml(archive, "xl/workbook.xml", workbookXml);
        }

        package.Position = 0;
        return package;
    }
}
