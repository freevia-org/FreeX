using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

/// <summary>
/// Shared scanner behind each app's direct-package-entry sanitization tripwire.
/// <para>
/// FreeX, FreeW and FreeP all write OOXML/ODF packages, and all three route almost every part through
/// a shared writer that sanitizes XML-1.0-illegal characters on the way out (<c>OpcXml</c> for OOXML,
/// <c>OpenDocumentPackageWriter</c> for ODF). A writer that instead calls <c>archive.CreateEntry(...)</c>
/// and serializes an <c>XDocument</c> straight into the returned stream skips that boundary, and one C0
/// control code or lone surrogate in the model text it serializes aborts the WHOLE document save with an
/// <c>ArgumentException</c> and no file written.
/// </para>
/// <para>
/// This has already happened three times: FreeX's <c>XlsxWorksheetChartWriter</c> (the originally-reported
/// site), FreeX's <c>XlsxWorkbookThemeWriter</c> (found by the audit that first added this scan, and it
/// really did abort saves on a control character in the theme name), and FreeW's <c>OdtFileAdapter</c>
/// (three unsanitized parts, closed by moving the sanitize into the ODF package writer). The scan lives
/// here rather than being copy-pasted per app so the three tripwires cannot drift apart.
/// </para>
/// <para>
/// <b>Scope, stated plainly so the next reviewer does not over-trust it.</b> This is a regex over source
/// text and it is a backstop, not a proof. It does NOT see: a site that hands the stream to a helper
/// which saves it elsewhere (the <c>.Save(</c> lands in another method); a document serialized through an
/// <c>XmlWriter</c> the scan cannot tie back to a package entry; or XML built by string concatenation and
/// written as raw bytes. The durable guarantee is the sanitize inside the shared package writers -- this
/// only catches the specific shape that has bitten us repeatedly.
/// </para>
/// </summary>
internal static class PackageEntryXmlSanitizationScanner
{
    /// <summary>A direct <c>CreateEntry</c> + <c>XDocument.Save</c> site found in the scanned sources.</summary>
    internal sealed record Site(string FileName, string SavedDocument, bool Sanitizes)
    {
        public override string ToString() =>
            $"{FileName}: '{SavedDocument}.Save(...)' into a directly-created package entry, with no sanitize before it";
    }

    // "var e = archive.CreateEntry(...);" then, within a few statements, "someDoc.Save(stream/writer)".
    // Captures the identifier being saved so a caller can allowlist a specific site precisely. The
    // second alternative catches a document saved straight off an expression -- new XDocument(x).Save(s)
    // -- which has no identifier to capture; FreeW's only direct site has exactly that shape, and an
    // identifier-only pattern would have made FreeW's tripwire silently match nothing.
    private static readonly Regex DirectEntrySave = new(
        @"CreateEntry\([^;]*;(?:[^;]*;){0,3}?[^;]*?(?:\b(?<doc>[A-Za-z_][A-Za-z0-9_]*)|\))\.Save\(",
        RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>Stands in for the saved identifier at a site that saves an expression directly.</summary>
    internal const string InlineExpression = "<inline expression>";

    // How far back to look for the sanitize call: far enough to cover the method that built the document,
    // short enough that an unrelated sanitize elsewhere in a large file cannot vouch for this site.
    private const int LookbackCharacters = 1500;

    /// <summary>
    /// Scans every <c>.cs</c> file under <paramref name="sourceRoot"/> and returns one entry per direct
    /// package-entry XML write, saying whether a sanitize call appears just above it.
    /// </summary>
    /// <param name="sanitizeCallNames">
    /// The sanitize entry points that count as covering a site -- passed in because the apps reach the
    /// shared sanitizer under different names (a direct <c>XmlTextSanitizer.SanitizeInPlace</c>, or a
    /// project-local wrapper).
    /// </param>
    internal static IReadOnlyList<Site> Scan(string sourceRoot, params string[] sanitizeCallNames)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRoot);
        if (sanitizeCallNames.Length == 0)
            throw new ArgumentException("At least one sanitize call name is required.", nameof(sanitizeCallNames));

        var sites = new List<Site>();

        foreach (var path in Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories))
        {
            var source = File.ReadAllText(path);
            var fileName = Path.GetFileName(path);

            foreach (Match match in DirectEntrySave.Matches(source))
            {
                var group = match.Groups["doc"];
                var savedDocument = group.Success ? group.Value : InlineExpression;

                // The stream/writer variable is not the document (…Open(); doc.Save(stream)).
                if (savedDocument is "stream" or "outStream" or "writer" or "ctWriter")
                    continue;

                var regionStart = Math.Max(0, match.Index - LookbackCharacters);
                var region = source[regionStart..(match.Index + match.Length)];
                var sanitizes = sanitizeCallNames.Any(name => region.Contains(name, StringComparison.Ordinal));

                sites.Add(new Site(fileName, savedDocument, sanitizes));
            }
        }

        return sites;
    }
}
