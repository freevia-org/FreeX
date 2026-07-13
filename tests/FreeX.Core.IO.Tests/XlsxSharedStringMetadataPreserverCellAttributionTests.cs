using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R40-io-sst-richtext-3-1: the phonetic/rich shared-string patch-back must only be applied to the
/// cell(s) that ORIGINALLY carried the rich/phonetic content. When a source workbook has two
/// distinct shared-string entries with identical plain text -- one plain (cell A1) and one
/// phonetic/rich (cell A2) -- and a ClosedXML full rebuild collapses both into a single shared
/// target entry (because the model can't capture the phonetic/rich distinction), the patch-back
/// must not graft A2's ruby onto A1. It must instead split a new shared-string entry for A2 and
/// redirect only A2's cell reference, leaving A1 untouched.
/// </summary>
public sealed class XlsxSharedStringMetadataPreserverCellAttributionTests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Fact]
    public void PreserveRichTextAndPhonetics_TwoCellsShareTextButOnlyOneHadRuby_OnlyThatCellKeepsRubyAfterRoundTrip()
    {
        using var sourcePackage = XlsxPackageTestFixtures.CreatePackage(
            ("xl/sharedStrings.xml", """
                <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" count="2" uniqueCount="2">
                  <si><t>Tanaka</t></si>
                  <si>
                    <t>Tanaka</t>
                    <rPh sb="0" eb="6"><t>タナカ</t></rPh>
                    <phoneticPr fontId="1"/>
                  </si>
                </sst>
                """),
            ("xl/worksheets/sheet1.xml", """
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <sheetData>
                    <row r="1"><c r="A1" t="s"><v>0</v></c></row>
                    <row r="2"><c r="A2" t="s"><v>1</v></c></row>
                  </sheetData>
                </worksheet>
                """));

        // Simulates the post-ClosedXML-rebuild target: both A1 and A2 collapsed onto the SAME
        // (plain) shared-string entry because the rich-run/phonetic model never captured the ruby.
        using var targetPackage = XlsxPackageTestFixtures.CreatePackage(
            ("xl/sharedStrings.xml", """
                <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" count="2" uniqueCount="1">
                  <si><t>Tanaka</t></si>
                </sst>
                """),
            ("xl/worksheets/sheet1.xml", """
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <sheetData>
                    <row r="1"><c r="A1" t="s"><v>0</v></c></row>
                    <row r="2"><c r="A2" t="s"><v>0</v></c></row>
                  </sheetData>
                </worksheet>
                """));

        using var sourceArchive = new ZipArchive(sourcePackage, ZipArchiveMode.Read, leaveOpen: true);
        using (var targetArchive = new ZipArchive(targetPackage, ZipArchiveMode.Update, leaveOpen: true))
        {
            XlsxSharedStringMetadataPreserver.PreserveRichTextAndPhonetics(sourceArchive, targetArchive);
        }

        targetPackage.Position = 0;
        using var verifyArchive = new ZipArchive(targetPackage, ZipArchiveMode.Read, leaveOpen: true);

        var sharedStringsXml = XlsxPackageTestFixtures.LoadPackageXml(verifyArchive, "xl/sharedStrings.xml");
        var sharedStrings = sharedStringsXml.Root!.Elements(WorkbookNs + "si").ToList();

        var worksheetXml = XlsxPackageTestFixtures.LoadPackageXml(verifyArchive, "xl/worksheets/sheet1.xml");
        var cells = worksheetXml.Root!.Descendants(WorkbookNs + "c").ToList();
        var cellA1 = cells.Single(c => c.Attribute("r")!.Value == "A1");
        var cellA2 = cells.Single(c => c.Attribute("r")!.Value == "A2");

        var a1Index = int.Parse(cellA1.Element(WorkbookNs + "v")!.Value);
        var a2Index = int.Parse(cellA2.Element(WorkbookNs + "v")!.Value);

        // The two cells must now point at DIFFERENT shared-string entries -- the patch-back must
        // not have left them sharing one (now-contaminated) entry.
        a1Index.Should().NotBe(a2Index, "A1 never had ruby and must not end up sharing an entry with A2's phonetic content");

        var a1String = sharedStrings[a1Index];
        var a2String = sharedStrings[a2Index];

        a1String.Element(WorkbookNs + "rPh").Should().BeNull("A1 never had phonetic metadata in the source and must not gain any");
        a1String.Element(WorkbookNs + "phoneticPr").Should().BeNull("A1 never had phonetic metadata in the source and must not gain any");

        a2String.Element(WorkbookNs + "rPh").Should().NotBeNull("A2 had ruby in the source and must keep it after round-trip");
        a2String.Element(WorkbookNs + "rPh")!.Element(WorkbookNs + "t")!.Value.Should().Be("タナカ");
        a2String.Element(WorkbookNs + "phoneticPr").Should().NotBeNull();
    }

    [Fact]
    public void PreserveRichTextAndPhonetics_SingleOwnerCell_StillPatchesInPlace()
    {
        // No-regression sibling: when the rich source string's cell maps 1:1 to a target entry
        // referenced by that same single cell (the common case, no dedup collision), the fix must
        // keep patching the existing entry in place rather than needlessly splitting a new one.
        using var sourcePackage = XlsxPackageTestFixtures.CreatePackage(
            ("xl/sharedStrings.xml", """
                <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" count="1" uniqueCount="1">
                  <si>
                    <t>Rich phonetic</t>
                    <rPh sb="0" eb="4"><t>ri-chi</t></rPh>
                    <phoneticPr fontId="1"/>
                  </si>
                </sst>
                """),
            ("xl/worksheets/sheet1.xml", """
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <sheetData>
                    <row r="1"><c r="A1" t="s"><v>0</v></c></row>
                  </sheetData>
                </worksheet>
                """));

        using var targetPackage = XlsxPackageTestFixtures.CreatePackage(
            ("xl/sharedStrings.xml", """
                <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" count="1" uniqueCount="1">
                  <si><t>Rich phonetic</t></si>
                </sst>
                """),
            ("xl/worksheets/sheet1.xml", """
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <sheetData>
                    <row r="1"><c r="A1" t="s"><v>0</v></c></row>
                  </sheetData>
                </worksheet>
                """));

        using var sourceArchive = new ZipArchive(sourcePackage, ZipArchiveMode.Read, leaveOpen: true);
        using (var targetArchive = new ZipArchive(targetPackage, ZipArchiveMode.Update, leaveOpen: true))
        {
            XlsxSharedStringMetadataPreserver.PreserveRichTextAndPhonetics(sourceArchive, targetArchive);
        }

        targetPackage.Position = 0;
        using var verifyArchive = new ZipArchive(targetPackage, ZipArchiveMode.Read, leaveOpen: true);

        var sharedStringsXml = XlsxPackageTestFixtures.LoadPackageXml(verifyArchive, "xl/sharedStrings.xml");
        var sharedStrings = sharedStringsXml.Root!.Elements(WorkbookNs + "si").ToList();

        // No new entry should have been appended -- the single pre-existing entry is patched in place.
        sharedStrings.Should().HaveCount(1);
        sharedStrings[0].Element(WorkbookNs + "rPh").Should().NotBeNull();
        sharedStrings[0].Element(WorkbookNs + "phoneticPr").Should().NotBeNull();

        var worksheetXml = XlsxPackageTestFixtures.LoadPackageXml(verifyArchive, "xl/worksheets/sheet1.xml");
        var cellA1 = worksheetXml.Root!.Descendants(WorkbookNs + "c").Single(c => c.Attribute("r")!.Value == "A1");
        cellA1.Element(WorkbookNs + "v")!.Value.Should().Be("0", "the single owner cell should keep referencing the same (now-patched) shared-string index");
    }
}
