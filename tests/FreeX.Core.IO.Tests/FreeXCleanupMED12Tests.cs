using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Focused regression tests for FreeX cleanup batch MED12 (round-10 MED/LOW findings).
/// </summary>
public sealed class FreeXCleanupMED12Tests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    /// <summary>
    /// P70: when a plain si and a rich si share the same text ("Hello" plain-first-use, then
    /// "Hello" with rich runs, as ClosedXML emits), the positional duplicate-text fallback must
    /// pair the rich SOURCE occurrence only against a rich TARGET occurrence sharing that text —
    /// never against an unrelated plain target si that merely shares the text. Otherwise the
    /// plain si (and every cell using it) is silently promoted to the source's rich formatting.
    /// </summary>
    [Fact]
    public void PreserveRichTextAndPhonetics_DuplicateTextPositionalFallback_NeverStampsRichOntoPlainTarget()
    {
        using var sourcePackage = XlsxPackageTestFixtures.CreatePackage(("xl/sharedStrings.xml", """
            <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <si>
                <r><rPr><b/></rPr><t>Hello</t></r>
              </si>
            </sst>
            """));
        // Target mirrors ClosedXML's full regeneration: the plain first-use si comes first,
        // followed by a second si with identical plain text that (in this file) is also plain --
        // i.e. there is NO rich target occurrence of "Hello" for the rich source run to pair with.
        using var targetPackage = XlsxPackageTestFixtures.CreatePackage(("xl/sharedStrings.xml", """
            <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <si><t>Hello</t></si>
              <si><t>Hello</t></si>
            </sst>
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
        // Neither target si may have been positionally clobbered with the source's rich <r> run --
        // there was no rich target occurrence to legitimately pair it with.
        strings[0].Elements(WorkbookNs + "r").Should().BeEmpty("the first (plain) target si must not be stamped with rich formatting from an unrelated source si");
        strings[1].Elements(WorkbookNs + "r").Should().BeEmpty("the second (plain) target si must not be stamped with rich formatting from an unrelated source si either");
    }

    /// <summary>
    /// P70 (positive case): when the target DOES have a genuinely rich occurrence sharing the
    /// duplicate text, the positional fallback must still pair the rich source run onto that rich
    /// target occurrence (not the earlier plain one), preserving the documented "first rich source
    /// occurrence to first rich target occurrence" invariant.
    /// </summary>
    [Fact]
    public void PreserveRichTextAndPhonetics_DuplicateTextPositionalFallback_PairsRichSourceWithRichTarget()
    {
        using var sourcePackage = XlsxPackageTestFixtures.CreatePackage(("xl/sharedStrings.xml", """
            <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <si>
                <r><rPr><b/></rPr><t>Hello</t></r>
              </si>
            </sst>
            """));
        // Target: plain first-use si, then a rich si with identical text (matching the finding's
        // exact B1/A2 scenario: plain "Hello" at index 0, rich "Hello" at index 1).
        using var targetPackage = XlsxPackageTestFixtures.CreatePackage(("xl/sharedStrings.xml", """
            <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <si><t>Hello</t></si>
              <si><r><t>Hello</t></r></si>
            </sst>
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
        strings[0].Elements(WorkbookNs + "r").Should().BeEmpty("the plain first-use si must stay plain");
        strings[1].Elements(WorkbookNs + "r").Should().HaveCount(1, "the rich target occurrence should receive the source's rich run");
        strings[1].Elements(WorkbookNs + "r").Single().Element(WorkbookNs + "rPr")!.Element(WorkbookNs + "b").Should().NotBeNull();
    }
}
