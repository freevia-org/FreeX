using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Structural tripwire for the ONE way the XML-1.0-illegal-character guard can still be bypassed in
/// FreeX's XLSX writer.
/// <para>
/// Almost every OOXML part FreeX writes goes through <c>OpcXml.WriteXmlEntry</c>/<c>ReplaceXmlEntry</c>
/// (via <c>XlsxPackageXmlEditor</c>), which sanitizes the document on the way out. A writer that instead
/// calls <c>archive.CreateEntry(...)</c> and serializes an <c>XDocument</c> straight into the returned
/// stream skips that boundary entirely, and one C0 control code or lone surrogate in the model text it
/// serializes aborts the WHOLE workbook save with an <c>ArgumentException</c> and no file written.
/// </para>
/// <para>
/// This has already happened twice. <c>XlsxWorksheetChartWriter</c> was the originally-reported site;
/// <c>XlsxWorkbookThemeWriter</c> was found by the audit that added this test, and really did abort a
/// save on a control character in the workbook theme name. Both are fixed, and both are now pinned here
/// so a third bypass fails at test time instead of waiting for the next audit round.
/// </para>
/// <para>
/// <b>Scope, stated plainly so the next reviewer does not over-trust this file.</b> This is a regex scan
/// of the FreeX.Core.IO sources, and it is a backstop, not a proof. It does NOT cover: writers outside
/// this project; a site that hands the stream to a helper which saves it elsewhere (the <c>.Save(</c>
/// lands in another method, so the scan never sees it); a document serialized via <c>XmlWriter</c> rather
/// than <c>XDocument.Save</c>; or XML built by string concatenation and written as raw bytes. The
/// durable guarantee is the sanitize inside the shared writers, not this scan -- this only catches the
/// specific shape that bit us twice.
/// </para>
/// </summary>
public sealed class DirectPackageEntryXmlSanitizationInvariantTests
{
    /// <summary>
    /// Sites that serialize a document they just PARSED OUT OF the archive, mutate structurally (an
    /// attribute rename, an added relationship element), and write back. XML that parsed cannot contain
    /// an XML-1.0-illegal character, and none of these introduce model text, so they are safe without a
    /// sanitize. Allowlisted by file + the identifier they save, so that adding model text to one of
    /// them changes the line and forces a fresh look rather than riding on a stale exemption.
    /// </summary>
    private static readonly (string File, string SavedDocument)[] ReserializesParsedXml =
    [
        // Rewrites <color rgb="00000000"> to <color auto="1"/> in the shared strings ClosedXML wrote.
        ("XlsxFileAdapter.SavePostProcessing.cs", "doc"),
        // Adds one <Relationship> element (generated id/type/target) to a parsed .rels part.
        ("XlsxWorksheetBackgroundReaderWriter.cs", "relsXml"),
    ];

    // "var x = archive.CreateEntry(...)" ... then within a few lines "someDoc.Save(stream)".
    // Captures the identifier being saved so it can be matched against the allowlist.
    private static readonly Regex DirectEntrySave = new(
        @"CreateEntry\([^;]*;(?<between>(?:[^;]*;){0,3}?)[^;]*?\b(?<doc>[A-Za-z_][A-Za-z0-9_]*)\.Save\(",
        RegexOptions.Compiled | RegexOptions.Singleline);

    [Fact]
    public void EveryDirectPackageEntryXmlWrite_SanitizesOrIsExplicitlyExempt()
    {
        var ioRoot = Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx"),
            "src",
            "FreeX.Core.IO");

        var offenders = new List<string>();
        var matchedFiles = new List<string>();

        foreach (var path in Directory.EnumerateFiles(ioRoot, "*.cs", SearchOption.AllDirectories))
        {
            var source = File.ReadAllText(path);
            var fileName = Path.GetFileName(path);

            foreach (Match match in DirectEntrySave.Matches(source))
            {
                var savedDocument = match.Groups["doc"].Value;

                // The stream variable itself is not the document (…Open(); doc.Save(stream)).
                if (savedDocument is "stream" or "outStream")
                    continue;

                matchedFiles.Add(fileName);

                if (ReserializesParsedXml.Contains((fileName, savedDocument)))
                    continue;

                // The sanitize must appear before the save, in the same region the scan matched or
                // just above it -- i.e. somewhere in the method that built the document.
                var regionStart = Math.Max(0, match.Index - 1500);
                var region = source[regionStart..(match.Index + match.Length)];
                if (!region.Contains("SanitizeInPlace", StringComparison.Ordinal))
                    offenders.Add($"{fileName}: '{savedDocument}.Save(...)' into a directly-created package entry, with no XmlTextSanitizer.SanitizeInPlace before it");
            }
        }

        // Guards the scan itself. If a refactor changes the call shape so the regex stops matching, this
        // test would otherwise pass vacuously while covering nothing -- so the two writers it exists for
        // must be found BY NAME every run, not merely counted.
        matchedFiles.Should().Contain(
            "XlsxWorksheetChartWriter.cs",
            "this scan exists because the chart writer bypasses OpcXml; if it no longer matches, the regex has gone stale rather than the bypass having gone away");
        matchedFiles.Should().Contain(
            "XlsxWorkbookThemeWriter.cs",
            "the theme writer is the second known bypass and really did abort saves; losing it from the scan means the same silent regression");

        // Asserted as joined text rather than on the list itself: FluentAssertions' BeEmpty reports only
        // the FIRST item ("found at least one item"), which hid a second offender while this guard was
        // being validated. A guard that names one of three bypasses is a guard you have to run three times.
        string.Join(Environment.NewLine, offenders).Should().BeEmpty(
            "a package part written straight to a zip entry skips OpcXml's sanitize, so one control character or lone surrogate in its model text aborts the whole workbook save");
    }
}
