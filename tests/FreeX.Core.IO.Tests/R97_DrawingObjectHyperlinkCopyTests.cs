using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R97-model-drawing-hyperlink-2-2: R95 fixed hyperlink loss for a source-loaded object whose ANCHOR
/// gets rebuilt (a colour/fill edit) by re-reading the hyperlink from the true source package, keyed
/// by <c>cNvPr@name</c>. That mechanism only works while the object still exists, under the same
/// name, inside the SOURCE package -- a shape/text-box/picture that is duplicated (Duplicate Sheet,
/// Ctrl+C/Ctrl+V of a single object, or the plain-range-copy floating-object carry) produces a COPY
/// that is never itself present in the source package, so R95's name-keyed re-read has nothing to
/// find for it and the copy's hyperlink was silently dropped forever -- even though the ORIGINAL
/// object still had a hyperlink to give it.
/// <para>
/// The fix gives <see cref="DrawingShapeModel"/>/<see cref="TextBoxModel"/>/<see cref="PictureModel"/>
/// their own <see cref="DrawingShapeModel.Hyperlink"/>/<see cref="TextBoxModel.Hyperlink"/>/
/// <see cref="PictureModel.Hyperlink"/> field, populated on LOAD (mirroring what R95's writer-side
/// re-read already resolves, but now onto the model itself) and copied field-for-field by
/// <c>DuplicateSheetDrawingCloner</c> and <c>PastePicturesCommand</c>'s clone helpers.
/// <see cref="XlsxWorksheetDrawingObjectWriter"/> now prefers the model's own Hyperlink over the R95
/// source-package re-read, falling back to the R95 mechanism only when the model carries none --
/// keeping an ordinary (non-cloned) source-loaded object's round-trip unchanged.
/// </para>
/// <para>
/// Every test here goes through the real product entry point: <see cref="XlsxFileAdapter.Load"/> a
/// hand-crafted package (the same technique <c>R95_DrawingObjectHyperlinkPreservationTests</c> and
/// <c>XlsxLinkedPictureLoadTests</c> already use to establish an object Excel itself would produce),
/// a real <see cref="IWorkbookCommand"/>, a real <see cref="XlsxFileAdapter.Save"/>, then a reload --
/// never a hand-seeded package assertion or a bare model check. <see cref="DuplicateSheetCommand"/>
/// adds a brand-new sheet absent from the source package, which by itself forces the FULL
/// (ClosedXML-rebuild) save path -- <c>XlsxFileAdapter.SaveCoreUnlocked</c>'s cheap patched-cell-value
/// path only ever applies when the model's sheet set/fingerprint still matches the source package, so
/// the fast patch path (which never runs <see cref="XlsxWorksheetDrawingObjectWriter"/> at all) is
/// categorically unreachable once a sheet has been duplicated -- every test below exercises the FULL
/// save path.
/// </para>
/// </summary>
public sealed class R97_DrawingObjectHyperlinkCopyTests
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace SpreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
    private static readonly XNamespace DrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace ContentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
    private const string HyperlinkRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink";
    private const string ImageRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image";

    // ── Primary finding: Duplicate Sheet on a hyperlinked SHAPE. Fail-before/pass-after. ──

    [Fact]
    public void DuplicateSheet_Shape_CopyKeepsHyperlink_AndOriginalKeepsItToo()
    {
        using var package = BuildPackageWithDrawing(
            ShapeAnchor("Rectangle 1", fromCol: 1, toCol: 4, hlinkRelId: "rIdHlink1"),
            ("rIdHlink1", "https://example.com/shape-link", "External"));
        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(package);
        var sourceSheet = loaded.GetSheetAt(0);
        var sourceShape = sourceSheet.DrawingShapes.Should().ContainSingle().Subject;
        sourceShape.Hyperlink.Should().NotBeNull("the model itself must carry the hyperlink read from the source package");
        sourceShape.Hyperlink!.Target.Should().Be("https://example.com/shape-link");

        new DuplicateSheetCommand(sourceSheet.Id).Apply(new TestCommandContext(loaded)).Success.Should().BeTrue();
        var copySheet = loaded.Sheets[1];
        var copyShape = copySheet.DrawingShapes.Should().ContainSingle().Subject;
        copyShape.IsSourceLoaded.Should().BeFalse("a duplicate is never itself present in the source package");
        copyShape.Hyperlink.Should().NotBeNull("CloneDrawingShape must carry the Hyperlink field forward");

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;
        var reloaded = adapter.Load(saved);

        var reloadedOriginalHyperlink = ResolveObjectHyperlinkByName(saved, sourceSheet.Name, "Rectangle 1");
        reloadedOriginalHyperlink.Should().Be("https://example.com/shape-link",
            "the ORIGINAL shape's hyperlink must still survive a full save after duplicating its sheet");
        var reloadedCopyHyperlink = ResolveObjectHyperlinkByName(saved, copySheet.Name, "Rectangle 1");
        reloadedCopyHyperlink.Should().Be("https://example.com/shape-link",
            "the COPY shape (never present in the source package) must still carry the hyperlink -- this is the R97 fix");

        reloaded.GetSheetAt(1).DrawingShapes.Should().ContainSingle().Subject.Hyperlink!.Target
            .Should().Be("https://example.com/shape-link", "the model itself must reload with the hyperlink too");
    }

    // ── Sibling object type: TEXT BOX. ──

    [Fact]
    public void DuplicateSheet_TextBox_CopyKeepsHyperlink()
    {
        using var package = BuildPackageWithDrawing(
            TextBoxAnchor("TextBox 1", "Hello", fromCol: 1, toCol: 4, hlinkRelId: "rIdHlink1"),
            ("rIdHlink1", "https://example.com/textbox-link", "External"));
        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(package);
        var sourceSheet = loaded.GetSheetAt(0);
        loaded.GetSheetAt(0).TextBoxes.Should().ContainSingle().Subject.Hyperlink.Should().NotBeNull();

        new DuplicateSheetCommand(sourceSheet.Id).Apply(new TestCommandContext(loaded)).Success.Should().BeTrue();
        var copySheet = loaded.Sheets[1];
        copySheet.TextBoxes.Should().ContainSingle().Subject.Hyperlink.Should().NotBeNull();

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);

        ResolveObjectHyperlinkByName(saved, copySheet.Name, "TextBox 1").Should().Be("https://example.com/textbox-link",
            "the duplicated text box must keep its hyperlink after a full save");
    }

    // ── Sibling object type: PICTURE. ──

    [Fact]
    public void DuplicateSheet_Picture_CopyKeepsHyperlink()
    {
        var adapter = new XlsxFileAdapter();
        var workbook = new Workbook("PictureHyperlinkCopy");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("x"));
        using var initialSave = new MemoryStream();
        adapter.Save(workbook, initialSave);
        InjectPictureWithHyperlink(initialSave, "Picture 1", "https://example.com/picture-link", "External");

        initialSave.Position = 0;
        var loaded = adapter.Load(initialSave);
        var sourceSheet = loaded.GetSheetAt(0);
        var sourcePicture = sourceSheet.Pictures.Should().ContainSingle().Subject;
        sourcePicture.Hyperlink.Should().NotBeNull("the model must carry the hyperlink read off the picture's cNvPr");
        sourcePicture.Hyperlink!.Target.Should().Be("https://example.com/picture-link");

        new DuplicateSheetCommand(sourceSheet.Id).Apply(new TestCommandContext(loaded)).Success.Should().BeTrue();
        var copySheet = loaded.Sheets[1];
        var copyPicture = copySheet.Pictures.Should().ContainSingle().Subject;
        copyPicture.IsSourceLoaded.Should().BeFalse();
        copyPicture.Hyperlink.Should().NotBeNull("ClonePicture must carry the Hyperlink field forward");

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);

        ResolveObjectHyperlinkByName(saved, copySheet.Name, "Picture 1").Should().Be("https://example.com/picture-link",
            "the duplicated picture must keep its hyperlink after a full save");
    }

    // ── Internal ("Place in This Document") target -- no TargetMode attribute (OPC default Internal),
    // and no ScreenTip. Must round-trip through the copy identically to the external case. ──

    [Fact]
    public void DuplicateSheet_Shape_InternalPlaceInDocumentTarget_CopyKeepsHyperlink()
    {
        using var package = BuildPackageWithDrawing(
            ShapeAnchor("Rectangle 1", fromCol: 1, toCol: 4, hlinkRelId: "rIdHlink1"),
            ("rIdHlink1", "Sheet1!A1", null));
        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(package);
        var sourceSheet = loaded.GetSheetAt(0);
        var sourceHyperlink = sourceSheet.DrawingShapes.Should().ContainSingle().Subject.Hyperlink;
        sourceHyperlink.Should().NotBeNull();
        sourceHyperlink!.Target.Should().Be("Sheet1!A1");
        sourceHyperlink.TargetMode.Should().BeNull("an internal target's relationship omits TargetMode (OPC default Internal)");

        new DuplicateSheetCommand(sourceSheet.Id).Apply(new TestCommandContext(loaded)).Success.Should().BeTrue();
        var copySheet = loaded.Sheets[1];

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);

        var (target, targetMode) = ResolveObjectHyperlinkRelationshipByName(saved, copySheet.Name, "Rectangle 1");
        target.Should().Be("Sheet1!A1");
        targetMode.Should().BeNull("the copy's internal target must stay TargetMode-less, matching the original");
    }

    // ── No-regression sibling: an object with NO hyperlink must not gain one when duplicated. ──

    [Fact]
    public void DuplicateSheet_Shape_NoHyperlink_CopyDoesNotInventOne()
    {
        using var package = BuildPackageWithDrawing(
            ShapeAnchor("Rectangle 1", fromCol: 1, toCol: 4, hlinkRelId: null));
        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(package);
        var sourceSheet = loaded.GetSheetAt(0);
        sourceSheet.DrawingShapes.Should().ContainSingle().Subject.Hyperlink.Should().BeNull();

        new DuplicateSheetCommand(sourceSheet.Id).Apply(new TestCommandContext(loaded)).Success.Should().BeTrue();
        var copySheet = loaded.Sheets[1];
        copySheet.DrawingShapes.Should().ContainSingle().Subject.Hyperlink.Should().BeNull();

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);

        saved.Position = 0;
        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        FindCNvPrByName(archive, copySheet.Name, "Rectangle 1")
            .Element(DrawingNs + "hlinkClick").Should().BeNull("no hyperlink existed on the source shape; the copy must not invent one");
    }

    // ── No-regression sibling: a source-loaded object that is NOT duplicated/edited must keep
    // ── round-tripping its hyperlink via the pre-existing R95 mechanism (model.Hyperlink populated,
    // ── but writer behavior for an unmodified source-loaded object must not regress). ──

    [Fact]
    public void SourceLoadedShape_ColorEditRoundTrip_StillKeepsHyperlink_ModelPreferredPath()
    {
        using var package = BuildPackageWithDrawing(
            ShapeAnchor("Rectangle 1", fromCol: 1, toCol: 4, hlinkRelId: "rIdHlink1"),
            ("rIdHlink1", "https://example.com/still-here", "External"));
        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(package);
        var sheet = loaded.GetSheetAt(0);
        var shape = sheet.DrawingShapes.Should().ContainSingle().Subject;

        new SetDrawingShapeColorsCommand(sheet.Id, shape.Id, fillColor: new CellColor(0, 0, 0xFF), outlineColor: null)
            .Apply(new TestCommandContext(loaded))
            .Success.Should().BeTrue();
        shape.IsSourceLoaded.Should().BeFalse();
        shape.Hyperlink.Should().NotBeNull("the model's own Hyperlink field must survive the edit untouched (never cleared by a colour command)");

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);

        ResolveObjectHyperlinkByName(saved, sheet.Name, "Rectangle 1").Should().Be("https://example.com/still-here",
            "the model-preferred write path must still preserve a plain (non-cloned) edited object's hyperlink");
    }

    // ── Single-object Ctrl+C/Ctrl+V (DuplicateDrawingObjectCommand). ──

    [Fact]
    public void DuplicateDrawingObjectCommand_Shape_CopyKeepsHyperlink()
    {
        using var package = BuildPackageWithDrawing(
            ShapeAnchor("Rectangle 1", fromCol: 1, toCol: 4, hlinkRelId: "rIdHlink1"),
            ("rIdHlink1", "https://example.com/ctrlc-link", "External"));
        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(package);
        var sheet = loaded.GetSheetAt(0);
        var shape = sheet.DrawingShapes.Should().ContainSingle().Subject;

        // Add a second sheet so the destination differs from the source and the workbook diverges
        // from the source package (forcing the full save path) exactly like the Duplicate Sheet tests.
        var destinationSheet = loaded.AddSheet("Destination");

        var duplicateCommand = new DuplicateDrawingObjectCommand(
            sheet.Id, destinationSheet.Id, SelectionPaneObjectKind.Shape, shape.Id);
        duplicateCommand.Apply(new TestCommandContext(loaded)).Success.Should().BeTrue();

        var copy = destinationSheet.DrawingShapes.Should().ContainSingle().Subject;
        copy.Hyperlink.Should().NotBeNull("DuplicateDrawingObjectCommand reuses CloneDrawingShape, which now carries Hyperlink");

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);

        ResolveObjectHyperlinkByName(saved, destinationSheet.Name, "Rectangle 1").Should().Be("https://example.com/ctrlc-link",
            "a Ctrl+C/Ctrl+V duplicate of a hyperlinked shape must keep the hyperlink");
    }

    // ── Range-copy floating-object carry: PasteShapesCommand / PasteTextBoxesCommand / PastePicturesCommand. ──

    [Fact]
    public void PasteShapesCommand_RangeCopyCarry_CopyKeepsHyperlink()
    {
        using var package = BuildPackageWithDrawing(
            ShapeAnchor("Rectangle 1", fromCol: 1, toCol: 2, hlinkRelId: "rIdHlink1"),
            ("rIdHlink1", "https://example.com/range-copy-shape", "External"));
        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(package);
        var sheet = loaded.GetSheetAt(0);
        var shape = sheet.DrawingShapes.Should().ContainSingle().Subject;
        loaded.AddSheet("Other"); // forces the full save path, matching the other tests here.

        var destination = new CellAddress(sheet.Id, 20, 20);
        var pasteCommand = new PasteShapesCommand(
            sheet.Id,
            new GridRange(shape.Anchor, shape.Anchor),
            destination,
            [shape],
            transpose: false);
        pasteCommand.Apply(new TestCommandContext(loaded)).Success.Should().BeTrue();

        var pasted = sheet.DrawingShapes.Should().HaveCount(2).And.Subject
            .Single(s => s.Id != shape.Id);
        pasted.Hyperlink.Should().NotBeNull("PasteShapesCommand reuses CloneDrawingShape, which now carries Hyperlink");

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);

        // FindCNvPrByName resolves to the LAST cNvPr sharing this name -- the pasted copy, since
        // PasteShapesCommand copies DrawingShapeModel.Name verbatim (matching real Excel Copy/Paste)
        // and the writer appends the pasted object after the original in document order.
        ResolveObjectHyperlinkByName(saved, sheet.Name, "Rectangle 1").Should().Be("https://example.com/range-copy-shape",
            "a range-copy-carried shape must keep its hyperlink");
    }

    [Fact]
    public void PasteTextBoxesCommand_RangeCopyCarry_CopyKeepsHyperlink()
    {
        using var package = BuildPackageWithDrawing(
            TextBoxAnchor("TextBox 1", "Hi", fromCol: 1, toCol: 2, hlinkRelId: "rIdHlink1"),
            ("rIdHlink1", "https://example.com/range-copy-textbox", "External"));
        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(package);
        var sheet = loaded.GetSheetAt(0);
        var textBox = sheet.TextBoxes.Should().ContainSingle().Subject;
        loaded.AddSheet("Other");

        var destination = new CellAddress(sheet.Id, 20, 20);
        var pasteCommand = new PasteTextBoxesCommand(
            sheet.Id,
            new GridRange(textBox.Anchor, textBox.Anchor),
            destination,
            [textBox],
            transpose: false);
        pasteCommand.Apply(new TestCommandContext(loaded)).Success.Should().BeTrue();

        var pasted = sheet.TextBoxes.Should().HaveCount(2).And.Subject.Single(t => t.Id != textBox.Id);
        pasted.Hyperlink.Should().NotBeNull();

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);

        ResolveObjectHyperlinkByName(saved, sheet.Name, "TextBox 1").Should().Be("https://example.com/range-copy-textbox",
            "a range-copy-carried text box must keep its hyperlink");
    }

    [Fact]
    public void PastePicturesCommand_RangeCopyCarry_CopyKeepsHyperlink()
    {
        var adapter = new XlsxFileAdapter();
        var workbook = new Workbook("PastePictureHyperlink");
        var sheet0 = workbook.AddSheet("Sheet1");
        sheet0.SetCell(new CellAddress(sheet0.Id, 1, 1), new TextValue("x"));
        using var initialSave = new MemoryStream();
        adapter.Save(workbook, initialSave);
        InjectPictureWithHyperlink(initialSave, "Picture 1", "https://example.com/range-copy-picture", "External");

        initialSave.Position = 0;
        var loaded = adapter.Load(initialSave);
        var sheet = loaded.GetSheetAt(0);
        var picture = sheet.Pictures.Should().ContainSingle().Subject;
        loaded.AddSheet("Other");

        var destination = new CellAddress(sheet.Id, 20, 20);
        var pasteCommand = new PastePicturesCommand(
            sheet.Id,
            new GridRange(picture.Anchor, picture.Anchor),
            destination,
            [picture],
            transpose: false);
        pasteCommand.Apply(new TestCommandContext(loaded)).Success.Should().BeTrue();

        var pasted = sheet.Pictures.Should().HaveCount(2).And.Subject.Single(p => p.Id != picture.Id);
        pasted.Hyperlink.Should().NotBeNull("PastePicturesCommand's ClonePictureAtAnchor now carries Hyperlink forward");

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);

        ResolveObjectHyperlinkByName(saved, sheet.Name, "Picture 1").Should().Be("https://example.com/range-copy-picture",
            "a range-copy-carried picture must keep its hyperlink");
    }

    // ────────────────────────────── helpers ──────────────────────────────

    private static string? ResolveObjectHyperlinkByName(MemoryStream saved, string sheetName, string objectName) =>
        ResolveObjectHyperlinkRelationshipByName(saved, sheetName, objectName).Target;

    private static (string? Target, string? TargetMode) ResolveObjectHyperlinkRelationshipByName(
        MemoryStream saved, string sheetName, string objectName)
    {
        saved.Position = 0;
        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var cNvPr = FindCNvPrByName(archive, sheetName, objectName);
        var hlinkClick = cNvPr.Element(DrawingNs + "hlinkClick");
        if (hlinkClick is null)
            return (null, null);

        var relId = hlinkClick.Attribute(RelNs + "id")!.Value;
        var drawingPath = FindDrawingPathForSheet(archive, sheetName);
        var relsPath = XlsxPackagePath.GetRelationshipPartPath(drawingPath);
        var relsXml = XlsxPackageTestFixtures.LoadPackageXml(archive, relsPath);
        var relationship = relsXml.Root!.Elements(PackageRelNs + "Relationship")
            .First(r => r.Attribute("Id")!.Value == relId);
        return (relationship.Attribute("Target")!.Value, relationship.Attribute("TargetMode")?.Value);
    }

    /// <summary>
    /// Resolves the object's <c>cNvPr</c> by name. A pasted copy's <see cref="DrawingShapeModel.Name"/>
    /// (etc.) is copied VERBATIM from its source (Excel's real Copy/Paste on a floating object does the
    /// same -- it doesn't rename the copy), so more than one <c>cNvPr</c> can legitimately share a name
    /// once a paste test runs. The writer always appends newly-added objects (source list order, no
    /// explicit z-order set in these tests) after existing ones, so the LAST matching element in
    /// document order is always the most-recently-added (pasted/duplicated) one -- which is what every
    /// caller here actually wants to resolve.
    /// </summary>
    private static XElement FindCNvPrByName(ZipArchive archive, string sheetName, string objectName)
    {
        var drawingPath = FindDrawingPathForSheet(archive, sheetName);
        var drawingXml = XlsxPackageTestFixtures.LoadPackageXml(archive, drawingPath);
        return drawingXml.Descendants(SpreadsheetDrawingNs + "cNvPr")
            .Last(cNvPr => cNvPr.Attribute("name")?.Value == objectName);
    }

    private static string FindDrawingPathForSheet(ZipArchive archive, string sheetName)
    {
        var workbookXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/workbook.xml");
        var workbookRelsXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/_rels/workbook.xml.rels");
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var sheetElement = workbookXml.Root!.Element(workbookNs + "sheets")!.Elements(workbookNs + "sheet")
            .Single(e => e.Attribute("name")!.Value == sheetName);
        var sheetRelId = sheetElement.Attribute(RelNs + "id")!.Value;
        var worksheetTarget = workbookRelsXml.Root!.Elements(PackageRelNs + "Relationship")
            .Single(r => r.Attribute("Id")!.Value == sheetRelId).Attribute("Target")!.Value;
        var worksheetPath = XlsxPackagePath.NormalizeWorkbookTarget(worksheetTarget);

        var worksheetXml = XlsxPackageTestFixtures.LoadPackageXml(archive, worksheetPath);
        var drawingRelId = worksheetXml.Root!.Element(WorksheetNs + "drawing")!.Attribute(RelNs + "id")!.Value;
        var worksheetRelsXml = XlsxPackageTestFixtures.LoadPackageXml(
            archive, XlsxPackagePath.GetRelationshipPartPath(worksheetPath));
        var drawingTarget = worksheetRelsXml.Root!.Elements(PackageRelNs + "Relationship")
            .Single(r => r.Attribute("Id")!.Value == drawingRelId).Attribute("Target")!.Value;
        return XlsxPackagePath.ResolveRelationshipTarget(worksheetPath, drawingTarget);
    }

    private static string ShapeAnchor(string name, int fromCol, int toCol, string? hlinkRelId) => $"""
        <xdr:twoCellAnchor>
          <xdr:from><xdr:col>{fromCol}</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>1</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from>
          <xdr:to><xdr:col>{toCol}</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>8</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:to>
          <xdr:sp macro="" textlink="">
            <xdr:nvSpPr>
              <xdr:cNvPr id="{fromCol + 10}" name="{name}">{HlinkClickXml(hlinkRelId)}</xdr:cNvPr>
              <xdr:cNvSpPr/>
            </xdr:nvSpPr>
            <xdr:spPr>
              <a:xfrm><a:off x="0" y="0"/><a:ext cx="914400" cy="914400"/></a:xfrm>
              <a:prstGeom prst="rect"><a:avLst/></a:prstGeom>
              <a:solidFill><a:srgbClr val="FF0000"/></a:solidFill>
            </xdr:spPr>
          </xdr:sp>
          <xdr:clientData/>
        </xdr:twoCellAnchor>
        """;

    private static string TextBoxAnchor(string name, string text, int fromCol, int toCol, string? hlinkRelId) => $"""
        <xdr:twoCellAnchor>
          <xdr:from><xdr:col>{fromCol}</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>1</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from>
          <xdr:to><xdr:col>{toCol}</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>8</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:to>
          <xdr:sp macro="" textlink="">
            <xdr:nvSpPr>
              <xdr:cNvPr id="{fromCol + 20}" name="{name}">{HlinkClickXml(hlinkRelId)}</xdr:cNvPr>
              <xdr:cNvSpPr txBox="1"/>
            </xdr:nvSpPr>
            <xdr:spPr>
              <a:xfrm><a:off x="0" y="0"/><a:ext cx="914400" cy="914400"/></a:xfrm>
              <a:prstGeom prst="rect"><a:avLst/></a:prstGeom>
              <a:solidFill><a:srgbClr val="FF0000"/></a:solidFill>
            </xdr:spPr>
            <xdr:txBody>
              <a:bodyPr/>
              <a:lstStyle/>
              <a:p><a:r><a:t>{text}</a:t></a:r></a:p>
            </xdr:txBody>
          </xdr:sp>
          <xdr:clientData/>
        </xdr:twoCellAnchor>
        """;

    private static string HlinkClickXml(string? hlinkRelId) =>
        hlinkRelId is null ? "" : $"""<a:hlinkClick r:id="{hlinkRelId}"/>""";

    private static MemoryStream BuildPackageWithDrawing(
        string anchorsXml, params (string Id, string Target, string? TargetMode)[] hyperlinkRelationships)
    {
        var workbook = new Workbook("DrawingObjectHyperlinkCopy");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("x"));

        var adapter = new XlsxFileAdapter();
        var package = new MemoryStream();
        adapter.Save(workbook, package);

        package.Position = 0;
        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            var drawingXml = XDocument.Parse($"""
                <xdr:wsDr xmlns:xdr="{SpreadsheetDrawingNs}" xmlns:a="{DrawingNs}" xmlns:r="{RelNs}">
                {anchorsXml}
                </xdr:wsDr>
                """);
            WritePackageXml(archive, "xl/drawings/drawing1.xml", drawingXml);

            var relsXml = new XDocument(new XElement(PackageRelNs + "Relationships",
                hyperlinkRelationships.Select(rel => new XElement(
                    PackageRelNs + "Relationship",
                    new XAttribute("Id", rel.Id),
                    new XAttribute("Type", HyperlinkRelationshipType),
                    new XAttribute("Target", rel.Target),
                    rel.TargetMode is null ? null : new XAttribute("TargetMode", rel.TargetMode)))));
            WritePackageXml(archive, "xl/drawings/_rels/drawing1.xml.rels", relsXml);

            var worksheetXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
            worksheetXml.Root!.Add(new XElement(WorksheetNs + "drawing", new XAttribute(RelNs + "id", "rIdDrawing1")));
            WritePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);

            const string worksheetRelsPath = "xl/worksheets/_rels/sheet1.xml.rels";
            var worksheetRelsXml = archive.GetEntry(worksheetRelsPath) is { } existingRelsEntry
                ? XlsxPackageTestFixtures.LoadPackageXml(existingRelsEntry)
                : new XDocument(new XElement(PackageRelNs + "Relationships"));
            worksheetRelsXml.Root!.Add(new XElement(PackageRelNs + "Relationship",
                new XAttribute("Id", "rIdDrawing1"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing"),
                new XAttribute("Target", "../drawings/drawing1.xml")));
            WritePackageXml(archive, worksheetRelsPath, worksheetRelsXml);

            var contentTypesXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "[Content_Types].xml");
            contentTypesXml.Root!.Add(new XElement(ContentTypeNs + "Override",
                new XAttribute("PartName", "/xl/drawings/drawing1.xml"),
                new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.drawing+xml")));
            WritePackageXml(archive, "[Content_Types].xml", contentTypesXml);
        }

        package.Position = 0;
        return package;
    }

    /// <summary>
    /// Adds an EMBEDDED picture (r:embed + a media part -- the normal case, not "Link to File")
    /// carrying an <c>a:hlinkClick</c> to <paramref name="packageStream"/>'s drawing part, CREATING
    /// that drawing part (and its worksheet relationship + content-type override) from scratch when
    /// the workbook had no drawing objects at all yet (the common case here: these tests start from a
    /// plain cell-only workbook so a picture can be the sheet's only drawing object). When a drawing
    /// part already exists, appends to it instead -- mirrors
    /// <c>XlsxLinkedPictureLoadTests.InjectLinkedPicture</c>'s technique for the append case.
    /// </summary>
    private static void InjectPictureWithHyperlink(MemoryStream packageStream, string pictureName, string hyperlinkTarget, string? hyperlinkTargetMode)
    {
        packageStream.Position = 0;
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true);

        var drawingEntry = archive.Entries.FirstOrDefault(entry =>
            entry.FullName.StartsWith("xl/drawings/drawing", StringComparison.OrdinalIgnoreCase) &&
            entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
            !entry.FullName.Contains("/_rels/", StringComparison.OrdinalIgnoreCase));

        string drawingPath;
        XDocument drawingXml;
        XDocument relsXml;
        if (drawingEntry is not null)
        {
            drawingPath = drawingEntry.FullName;
            var existingRelsPath = XlsxPackagePath.GetRelationshipPartPath(drawingPath);
            drawingXml = XlsxPackageXmlEditor.LoadXml(drawingEntry);
            relsXml = archive.GetEntry(existingRelsPath) is { } relsEntry
                ? XlsxPackageXmlEditor.LoadXml(relsEntry)
                : new XDocument(new XElement(PackageRelNs + "Relationships"));
        }
        else
        {
            // No drawing part yet -- create one and wire it to the (single) worksheet, mirroring
            // BuildPackageWithDrawing's setup for the shape/text-box tests above.
            drawingPath = "xl/drawings/drawing1.xml";
            drawingXml = new XDocument(new XElement(SpreadsheetDrawingNs + "wsDr",
                new XAttribute(XNamespace.Xmlns + "xdr", SpreadsheetDrawingNs),
                new XAttribute(XNamespace.Xmlns + "a", DrawingNs),
                new XAttribute(XNamespace.Xmlns + "r", RelNs)));
            relsXml = new XDocument(new XElement(PackageRelNs + "Relationships"));

            var worksheetEntry = archive.Entries.Single(entry =>
                entry.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase) &&
                entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
                !entry.FullName.Contains("/_rels/", StringComparison.OrdinalIgnoreCase));
            var worksheetPath = worksheetEntry.FullName;
            var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
            worksheetXml.Root!.Add(new XElement(WorksheetNs + "drawing", new XAttribute(RelNs + "id", "rIdDrawing1")));
            XlsxPackageXmlEditor.ReplaceXml(archive, worksheetPath, worksheetXml);

            var worksheetRelsPath = XlsxPackagePath.GetRelationshipPartPath(worksheetPath);
            var worksheetRelsXml = archive.GetEntry(worksheetRelsPath) is { } existingWorksheetRelsEntry
                ? XlsxPackageXmlEditor.LoadXml(existingWorksheetRelsEntry)
                : new XDocument(new XElement(PackageRelNs + "Relationships"));
            worksheetRelsXml.Root!.Add(new XElement(PackageRelNs + "Relationship",
                new XAttribute("Id", "rIdDrawing1"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing"),
                new XAttribute("Target", XlsxPackagePath.GetRelationshipTarget(worksheetPath, drawingPath))));
            XlsxPackageXmlEditor.ReplaceXml(archive, worksheetRelsPath, worksheetRelsXml);

            var contentTypesXml = XlsxPackageXmlEditor.LoadXml(archive.GetEntry("[Content_Types].xml")!);
            contentTypesXml.Root!.Add(new XElement(ContentTypeNs + "Override",
                new XAttribute("PartName", "/" + drawingPath),
                new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.drawing+xml")));
            XlsxPackageXmlEditor.ReplaceXml(archive, "[Content_Types].xml", contentTypesXml);
        }

        var drawingRelsPath = XlsxPackagePath.GetRelationshipPartPath(drawingPath);
        const string mediaPath = "xl/media/r97TestPicture.png";
        archive.GetEntry(mediaPath)?.Delete();
        var mediaEntry = archive.CreateEntry(mediaPath);
        using (var mediaStream = mediaEntry.Open())
            mediaStream.Write(MinimalPngBytes());

        const string imageRelId = "rIdR97TestPicture";
        relsXml.Root!.Add(new XElement(
            PackageRelNs + "Relationship",
            new XAttribute("Id", imageRelId),
            new XAttribute("Type", ImageRelationshipType),
            new XAttribute("Target", "../media/r97TestPicture.png")));

        const string hyperlinkRelId = "rIdR97TestPictureHyperlink";
        relsXml.Root!.Add(new XElement(
            PackageRelNs + "Relationship",
            new XAttribute("Id", hyperlinkRelId),
            new XAttribute("Type", HyperlinkRelationshipType),
            new XAttribute("Target", hyperlinkTarget),
            hyperlinkTargetMode is null ? null : new XAttribute("TargetMode", hyperlinkTargetMode)));

        drawingXml.Root!.Add(new XElement(SpreadsheetDrawingNs + "oneCellAnchor",
            new XElement(SpreadsheetDrawingNs + "from",
                new XElement(SpreadsheetDrawingNs + "col", "6"),
                new XElement(SpreadsheetDrawingNs + "colOff", "0"),
                new XElement(SpreadsheetDrawingNs + "row", "6"),
                new XElement(SpreadsheetDrawingNs + "rowOff", "0")),
            new XElement(SpreadsheetDrawingNs + "ext",
                new XAttribute("cx", "914400"),
                new XAttribute("cy", "914400")),
            new XElement(SpreadsheetDrawingNs + "pic",
                new XElement(SpreadsheetDrawingNs + "nvPicPr",
                    new XElement(SpreadsheetDrawingNs + "cNvPr",
                        new XAttribute("id", "199"),
                        new XAttribute("name", pictureName),
                        new XElement(DrawingNs + "hlinkClick", new XAttribute(RelNs + "id", hyperlinkRelId))),
                    new XElement(SpreadsheetDrawingNs + "cNvPicPr")),
                new XElement(SpreadsheetDrawingNs + "blipFill",
                    new XElement(DrawingNs + "blip", new XAttribute(RelNs + "embed", imageRelId)),
                    new XElement(DrawingNs + "stretch", new XElement(DrawingNs + "fillRect"))),
                new XElement(SpreadsheetDrawingNs + "spPr",
                    new XElement(DrawingNs + "prstGeom",
                        new XAttribute("prst", "rect"),
                        new XElement(DrawingNs + "avLst")))),
            new XElement(SpreadsheetDrawingNs + "clientData")));

        XlsxPackageXmlEditor.ReplaceXml(archive, drawingPath, drawingXml);
        XlsxPackageXmlEditor.ReplaceXml(archive, drawingRelsPath, relsXml);
    }

    private static void WritePackageXml(ZipArchive archive, string entryName, XDocument document)
    {
        archive.GetEntry(entryName)?.Delete();
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var stream = entry.Open();
        document.Save(stream, SaveOptions.DisableFormatting);
    }

    private static byte[] MinimalPngBytes() =>
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41,
        0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
        0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82
    ];
}
