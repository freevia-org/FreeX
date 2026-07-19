using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R49-io-comment-legacy-vml-3-1: on a FRESH save (workbook has no source package, so the save
/// routes through <see cref="XlsxLegacyCommentVisibilityNormalizer"/> rather than
/// <see cref="XlsxLegacyCommentPreserver"/>), pinning a note via <see cref="Sheet.ShownComments"/>
/// must flip BOTH the VML ClientData &lt;x:Visible/&gt; flag AND the shape's CSS
/// <c>visibility</c> style property -- real Excel paints the note box according to the CSS
/// property, so leaving it at ClosedXML's default "hidden" makes a pinned note appear unpinned
/// when the saved file is opened in real Excel, even though FreeX's own reader (which only checks
/// ClientData) believes the pin round-tripped correctly.
/// </summary>
public sealed class R49_LegacyCommentFreshSaveVisibilityStyleTests
{
    private static readonly XNamespace VmlNs = "urn:schemas-microsoft-com:vml";
    private static readonly XNamespace ExcelVmlNs = "urn:schemas-microsoft-com:office:excel";
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

    [Fact]
    public void FreshSave_PinnedNote_VmlShapeStyleIsVisible()
    {
        // Arrange: a brand-new workbook (no source package at all) with a note pinned open via
        // ShownComments -- exactly the ShowHideCommentCommand "Show Comment" flow.
        var workbook = new Workbook("FreshPinTest");
        var sheet = workbook.AddSheet("S1");
        var address = new CellAddress(sheet.Id, 2, 3); // C2
        sheet.Comments[address] = "Always shown";
        sheet.ShownComments.Add(address);

        // Act: save (no source package -> routes through the fresh-save VML normalizer).
        var adapter = new XlsxFileAdapter();
        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        // Assert: the shape's CSS visibility must say "visible", not just the ClientData flag --
        // real Excel paints the box from the CSS property.
        using var archive = new ZipArchive(saved, ZipArchiveMode.Read);
        var shape = FindSoleNoteShape(archive);
        shape.Should().NotBeNull("the saved package must reference a VML drawing with the note shape");

        HasVisibleClientDataFlag(shape!).Should().BeTrue(
            "the ClientData <x:Visible/> flag must be set for a pinned note");

        var style = shape!.Attribute("style")?.Value ?? "";
        style.Should().Contain("visibility:visible",
            "real Excel paints the note box from the shape's CSS visibility property, so a " +
            "pinned note's style must say visible, not just its ClientData flag " +
            "(R49-io-comment-legacy-vml-3-1)");
        style.Should().NotContain("visibility:hidden");
    }

    [Fact]
    public void FreshSave_UnpinnedNote_VmlShapeStyleStaysHidden_NoRegression()
    {
        // Sibling no-regression case: an ordinary (never pinned) note must keep the default
        // "hidden" CSS visibility -- the fix must not flip every note to visible regardless of
        // pin state.
        var workbook = new Workbook("FreshUnpinnedTest");
        var sheet = workbook.AddSheet("S1");
        var address = new CellAddress(sheet.Id, 2, 3); // C2
        sheet.Comments[address] = "Not pinned";
        // Deliberately NOT added to ShownComments.

        var adapter = new XlsxFileAdapter();
        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read);
        var shape = FindSoleNoteShape(archive);
        shape.Should().NotBeNull();

        HasVisibleClientDataFlag(shape!).Should().BeFalse(
            "an unpinned note must not carry the ClientData <x:Visible/> flag");

        var style = shape!.Attribute("style")?.Value ?? "";
        style.Should().Contain("visibility:hidden",
            "an unpinned note's shape must remain CSS-hidden (no regression)");
        style.Should().NotContain("visibility:visible");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static bool HasVisibleClientDataFlag(XElement shape) =>
        shape.Elements(ExcelVmlNs + "ClientData")
            .Any(cd => cd.Element(ExcelVmlNs + "Visible") is not null);

    /// <summary>
    /// Finds the sole VML note shape referenced by the first worksheet in the saved package,
    /// resolved via the worksheet's own &lt;legacyDrawing&gt; relationship (not just "any VML file
    /// in the archive").
    /// </summary>
    private static XElement? FindSoleNoteShape(ZipArchive archive)
    {
        var worksheetEntry = archive.Entries.FirstOrDefault(e =>
            e.FullName.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
            e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
        if (worksheetEntry is null)
            return null;

        XDocument worksheetXml;
        using (var stream = worksheetEntry.Open())
            worksheetXml = XDocument.Load(stream);

        var vmlRelId = worksheetXml.Root?.Element(WorksheetNs + "legacyDrawing")?.Attribute(RelNs + "id")?.Value;
        if (string.IsNullOrWhiteSpace(vmlRelId))
            return null;

        var relsPath = "xl/worksheets/_rels/" + Path.GetFileName(worksheetEntry.FullName) + ".rels";
        var relsEntry = archive.GetEntry(relsPath);
        if (relsEntry is null)
            return null;

        XDocument relsXml;
        using (var stream = relsEntry.Open())
            relsXml = XDocument.Load(stream);

        const string vmlRelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/vmlDrawing";
        var vmlTarget = relsXml.Root?.Elements(PackageRelNs + "Relationship")
            .Where(r => string.Equals(r.Attribute("Id")?.Value, vmlRelId, StringComparison.Ordinal) &&
                        string.Equals(r.Attribute("Type")?.Value, vmlRelType, StringComparison.OrdinalIgnoreCase))
            .Select(r => r.Attribute("Target")?.Value)
            .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));
        if (string.IsNullOrWhiteSpace(vmlTarget))
            return null;

        var vmlPath = vmlTarget!.StartsWith("..", StringComparison.Ordinal)
            ? "xl/drawings/" + Path.GetFileName(vmlTarget)
            : vmlTarget.TrimStart('/');

        var vmlEntry = archive.Entries.FirstOrDefault(e =>
            string.Equals(e.FullName, vmlPath, StringComparison.OrdinalIgnoreCase));
        if (vmlEntry is null)
            return null;

        XDocument vmlXml;
        using (var stream = vmlEntry.Open())
            vmlXml = XDocument.Load(stream);

        return vmlXml.Root?.Elements(VmlNs + "shape")
            .FirstOrDefault(shape => shape.Elements(ExcelVmlNs + "ClientData")
                .Any(cd => string.Equals(cd.Attribute("ObjectType")?.Value, "Note", StringComparison.OrdinalIgnoreCase)));
    }
}
