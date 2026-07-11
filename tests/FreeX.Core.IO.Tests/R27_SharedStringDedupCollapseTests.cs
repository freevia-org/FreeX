using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R27-io-shared-strings-deep-1: when the target's SST regeneration collapses two distinct
/// same-text rich source occurrences into a single shared target entry (because FreeX's rich-run
/// model does not capture whatever rPr sub-element distinguished them), the count-mismatched
/// positional pairing must NOT graft one source cell's exact rich XML onto that now-shared target
/// entry -- doing so would silently overwrite the OTHER cell(s)' formatting instead of merely
/// losing the unmodeled detail.
/// </summary>
public sealed class R27_SharedStringDedupCollapseTests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Fact]
    public void PreserveRichTextAndPhonetics_TargetDedupCollapsesTwoSourceEntries_DoesNotCrossContaminateSharedTarget()
    {
        // Source has 2 DISTINCT rich "Dup" entries (bold vs. italic) -- e.g. from two cells whose
        // runs differed only in a property the model doesn't capture, so after a load+save round
        // trip ClosedXML's own SST regeneration dedupes them into a single shared target entry.
        using var sourcePackage = XlsxPackageTestFixtures.CreatePackage(("xl/sharedStrings.xml", """
            <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <si><r><rPr><b/></rPr><t>Dup</t></r></si>
              <si><r><rPr><i/></rPr><t>Dup</t></r></si>
            </sst>
            """));

        // Target only has ONE rich "Dup" entry left -- both cells (A1 and A2) now reference the
        // SAME shared-string index.
        using var targetPackage = XlsxPackageTestFixtures.CreatePackage(
            ("xl/sharedStrings.xml", """
                <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <si><r><t>Dup</t></r></si>
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
        var xml = XlsxPackageTestFixtures.LoadPackageXml(verifyArchive, "xl/sharedStrings.xml", "xl/sharedStrings.xml");
        var strings = xml.Root!.Elements(WorkbookNs + "si").ToList();

        strings.Should().HaveCount(1, "the shared target entry must not be split or duplicated");

        // Neither source formatting (bold or italic) may be grafted onto the shared target entry:
        // A2 would silently inherit whichever one got picked, corrupting its own formatting.
        strings[0].Descendants(WorkbookNs + "b").Should().BeEmpty(
            "grafting cell A1's bold formatting onto the shared entry would corrupt A2's rendering");
        strings[0].Descendants(WorkbookNs + "i").Should().BeEmpty(
            "grafting cell A2's italic formatting onto the shared entry would corrupt A1's rendering");
    }

    [Fact]
    public void PreserveRichTextAndPhonetics_DuplicateText_MatchingCounts_StillPairsCorrectly()
    {
        // Sibling already-working case: source and target both have exactly 2 same-text rich
        // occurrences (no dedup collapse), so the 1:1 positional pairing must still apply and
        // preserve each cell's own distinct formatting.
        using var sourcePackage = XlsxPackageTestFixtures.CreatePackage(("xl/sharedStrings.xml", """
            <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <si><r><rPr><b/></rPr><t>Dup</t></r></si>
              <si><r><rPr><i/></rPr><t>Dup</t></r></si>
            </sst>
            """));
        using var targetPackage = XlsxPackageTestFixtures.CreatePackage(
            ("xl/sharedStrings.xml", """
                <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <si><r><t>Dup</t></r></si>
                  <si><r><t>Dup</t></r></si>
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

        using var sourceArchive = new ZipArchive(sourcePackage, ZipArchiveMode.Read, leaveOpen: true);
        using (var targetArchive = new ZipArchive(targetPackage, ZipArchiveMode.Update, leaveOpen: true))
        {
            XlsxSharedStringMetadataPreserver.PreserveRichTextAndPhonetics(sourceArchive, targetArchive);
        }

        targetPackage.Position = 0;
        using var verifyArchive = new ZipArchive(targetPackage, ZipArchiveMode.Read, leaveOpen: true);
        var xml = XlsxPackageTestFixtures.LoadPackageXml(verifyArchive, "xl/sharedStrings.xml", "xl/sharedStrings.xml");
        var strings = xml.Root!.Elements(WorkbookNs + "si").ToList();

        strings.Should().HaveCount(2);
        strings[0].Descendants(WorkbookNs + "b").Should().NotBeEmpty(
            "target index 0 backs cell A1, which used the BOLD source string");
        strings[1].Descendants(WorkbookNs + "i").Should().NotBeEmpty(
            "target index 1 backs cell A2, which used the ITALIC source string");
    }
}
