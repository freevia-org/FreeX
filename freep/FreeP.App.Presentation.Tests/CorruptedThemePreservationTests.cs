using System;
using System.IO;
using System.IO.Compression;
using FreeP.Core.IO;
using FreeP.Core.Model;
using FluentAssertions;
using Xunit;

using PresentationModel = FreeP.Core.Model.Presentation;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// default-masks-missing F2: a .pptx whose ppt/theme/theme1.xml part is present but fails to
/// parse (corrupted/truncated XML) used to have its theme silently replaced, in memory, by
/// <see cref="PresentationTheme.CreateDefault"/> — indistinguishable from a package that never
/// had a theme part at all (see PptxPackageReader.ReadTheme). Because
/// PptxPackageWriter's per-master theme loop wrote that in-memory default unconditionally on
/// every save, round-tripping a deck with a corrupted-but-otherwise-intact theme part
/// permanently destroyed the original theme bytes — with no error or warning shown.
///
/// The fix: PptxPackageReader now records the resolved theme part's own zip path on
/// <see cref="SlideMaster.ThemePartPath"/> whenever a path is found, even if parsing that part
/// failed. PptxPackageWriter consults that path (together with the read-time package snapshot,
/// which captures every zip entry's raw bytes regardless of whether it parsed) to re-emit the
/// original bytes verbatim instead of synthesizing a generic default theme.
/// </summary>
public sealed class CorruptedThemePreservationTests
{
    private static byte[] WriteToBytes(PresentationModel pres)
    {
        using var ms = new MemoryStream();
        PptxPackageWriter.Write(pres, ms);
        return ms.ToArray();
    }

    private static byte[] ReadZipEntryBytes(byte[] pptxBytes, string entryPath)
    {
        using var ms = new MemoryStream(pptxBytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        var entry = zip.GetEntry(entryPath);
        entry.Should().NotBeNull($"the pptx package must contain {entryPath}");
        using var s = entry!.Open();
        using var outMs = new MemoryStream();
        s.CopyTo(outMs);
        return outMs.ToArray();
    }

    /// <summary>
    /// Replaces a single zip entry's bytes in-place, producing a new archive with everything
    /// else unchanged. Used to simulate a truncated/corrupted theme part landing in an
    /// otherwise-healthy package (disk error, partial write, etc.).
    /// </summary>
    private static byte[] ReplaceZipEntryBytes(byte[] pptxBytes, string entryPath, byte[] replacement)
    {
        using var ms = new MemoryStream();
        ms.Write(pptxBytes, 0, pptxBytes.Length);
        ms.Position = 0;
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Update, leaveOpen: true))
        {
            var existing = zip.GetEntry(entryPath);
            existing.Should().NotBeNull($"expected {entryPath} to exist before corrupting it");
            existing!.Delete();

            var entry = zip.CreateEntry(entryPath, CompressionLevel.NoCompression);
            using var entryStream = entry.Open();
            entryStream.Write(replacement, 0, replacement.Length);
        }

        return ms.ToArray();
    }

    [Fact]
    public void CorruptedTheme1_RoundTrip_PreservesOriginalBytesInsteadOfSynthesizingDefault()
    {
        // Arrange: a normal, valid presentation with exactly one master, so it writes exactly
        // one ppt/theme/theme1.xml.
        var original = PresentationModel.CreateEmpty();
        var validBytes = WriteToBytes(original);

        // Truncate/corrupt theme1.xml the way a partial disk write or interrupted save would —
        // well-formed enough to exist as a zip entry, but not parseable XML.
        var corruptedThemeXml = System.Text.Encoding.UTF8.GetBytes(
            "<a:theme xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" name=\"Custom Brand Theme\"><a:themeElements><a:clrScheme"); // deliberately unclosed/truncated
        var corruptedPackageBytes = ReplaceZipEntryBytes(validBytes, "ppt/theme/theme1.xml", corruptedThemeXml);

        // Act 1: read the corrupted package. Reading must succeed (no exception) with the
        // master's Theme falling back to null, but the original part's path must still be
        // recorded so the writer can find it again.
        var reopened = PptxPackageReader.Read(new MemoryStream(corruptedPackageBytes));

        reopened.Masters.Should().HaveCount(1);
        reopened.Masters[0].Theme.Should().BeNull(
            "the corrupted theme1.xml cannot be parsed into a PresentationTheme");
        reopened.Masters[0].ThemePartPath.Should().Be("ppt/theme/theme1.xml",
            "the reader must still know which zip entry the (unparseable) theme part came from");

        // Act 2: save it straight back out, unmodified.
        var resavedBytes = WriteToBytes(reopened);
        var resavedThemeBytes = ReadZipEntryBytes(resavedBytes, "ppt/theme/theme1.xml");

        // Assert: the original corrupted-but-intact bytes survive the round trip byte-for-byte,
        // instead of being replaced by a freshly synthesized generic Office theme.
        resavedThemeBytes.Should().Equal(corruptedThemeXml,
            "an unparseable-but-present theme part must be preserved verbatim on save, " +
            "not silently overwritten with a synthesized default theme");
    }

    /// <summary>
    /// Sibling no-regression case: a brand-new in-memory presentation that never went through
    /// the reader (so it has no package snapshot and no ThemePartPath at all) must still get a
    /// freshly synthesized theme1.xml written out — the preservation path must not accidentally
    /// swallow the legitimate "there was never a theme part to preserve" case.
    /// </summary>
    [Fact]
    public void FreshInMemoryPresentation_NoSnapshot_StillSynthesizesDefaultTheme()
    {
        var pres = PresentationModel.CreateEmpty();

        pres.Masters[0].Theme.Should().BeNull("CreateEmpty() does not populate a per-master theme");
        pres.Masters[0].ThemePartPath.Should().BeNull("a presentation built in memory was never read from a package");
        pres.PackageSnapshot.Should().BeNull("a presentation built in memory has no source package snapshot");

        var bytes = WriteToBytes(pres);
        var themeBytes = ReadZipEntryBytes(bytes, "ppt/theme/theme1.xml");

        // Must be valid, parseable theme XML (the synthesized default), not empty/corrupt.
        using var themeStream = new MemoryStream(themeBytes);
        var doc = System.Xml.Linq.XDocument.Load(themeStream);
        doc.Root.Should().NotBeNull();
        doc.Root!.Name.LocalName.Should().Be("theme");
    }
}
