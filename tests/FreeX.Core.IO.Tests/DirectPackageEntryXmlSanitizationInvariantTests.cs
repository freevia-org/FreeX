using FluentAssertions;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// FreeX's half of the direct-package-entry sanitization tripwire. The scan itself, and the full
/// statement of what it can and cannot see, lives in
/// <see cref="PackageEntryXmlSanitizationScanner"/>; this file supplies FreeX's source root, its
/// allowlist, and the writers that must stay covered.
/// <para>
/// Both bypasses this exists for are real: <c>XlsxWorksheetChartWriter</c> was the originally-reported
/// site, and <c>XlsxWorkbookThemeWriter</c> was found by the audit that added this test and really did
/// abort a save on a control character in the workbook theme name.
/// </para>
/// </summary>
public sealed class DirectPackageEntryXmlSanitizationInvariantTests
{
    /// <summary>
    /// Sites that serialize a document they just PARSED OUT OF the archive, mutate it structurally (an
    /// attribute rename, an added relationship element) and write it back. XML that parsed cannot contain
    /// an XML-1.0-illegal character, and neither site introduces model text, so both are safe without a
    /// sanitize. Keyed by file AND saved identifier so that adding model text to one changes the line and
    /// forces a fresh look, rather than riding on a stale exemption.
    /// </summary>
    private static readonly (string File, string SavedDocument)[] ReserializesParsedXml =
    [
        // Rewrites <color rgb="00000000"> to <color auto="1"/> in the shared strings ClosedXML wrote.
        ("XlsxFileAdapter.SavePostProcessing.cs", "doc"),
        // Adds one <Relationship> element (generated id/type/target) to a parsed .rels part.
        ("XlsxWorksheetBackgroundReaderWriter.cs", "relsXml"),
    ];

    // The writers this tripwire exists for. Asserted by name every run so that a refactor which changes
    // the call shape fails loudly, instead of the scan silently matching nothing and passing vacuously.
    private static readonly string[] MustStayCovered =
    [
        "XlsxWorksheetChartWriter.cs",
        "XlsxWorkbookThemeWriter.cs",
    ];

    [Fact]
    public void EveryDirectPackageEntryXmlWrite_SanitizesOrIsExplicitlyExempt()
    {
        var sites = PackageEntryXmlSanitizationScanner.Scan(
            Path.Combine(
                TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx"),
                "src",
                "FreeX.Core.IO"),
            "SanitizeInPlace");

        foreach (var writer in MustStayCovered)
        {
            sites.Select(site => site.FileName).Should().Contain(
                writer,
                "this scan exists for {0}; if it no longer matches, the regex has gone stale rather than the bypass having gone away",
                writer);
        }

        var offenders = sites
            .Where(site => !site.Sanitizes)
            .Where(site => !ReserializesParsedXml.Contains((site.FileName, site.SavedDocument)))
            .Select(site => site.ToString());

        // Joined rather than asserted on the list: FluentAssertions' BeEmpty reports only the FIRST item,
        // which hid a second offender while this guard was being validated. A guard that names one of
        // three bypasses is a guard you have to run three times.
        string.Join(Environment.NewLine, offenders).Should().BeEmpty(
            "a package part written straight to a zip entry skips OpcXml's sanitize, so one control character or lone surrogate in its model text aborts the whole workbook save");
    }
}
