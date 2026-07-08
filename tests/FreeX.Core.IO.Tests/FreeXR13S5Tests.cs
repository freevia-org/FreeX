using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Formula;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression coverage for round-13 bucket S5 finding R13-other-format-adapters-1:
/// <see cref="OdsFormulaConverter"/> never translated OpenFormula's ';' argument separator to/from
/// FreeX's ',' (the only separator FreeX's <see cref="Parser"/> accepts outside array constants), so
/// any multi-argument function (IF, VLOOKUP, SUMIF, ...) round-tripping through .ods was broken in
/// both directions.
/// </summary>
public sealed class FreeXR13S5Tests
{
    private static readonly XNamespace OfficeNs = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
    private static readonly XNamespace TableNs = "urn:oasis:names:tc:opendocument:xmlns:table:1.0";
    private static readonly XNamespace TextNs = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";

    /// <summary>Builds a minimal, hand-written ODS package (no FreeX round-trip involved) whose B1
    /// carries a raw LibreOffice-style <c>table:formula</c> attribute, simulating a real
    /// LibreOffice/Calc-authored file rather than one produced by FreeX's own writer.</summary>
    private static Stream BuildOdsPackageWithFormula(string tableFormulaAttribute)
    {
        var content = new XElement(OfficeNs + "document-content",
            new XAttribute(XNamespace.Xmlns + "office", OfficeNs.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "table", TableNs.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "text", TextNs.NamespaceName),
            new XElement(OfficeNs + "body",
                new XElement(OfficeNs + "spreadsheet",
                    new XElement(TableNs + "table",
                        new XAttribute(TableNs + "name", "Sheet1"),
                        new XElement(TableNs + "table-row",
                            new XElement(TableNs + "table-cell",
                                new XAttribute(OfficeNs + "value-type", "float"),
                                new XAttribute(OfficeNs + "value", "5"),
                                new XElement(TextNs + "p", "5")),
                            new XElement(TableNs + "table-cell",
                                new XAttribute(OfficeNs + "value-type", "float"),
                                // Deliberately a wrong cached value (Excel's/Calc's would be 1): forces
                                // the assertion below to depend on genuine recalculation of the parsed
                                // formula rather than an unrelated cached value happening to match.
                                new XAttribute(OfficeNs + "value", "999"),
                                new XAttribute(TableNs + "formula", tableFormulaAttribute),
                                new XElement(TextNs + "p", "999")))))));

        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("content.xml");
            using var entryStream = entry.Open();
            new XDocument(content).Save(entryStream);
        }

        stream.Position = 0;
        return stream;
    }

    [Fact]
    public void Load_LibreOfficeAuthoredSemicolonArgumentSeparators_TranslatesToCommasAndEvaluates()
    {
        // A real LibreOffice-authored .ods: of:=IF([.A1]>0;1;2) — the OpenFormula-conformant
        // semicolon argument separator, exactly as cited in the finding.
        using var package = BuildOdsPackageWithFormula("of:=IF([.A1]>0;1;2)");

        var workbook = new OdsFileAdapter().Load(package);
        var sheet = workbook.Sheets.Single();

        var formulaCell = sheet.GetCell(1, 2);
        formulaCell.Should().NotBeNull();
        // Pre-fix, OdsFormulaConverter.ToA1 left the semicolons untouched: "IF(A1>0;1;2)", which
        // FreeX's Parser (comma-only argument separator) cannot parse.
        formulaCell!.FormulaText.Should().Be("IF(A1>0,1,2)");

        // The imported text must actually be usable by FreeX's own parser — pre-fix this throws
        // FormulaParseException ("Expected current-row structured reference"/close-paren mismatch).
        var parsed = FormulaEvaluator.ParseFormula(formulaCell.FormulaText!);
        parsed.Should().NotBeNull();

        var engine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());
        engine.RecalculateAllFormulas(workbook);

        // A1(5) > 0, so IF must evaluate to its second argument, 1 — matching Excel/Calc's result.
        sheet.GetValue(1, 2).Should().Be(new NumberValue(1));
    }

    [Fact]
    public void Save_MultiArgumentFormula_EmitsOpenFormulaConformantSemicolonsNotCommas()
    {
        var wb = new Workbook("Untitled");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(5)));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), Cell.FromFormula("IF(A1>0,1,2)"));

        using var stream = new MemoryStream();
        new OdsFileAdapter().Save(wb, stream);
        stream.Position = 0;

        string savedFormula;
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
        {
            var entry = archive.GetEntry("content.xml")!;
            using var entryStream = entry.Open();
            var doc = XDocument.Load(entryStream);
            var cell = doc.Descendants(TableNs + "table-cell")
                .First(e => e.Attribute(TableNs + "formula") is not null);
            savedFormula = (string)cell.Attribute(TableNs + "formula")!;
        }

        // Pre-fix, FreeX emitted "of:=IF([.A1]>0,1,2)" — commas are not a valid OpenFormula argument
        // separator, so LibreOffice/OpenOffice/Calc would fail to evaluate this formula on open. This
        // checks the raw table:formula attribute directly (not FreeX's own reload, which prefers the
        // lossless freex-a1-formula hint and would mask the bug from a third-party reader's view).
        savedFormula.Should().Be("of:=IF([.A1]>0;1;2)");
    }
}
