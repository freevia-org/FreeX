using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeP.Core.IO;
using Xunit;
using PresentationModel = FreeP.Core.Model.Presentation;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// round171 F2: <see cref="PptxPackageReader.ReadArchive"/> used to return a bare, untouched
/// <c>new Presentation()</c> -- with zero exception and zero warning -- when the package's root
/// relationships had no officeDocument relationship, or when that relationship resolved to a
/// target part missing from the archive entirely (a realistic outcome of a partial/interrupted
/// download or a zip-repair tool that dropped one entry).
///
/// Reached in production via <c>PresentationFilePersistenceWorkflow.Open</c> -&gt;
/// <c>PptxPackageReader.Read(path)</c>, called from
/// <c>PresentationFileCommandSession.OpenPathCoreAsync</c> (freep/FreeP.App.Presentation/
/// PresentationFileCommandSession.cs), which -- on the old silent-empty return -- treated the
/// call as an unconditional success: it loaded the (empty) presentation, marked the document as
/// cleanly saved AT THE ORIGINAL PATH via <c>SetSaved</c>, and reported "Opened &lt;filename&gt;".
/// The very next Save (or an autosave) would then overwrite the user's real, only-partially-
/// corrupted .pptx with an essentially blank presentation.
///
/// The fix mirrors FreeW's <c>DocxReader.Read</c>, which throws
/// <see cref="InvalidDataException"/>("Not a Word document: word/document.xml is missing.") for
/// the equivalent missing word/document.xml case instead of returning an empty
/// <c>TextDocument</c> -- FreeP had no such guard at all. Once <see cref="PptxPackageReader.Read"/>
/// throws, <c>OpenPathCoreAsync</c>'s existing <c>catch (Exception ex)</c> reports a
/// <c>PresentationFileCommandResult.Failure</c> instead: <c>_loadPresentation</c> and
/// <c>SetSaved</c> are never reached, so the document is never marked clean and the original file
/// is never at risk from a subsequent Save.
/// </summary>
public sealed class PptxPackageReaderMissingRequiredPartTests
{
    private static readonly XNamespace PkgRel =
        "http://schemas.openxmlformats.org/package/2006/relationships";

    /// <summary>
    /// Builds a genuine, well-formed .pptx via the real writer, then removes the given zip entry
    /// entirely -- simulating a partial download / zip-repair tool that dropped one part -- while
    /// leaving every other entry (including [Content_Types].xml and _rels/.rels) intact.
    /// </summary>
    private static MemoryStream BuildPptxWithEntryRemoved(string entryPath)
    {
        var presentation = PresentationModel.CreateEmpty();
        var buffer = new MemoryStream();
        PptxPackageWriter.Write(presentation, buffer);
        buffer.Position = 0;

        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = archive.GetEntry(entryPath);
            entry.Should().NotBeNull($"a normal presentation package must contain {entryPath}");
            entry!.Delete();
        }

