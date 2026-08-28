namespace FreeW.Core.IO.Tests;

/// <summary>
/// FreeW's half of the direct-package-entry sanitization tripwire. The scan itself, and the full
/// statement of what it can and cannot see, lives in
/// <see cref="PackageEntryXmlSanitizationScanner"/>; this file supplies FreeW's source root, its
/// allowlist, and the writer that must stay covered.
/// <para>
/// FreeW's stake in this is concrete: <c>OdtFileAdapter</c> wrote content.xml, styles.xml AND meta.xml
/// with no sanitization at all, so a control character in a paragraph or in the document Title aborted
/// File &gt; Save As &gt; OpenDocument Text. That was closed by sanitizing inside
/// <c>OpenDocumentPackageWriter.WriteXmlEntry</c>, which is why the ODT adapter no longer appears here
/// at all -- it no longer creates its own entries. This guard exists so the next writer that DOES
/// create its own entries cannot repeat the mistake unnoticed.
/// </para>
/// </summary>
public class DirectPackageEntryXmlSanitizationInvariantTests
{
    /// <summary>
    /// Both sites are on the flat-OPC LOAD path: <c>WordXmlFileAdapter</c> rehydrates a parsed
    /// <c>pkg:package</c> document into an in-memory .docx, writing back element trees it just read with
    /// <c>XDocument.Load</c>. XML that parsed cannot contain an XML-1.0-illegal character, and no model
    /// text is introduced, so neither needs a sanitize. (Its Save path is safe for the same reason: it
    /// re-reads each part of the .docx DocxWriter just produced, rather than serializing model text.)
    /// </summary>
    private static readonly (string File, string SavedDocument)[] ReserializesParsedXml =
    [
        (
            "WordXmlFileAdapter.cs",
            PackageEntryXmlSanitizationScanner.InlineExpression
        ),
    ];

    // Asserted by name every run so a refactor that changes the call shape fails loudly, instead of the
    // scan silently matching nothing and passing vacuously. This very nearly happened: FreeW's only
    // direct sites save an expression rather than a named variable, which an identifier-only pattern
    // missed entirely.
    private const string MustStayCovered = "WordXmlFileAdapter.cs";

    [Fact]
    public void EveryDirectPackageEntryXmlWrite_SanitizesOrIsExplicitlyExempt()
    {
        var sites = PackageEntryXmlSanitizationScanner.Scan(
            Path.Combine(
                TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx"),
                "freew",
                "FreeW.Core.IO"),
            "SanitizeInPlace");

        sites.Select(site => site.FileName).Should().Contain(
            MustStayCovered,
            "this scan exists to watch FreeW's direct package-entry writes; if it no longer matches, the regex has gone stale rather than the bypass having gone away");

        var offenders = sites
            .Where(site => !site.Sanitizes)
            .Where(site => !ReserializesParsedXml.Contains((site.FileName, site.SavedDocument)))
            .Select(site => site.ToString());

        // Joined rather than asserted on the list: FluentAssertions' BeEmpty reports only the FIRST item.
        string.Join(Environment.NewLine, offenders).Should().BeEmpty(
            "a package part written straight to a zip entry skips the shared package writers' sanitize, so one control character or lone surrogate in its model text aborts the whole document save");
    }
}
