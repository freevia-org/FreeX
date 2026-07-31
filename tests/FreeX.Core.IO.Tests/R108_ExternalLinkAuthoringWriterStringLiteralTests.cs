using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R108-io-external-link-string-literal-false-positive-1: <c>XlsxExternalLinkAuthoringWriter</c>'s
/// <c>QuotedExternalReferencePattern</c> is a raw-text regex over <see cref="Cell.FormulaText"/> / the
/// saved worksheet <c>&lt;f&gt;</c> text with no notion of Excel's actual quoting rules. A formula whose
/// entire body is a double-quoted STRING LITERAL that merely happens to contain the same
/// <c>'[Book]Sheet'!</c> shape (e.g. a user typing <c>="'[Budget.xlsx]Data'!A1"</c> to show example
/// formula syntax) is not an external reference at all -- Excel just evaluates it to that literal text,
/// with no Edit Links entry and no <c>xl/externalLinks</c> part. Before the fix this writer matched
/// inside the string literal exactly as it would a genuine reference: it synthesized a bogus
/// externalLink backing chain for a book the user never referenced, and then rewrote the persisted
/// <c>&lt;f&gt;</c> text in place from the quoted-filename form to the numeric-ordinal form -- silently
/// corrupting the string literal's own value on save.
/// </summary>
public sealed class R108_ExternalLinkAuthoringWriterStringLiteralTests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    /// <summary>
    /// The primary proof: a formula whose whole body is a double-quoted string literal shaped like a
    /// bracketed external reference must round-trip through the real save path byte-for-byte in its
    /// formula text, and must not cause any external-link infrastructure to be synthesized.
    /// </summary>
    [Fact]
    public void FreshWorkbook_StringLiteralResemblingExternalReference_IsNotTreatedAsExternalLink()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        const string formulaText = "\"'[Budget.xlsx]Data'!A1\""; // ="'[Budget.xlsx]Data'!A1" -- a string literal, not a reference.
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.SetFormula(address, formulaText);

        using var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);

        saved.Position = 0;
        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);

        // 1. No externalLinks parts were synthesized at all.
        archive.Entries.Any(entry => entry.FullName.StartsWith("xl/externalLinks/", System.StringComparison.OrdinalIgnoreCase))
            .Should().BeFalse("a string literal that merely resembles a bracketed reference must not synthesize any external-link backing part");

        // 2. workbook.xml carries no <externalReferences> element (or it is empty).
        var workbookXml = LoadXml(archive, "xl/workbook.xml");
        var externalReferences = workbookXml.Root!.Element(WorkbookNs + "externalReferences");
        (externalReferences is null || !externalReferences.Elements().Any()).Should()
            .BeTrue("no genuine external-workbook reference was ever typed, so no <externalReference> entry may exist");

        // 3. The saved worksheet <f> text is byte-for-byte identical to what was typed -- the writer
        //    must not rewrite the quoted-filename shape inside the string literal to a numeric ordinal.
        var worksheetXml = LoadXml(archive, "xl/worksheets/sheet1.xml");
        var savedFormulaText = worksheetXml.Root!
            .Element(WorkbookNs + "sheetData")!
            .Elements(WorkbookNs + "row")
            .Elements(WorkbookNs + "c")
            .Elements(WorkbookNs + "f")
            .Select(element => element.Value)
            .SingleOrDefault();

        savedFormulaText.Should().Be(formulaText, "the string literal's content must not be mutated by the external-link authoring writer");
    }

    /// <summary>
    /// No-regression sibling: when the SAME sheet has one cell holding a genuine bracketed external
    /// reference to a book and another cell holding a string literal merely shaped like a reference to
    /// the SAME book name, the writer must still synthesize backing for (and ordinal-rewrite) only the
    /// genuine reference -- the string-literal cell's formula text must survive completely untouched,
    /// and only ONE externalLink part must be produced (not a duplicate, and not zero). This proves the
    /// fix discriminates per-match rather than either over- or under-suppressing the whole scan.
    /// (A single formula combining both shapes, e.g. via IF(), hits an unrelated ClosedXML formula-
    /// parser limitation on external-reference syntax nested inside a function call, so the two shapes
    /// are exercised in separate cells here instead.)
    /// </summary>
    [Fact]
    public void FreshWorkbook_GenuineReferenceAndStringLiteralLookalikeInSameSheet_OnlyGenuineReferenceIsAuthored()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        const string genuineFormulaText = "'[Budget.xlsx]Data'!A1";
        const string literalFormulaText = "\"'[Budget.xlsx]Data'!A1\"";
        sheet.SetFormula(new CellAddress(sheet.Id, 1, 1), genuineFormulaText);
        sheet.SetFormula(new CellAddress(sheet.Id, 2, 1), literalFormulaText);

        using var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);

        saved.Position = 0;
        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);

        // Exactly one externalLink part was synthesized -- for the genuine reference only, not
        // duplicated because the string-literal cell happens to name the same book.
        var externalLinkParts = archive.Entries
            .Where(entry =>
                entry.FullName.StartsWith("xl/externalLinks/externalLink", System.StringComparison.OrdinalIgnoreCase) &&
                entry.FullName.EndsWith(".xml", System.StringComparison.OrdinalIgnoreCase) &&
                !entry.FullName.Contains("_rels", System.StringComparison.OrdinalIgnoreCase))
            .ToList();
        externalLinkParts.Should().ContainSingle();

        var workbookXml = LoadXml(archive, "xl/workbook.xml");
        workbookXml.Root!
            .Element(WorkbookNs + "externalReferences")!
            .Elements(WorkbookNs + "externalReference")
            .Should().ContainSingle();

        // The genuine reference's <f> text is rewritten to the numeric-ordinal form, but the string
        // literal's <f> text is byte-for-byte untouched.
        var worksheetXml = LoadXml(archive, "xl/worksheets/sheet1.xml");
        var savedFormulaTexts = worksheetXml.Root!
            .Element(WorkbookNs + "sheetData")!
            .Elements(WorkbookNs + "row")
            .Elements(WorkbookNs + "c")
            .Elements(WorkbookNs + "f")
            .Select(element => element.Value)
            .ToList();

        savedFormulaTexts.Should().Contain("'[1]Data'!A1");
        savedFormulaTexts.Should().Contain(literalFormulaText);
    }

    private static XDocument LoadXml(ZipArchive archive, string entryName)
    {
        var entry = archive.GetEntry(entryName) ?? throw new FileNotFoundException(entryName);
        using var entryStream = entry.Open();
        return XDocument.Load(entryStream);
    }
}