        buffer.Position = 0;
        return buffer;
    }

    /// <summary>
    /// Builds a genuine .pptx via the real writer, then replaces _rels/.rels with a root
    /// relationships part that has no officeDocument relationship at all (only an unrelated
    /// relationship type survives) -- e.g. a corrupted/hand-edited root rels part.
    /// </summary>
    private static MemoryStream BuildPptxWithNoOfficeDocumentRelationship()
    {
        var presentation = PresentationModel.CreateEmpty();
        var buffer = new MemoryStream();
        PptxPackageWriter.Write(presentation, buffer);
        buffer.Position = 0;

        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = archive.GetEntry("_rels/.rels");
            entry.Should().NotBeNull();
            entry!.Delete();

            var replacement = new XDocument(
                new XElement(PkgRel + "Relationships",
                    new XElement(PkgRel + "Relationship",
                        new XAttribute("Id", "rIdUnrelated"),
                        new XAttribute("Type", "http://schemas.openxmlformats.org/package/2006/relationships/metadata/thumbnail"),
                        new XAttribute("Target", "docProps/thumbnail.jpeg"))));

            var newEntry = archive.CreateEntry("_rels/.rels");
            using var writeStream = newEntry.Open();
            replacement.Save(writeStream);
        }

        buffer.Position = 0;
        return buffer;
    }

    [Fact]
    public void Read_PresentationXmlPartMissingFromArchive_ThrowsInsteadOfReturningEmptyPresentation()
    {
        using var pptx = BuildPptxWithEntryRemoved("ppt/presentation.xml");

        Action act = () => PptxPackageReader.Read(pptx);

        act.Should().Throw<InvalidDataException>(
            "a package whose officeDocument relationship points at a part that is missing from the " +
            "archive must be reported as a failed open, not silently returned as an empty presentation " +
            "that a later Save could overwrite the real file with");
    }

    [Fact]
    public void Read_NoOfficeDocumentRelationshipInRootRels_ThrowsInsteadOfReturningEmptyPresentation()
    {
        using var pptx = BuildPptxWithNoOfficeDocumentRelationship();

        Action act = () => PptxPackageReader.Read(pptx);

        act.Should().Throw<InvalidDataException>(
            "a package whose root relationships have no officeDocument relationship at all must be " +
            "reported as a failed open, not silently returned as an empty presentation");
    }

    /// <summary>
    /// Sibling no-regression check: an ordinary, genuine .pptx package -- including one produced by
    /// FreeP's own writer round-trip -- must still open normally and non-empty after the guard was
    /// added.
    /// </summary>
    [Fact]
    public void Read_GenuinePptxPackage_StillOpensNormally_AfterMissingPartGuardAdded()
    {
        var presentation = PresentationModel.CreateEmpty();
        using var stream = new MemoryStream();
        PptxPackageWriter.Write(presentation, stream);
        stream.Position = 0;

        var reloaded = PptxPackageReader.Read(stream);

        reloaded.Slides.Should().NotBeEmpty(
            "a genuine package must still load its real slide content after the missing-part guard was added");
    }

    /// <summary>
    /// Sibling no-regression check: a package whose ppt/presentation.xml part exists but fails the
    /// hardened, DTD-prohibiting XML load (see PptxPackageReaderSourceTests.
    /// Read_PresentationXmlWithDtd_DoesNotApplyParsedPayload) is a DIFFERENT case from the part
    /// being physically missing -- it is a deliberately-tested "quarantine the rejected payload,
    /// don't crash" security contract, and must keep degrading to an empty presentation rather than
    /// throw. This guards against widening the new missing-part check into that XML-parse-failure
    /// case as well.
    /// </summary>
    [Fact]
    public void Read_PresentationXmlPartPresentButFailsHardenedXmlLoad_StillDegradesToEmpty_NotThrow()
    {
        var presentation = PresentationModel.CreateEmpty();
        var buffer = new MemoryStream();
        PptxPackageWriter.Write(presentation, buffer);
        buffer.Position = 0;

        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = archive.GetEntry("ppt/presentation.xml")!;
            entry.Delete();

            var newEntry = archive.CreateEntry("ppt/presentation.xml");
            using var writer = new StreamWriter(newEntry.Open());
            // A DOCTYPE trips the shared hardened loader's DtdProcessing.Prohibit, so
            // OpcXml.TryLoadXml catches the resulting XmlException and returns null -- the part IS
            // present in the archive, it just cannot be safely parsed.
            writer.Write("""
                <!DOCTYPE p:presentation [ <!ENTITY x "blocked"> ]>
                <p:presentation xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main">
                  <p:sldIdLst>&x;</p:sldIdLst>
                </p:presentation>
                """);
        }

        buffer.Position = 0;

        Action act = () => PptxPackageReader.Read(buffer);

        act.Should().NotThrow(
            "a part that is present but rejected by the hardened XML loader must keep degrading to an " +
            "empty presentation, matching PptxPackageReaderSourceTests' existing DTD-quarantine contract");
    }
}
