using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using FreeW.Core.Model;
using static FreeW.Core.IO.Ooxml;

namespace FreeW.Core.IO;

/// <summary>
/// Writes a <see cref="TextDocument"/> as a minimal-but-valid WordprocessingML (.docx) package:
/// [Content_Types].xml, package + document relationships, word/document.xml and word/styles.xml.
/// Round-trips with <see cref="DocxReader"/> over the supported formatting subset.
/// </summary>
public static class DocxWriter
{
    private const string OfficeDocumentRel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument";
    private const string StylesRel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles";
    private const string ImageRel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image";
    private const string HyperlinkRel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink";
    private const string HeaderRel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/header";
    private const string FooterRel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/footer";

    private const string HeaderContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml";
    private const string FooterContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.footer+xml";

    private const string HeaderRelationshipId = "rIdHeader1";
    private const string FooterRelationshipId = "rIdFooter1";
    private const string FootnotesRelationshipId = "rIdFootnotes";
    private const string EndnotesRelationshipId = "rIdEndnotes";
    private const string CommentsRelationshipId = "rIdComments";
    private const string SettingsRelationshipId = "rIdSettings";
    private const string HeaderPartName = "word/header1.xml";
    private const string FooterPartName = "word/footer1.xml";

    // Minimal numbering scheme: one abstract num per list kind, mapped 1:1 to a w:num. Bullets use
    // abstractNumId 0 / numId 1; decimal numbering uses abstractNumId 1 / numId 2; multilevel (legal
    // outline) numbering uses abstractNumId 2 / numId 3. Each abstract num defines 9 levels (ilvl 0..8)
    // so ListLevel maps directly to w:ilvl.
    internal const int BulletNumId = 1;
    internal const int NumberNumId = 2;
    internal const int MultiLevelNumId = 3;
    private const int ListLevelCount = 9;

    public static void Write(TextDocument document, string path)
    {
        using var stream = File.Create(path);
        Write(document, stream);
    }

    public static void Write(TextDocument document, Stream stream)
    {
        // Assign a relationship + media id to every inline image up front so document.xml, the
        // document relationships and the media parts all agree on rId/imageN.png.
        var images = CollectImages(document);
        // Assign a relationship + part name to every inline chart the same way (charts are a separate XML
        // part referenced from the run drawing by r:id, mirroring how images add a media part + r:embed).
        var charts = CollectCharts(document);
        // Assign a relationship + binary part name to every inline embedded OLE object the same way. Each
        // object's presentation icon is collected as an extra ImagePart appended to `images`, so the icon
        // media part + relationship + png content-type flow through the existing image plumbing untouched.
        var embeddedObjects = CollectEmbeddedObjects(document, images);
        // Assign four relationship ids + four part names to every inline SmartArt diagram the same way
        // (a diagram is four separate XML parts referenced from the run drawing by dgm:relIds).
        var smartArts = CollectSmartArts(document);
        // Assign an external relationship id to every distinct hyperlink target the same way.
        var hyperlinks = CollectHyperlinks(document);
        // Emit a numbering part only when at least one paragraph is decorated as a list.
        var hasLists = EnumerateParagraphs(document).Any(p => p.Formatting.ListKind != ListKind.None);

        // A header/footer is only emitted as a part when it carries visible content.
        var hasHeader = document.Header is { IsEmpty: false };
        var hasFooter = document.Footer is { IsEmpty: false };

        // A footnotes part is emitted only when the document actually carries footnotes.
        var hasFootnotes = document.Footnotes.Count > 0;

        // An endnotes part is emitted only when the document actually carries endnotes.
        var hasEndnotes = document.Endnotes.Count > 0;

        // A comments part is emitted only when the document actually carries review comments.
        var hasComments = document.Comments.Count > 0;

        // The watermark text is persisted best-effort as a custom document property (docProps/custom.xml),
        // emitted only when a watermark is set.
        var hasWatermark = !string.IsNullOrEmpty(document.Page.Watermark);

        // A word/settings.xml part is emitted only when something needs it — document protection
        // (w:documentProtection) and/or automatic hyphenation (w:autoHyphenation) — so documents that
        // need neither round-trip exactly as before (no settings part).
        var hasProtection = document.Protection.IsProtected;
        var hasSettings = hasProtection || document.Page.AutoHyphenation;

        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
        WritePart(archive, "[Content_Types].xml", BuildContentTypes(images.Count > 0, hasLists, hasHeader, hasFooter, hasFootnotes, hasEndnotes, hasComments, hasWatermark, hasSettings, charts, embeddedObjects.Count > 0, smartArts));
        WritePart(archive, "_rels/.rels", BuildPackageRels(hasWatermark));
        WritePart(archive, "docProps/core.xml", BuildCoreProperties(document.Properties));
        if (hasWatermark)
            WritePart(archive, "docProps/custom.xml", BuildCustomProperties(document.Page.Watermark!));
        WritePart(archive, "word/_rels/document.xml.rels", BuildDocumentRels(images, hyperlinks, hasLists, hasHeader, hasFooter, hasFootnotes, hasEndnotes, hasComments, hasSettings, charts, embeddedObjects, smartArts));
        WritePart(archive, "word/document.xml", BuildDocument(document, images, charts, embeddedObjects, smartArts, hyperlinks, hasHeader, hasFooter));
        WritePart(archive, "word/styles.xml", BuildStyles(document));
        if (hasSettings)
            WritePart(archive, SettingsPartName.TrimStart('/'), BuildSettings(document.Protection, document.Page.AutoHyphenation));
        if (hasLists)
            WritePart(archive, "word/numbering.xml", BuildNumbering());
        if (hasHeader)
            WritePart(archive, HeaderPartName, BuildHeaderFooter(W + "hdr", document.Header!));
        if (hasFooter)
            WritePart(archive, FooterPartName, BuildHeaderFooter(W + "ftr", document.Footer!));
        if (hasFootnotes)
            WritePart(archive, FootnotesPartName.TrimStart('/'), BuildFootnotes(document));
        if (hasEndnotes)
            WritePart(archive, EndnotesPartName.TrimStart('/'), BuildEndnotes(document));
        if (hasComments)
            WritePart(archive, CommentsPartName.TrimStart('/'), BuildComments(document));
        foreach (var image in images)
            WriteBinaryPart(archive, "word/media/" + image.FileName, image.Image.PngBytes);
        foreach (var chart in charts)
            WritePart(archive, "word/charts/" + chart.FileName, BuildChartSpace(chart.Chart));
        // Each embedded OLE object's native payload is written verbatim as a binary part. Its presentation
        // icon (if any) was appended to `images` by CollectEmbeddedObjects and is emitted in the media loop.
        foreach (var embedded in embeddedObjects)
            WriteBinaryPart(archive, "word/embeddings/" + embedded.FileName, embedded.EmbeddedObject.Payload);
        foreach (var smartArt in smartArts)
        {
            WritePart(archive, "word/diagrams/" + smartArt.DataFileName, BuildDiagramData(smartArt.SmartArt));
            WritePart(archive, "word/diagrams/" + smartArt.LayoutFileName, BuildDiagramLayout(smartArt.SmartArt.Kind));
            WritePart(archive, "word/diagrams/" + smartArt.QuickStyleFileName, BuildDiagramQuickStyle());
            WritePart(archive, "word/diagrams/" + smartArt.ColorsFileName, BuildDiagramColors());
        }
    }

    /// <summary>An inline image paired with the relationship id, media file name and a unique drawing id.</summary>
    private sealed record ImagePart(InlineImage Image, string RelationshipId, string FileName, uint DrawingId);

    /// <summary>
    /// An inline chart paired with the document relationship id, chart part file name (relative to
    /// <c>word/charts/</c>) and a unique drawing id, mirroring <see cref="ImagePart"/>.
    /// </summary>
    private sealed record ChartPart(Chart Chart, string RelationshipId, string FileName, uint DrawingId);

    private static List<ChartPart> CollectCharts(TextDocument document)
    {
        var charts = new List<ChartPart>();
        foreach (var paragraph in EnumerateParagraphs(document))
            foreach (var run in paragraph.Runs)
                if (run.Chart is { } chart)
                {
                    var index = charts.Count + 1;
                    charts.Add(new ChartPart(chart, $"rIdChart{index}", $"chart{index}.xml", (uint)index));
                }
        return charts;
    }

    /// <summary>
    /// An inline embedded OLE object paired with its document relationship id (to the .bin part), the binary
    /// part file name (relative to <c>word/embeddings/</c>), the VML shape id, and — when the object carries
    /// a presentation icon — the <see cref="ImagePart"/> emitting that icon as a media part. Mirrors
    /// <see cref="ChartPart"/>; the icon part is shared with the ordinary image plumbing.
    /// </summary>
    private sealed record EmbeddedObjectPart(
        EmbeddedObject EmbeddedObject,
        string RelationshipId,
        string FileName,
        string ShapeId,
        ImagePart? IconPart);

    /// <summary>
    /// Assigns each inline embedded OLE object a relationship id (rIdOleN), a binary part name
    /// (oleObjectN.bin) and a VML shape id. When the object has a presentation icon, an extra
    /// <see cref="ImagePart"/> is appended to <paramref name="images"/> so the icon's media part, document
    /// relationship and png content-type flow through the existing image plumbing unchanged. The walk order
    /// matches <see cref="EnumerateParagraphs"/> so document.xml and the rels agree on which ids belong to
    /// which run (replayed in <see cref="BuildDocument"/>).
    /// </summary>
    private static List<EmbeddedObjectPart> CollectEmbeddedObjects(TextDocument document, List<ImagePart> images)
    {
        var embedded = new List<EmbeddedObjectPart>();
        foreach (var paragraph in EnumerateParagraphs(document))
            foreach (var run in paragraph.Runs)
                if (run.EmbeddedObject is { } obj)
                {
                    var index = embedded.Count + 1;
                    ImagePart? iconPart = null;
                    if (obj.Icon is { } icon)
                    {
                        // Continue the image numbering so the icon media file name never clashes with a body
                        // image; the appended part is emitted by the ordinary media/rel/content-type loops.
                        var imageIndex = images.Count + 1;
                        iconPart = new ImagePart(icon, $"rIdImg{imageIndex}", $"image{imageIndex}.png", (uint)imageIndex);
                        images.Add(iconPart);
                    }
                    embedded.Add(new EmbeddedObjectPart(obj, $"rIdOle{index}", $"oleObject{index}.bin", $"_oleObj{index}", iconPart));
                }
        return embedded;
    }

    /// <summary>
    /// An inline SmartArt diagram paired with its four document relationship ids and four part file names
    /// (relative to <c>word/diagrams/</c>) plus a unique drawing id. The diagram is four separate XML parts
    /// — data (the node text/structure), layout, quickStyle and colors — referenced together from the run
    /// drawing's <c>dgm:relIds</c>. Mirrors <see cref="ChartPart"/>.
    /// </summary>
    private sealed record SmartArtPart(
        SmartArt SmartArt,
        string DataRelationshipId,
        string LayoutRelationshipId,
        string QuickStyleRelationshipId,
        string ColorsRelationshipId,
        string DataFileName,
        string LayoutFileName,
        string QuickStyleFileName,
        string ColorsFileName,
        uint DrawingId);

    private static List<SmartArtPart> CollectSmartArts(TextDocument document)
    {
        var smartArts = new List<SmartArtPart>();
        foreach (var paragraph in EnumerateParagraphs(document))
            foreach (var run in paragraph.Runs)
                if (run.SmartArt is { } smartArt)
                {
                    var index = smartArts.Count + 1;
                    smartArts.Add(new SmartArtPart(
                        smartArt,
                        $"rIdDgmData{index}",
                        $"rIdDgmLayout{index}",
                        $"rIdDgmStyle{index}",
                        $"rIdDgmColors{index}",
                        $"data{index}.xml",
                        $"layout{index}.xml",
                        $"quickStyle{index}.xml",
                        $"colors{index}.xml",
                        (uint)index));
                }
        return smartArts;
    }

    private static List<ImagePart> CollectImages(TextDocument document)
    {
        var images = new List<ImagePart>();
        foreach (var paragraph in EnumerateParagraphs(document))
            foreach (var run in paragraph.Runs)
                if (run.Image is { } image)
                {
                    var index = images.Count + 1;
                    images.Add(new ImagePart(image, $"rIdImg{index}", $"image{index}.png", (uint)index));
                }
        return images;
    }

    /// <summary>Maps each distinct hyperlink URL to one external relationship id (rIdLinkN).</summary>
    private static Dictionary<string, string> CollectHyperlinks(TextDocument document)
    {
        var byUrl = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var paragraph in EnumerateParagraphs(document))
            foreach (var run in paragraph.Runs)
                if (run.HyperlinkUrl is { Length: > 0 } url && !byUrl.ContainsKey(url))
                    byUrl[url] = $"rIdLink{byUrl.Count + 1}";
        return byUrl;
    }

    /// <summary>All paragraphs, including those nested inside table cells (where runs can also live).</summary>
    private static IEnumerable<Paragraph> EnumerateParagraphs(TextDocument document)
    {
        foreach (var block in document.Blocks)
        {
            if (block is Paragraph paragraph)
                yield return paragraph;
            else if (block is Table table)
                foreach (var row in table.Rows)
                    foreach (var cell in row.Cells)
                        foreach (var cellParagraph in cell.Paragraphs)
                            yield return cellParagraph;
        }
    }

    private static void WritePart(ZipArchive archive, string entryPath, XDocument content)
    {
        var entry = archive.CreateEntry(entryPath, CompressionLevel.Optimal);
        using var entryStream = entry.Open();
        content.Save(entryStream);
    }

    private static void WriteBinaryPart(ZipArchive archive, string entryPath, byte[] content)
    {
        var entry = archive.CreateEntry(entryPath, CompressionLevel.Optimal);
        using var entryStream = entry.Open();
        entryStream.Write(content, 0, content.Length);
    }

    private static XDocument BuildContentTypes(bool includePng, bool includeNumbering, bool hasHeader, bool hasFooter, bool hasFootnotes, bool hasEndnotes, bool hasComments, bool hasWatermark, bool hasSettings, IReadOnlyList<ChartPart> charts, bool hasEmbeddedObjects, IReadOnlyList<SmartArtPart> smartArts) => new(
        new XElement(Ct + "Types",
            new XElement(Ct + "Default", new XAttribute("Extension", "rels"),
                new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
            new XElement(Ct + "Default", new XAttribute("Extension", "xml"),
                new XAttribute("ContentType", "application/xml")),
            includePng
                ? new XElement(Ct + "Default", new XAttribute("Extension", "png"),
                    new XAttribute("ContentType", "image/png"))
                : null,
            // A single Default for the bin extension covers every embedded OLE payload part.
            hasEmbeddedObjects
                ? new XElement(Ct + "Default", new XAttribute("Extension", "bin"),
                    new XAttribute("ContentType", OleObjectContentType))
                : null,
            new XElement(Ct + "Override", new XAttribute("PartName", "/word/document.xml"),
                new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml")),
            new XElement(Ct + "Override", new XAttribute("PartName", "/word/styles.xml"),
                new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml")),
            includeNumbering
                ? new XElement(Ct + "Override", new XAttribute("PartName", NumberingPartName),
                    new XAttribute("ContentType", NumberingContentType))
                : null,
            hasHeader
                ? new XElement(Ct + "Override", new XAttribute("PartName", "/" + HeaderPartName),
                    new XAttribute("ContentType", HeaderContentType))
                : null,
            hasFooter
                ? new XElement(Ct + "Override", new XAttribute("PartName", "/" + FooterPartName),
                    new XAttribute("ContentType", FooterContentType))
                : null,
            hasFootnotes
                ? new XElement(Ct + "Override", new XAttribute("PartName", FootnotesPartName),
                    new XAttribute("ContentType", FootnotesContentType))
                : null,
            hasEndnotes
                ? new XElement(Ct + "Override", new XAttribute("PartName", EndnotesPartName),
                    new XAttribute("ContentType", EndnotesContentType))
                : null,
            hasComments
                ? new XElement(Ct + "Override", new XAttribute("PartName", CommentsPartName),
                    new XAttribute("ContentType", CommentsContentType))
                : null,
            hasSettings
                ? new XElement(Ct + "Override", new XAttribute("PartName", SettingsPartName),
                    new XAttribute("ContentType", SettingsContentType))
                : null,
            new XElement(Ct + "Override", new XAttribute("PartName", CorePropertiesPartName),
                new XAttribute("ContentType", CorePropertiesContentType)),
            hasWatermark
                ? new XElement(Ct + "Override", new XAttribute("PartName", CustomPropertiesPartName),
                    new XAttribute("ContentType", CustomPropertiesContentType))
                : null,
            // One Override per chart part declares the DrawingML chart content type.
            charts.Select(chart => new XElement(Ct + "Override",
                new XAttribute("PartName", "/word/charts/" + chart.FileName),
                new XAttribute("ContentType", ChartContentType))),
            // Four Overrides per SmartArt diagram declare the data / layout / quickStyle / colors content types.
            smartArts.SelectMany(s => new[]
            {
                new XElement(Ct + "Override",
                    new XAttribute("PartName", "/word/diagrams/" + s.DataFileName),
                    new XAttribute("ContentType", DiagramDataContentType)),
                new XElement(Ct + "Override",
                    new XAttribute("PartName", "/word/diagrams/" + s.LayoutFileName),
                    new XAttribute("ContentType", DiagramLayoutContentType)),
                new XElement(Ct + "Override",
                    new XAttribute("PartName", "/word/diagrams/" + s.QuickStyleFileName),
                    new XAttribute("ContentType", DiagramStyleContentType)),
                new XElement(Ct + "Override",
                    new XAttribute("PartName", "/word/diagrams/" + s.ColorsFileName),
                    new XAttribute("ContentType", DiagramColorsContentType))
            })));

    private static XDocument BuildPackageRels(bool hasWatermark) => new(
        new XElement(Rel + "Relationships",
            new XElement(Rel + "Relationship",
                new XAttribute("Id", "rId1"),
                new XAttribute("Type", OfficeDocumentRel),
                new XAttribute("Target", "word/document.xml")),
            new XElement(Rel + "Relationship",
                new XAttribute("Id", "rIdCore"),
                new XAttribute("Type", CorePropertiesRelType),
                new XAttribute("Target", "docProps/core.xml")),
            hasWatermark
                ? new XElement(Rel + "Relationship",
                    new XAttribute("Id", "rIdCustom"),
                    new XAttribute("Type", CustomPropertiesRelType),
                    new XAttribute("Target", "docProps/custom.xml"))
                : null));

    /// <summary>
    /// Builds docProps/custom.xml carrying the page watermark text as a single named custom property
    /// (<see cref="WatermarkPropertyName"/>). This is a standards-compliant OPC custom-properties part,
    /// so the watermark text round-trips even though it is not a true Word VML watermark.
    /// </summary>
    private static XDocument BuildCustomProperties(string watermark) => new(
        new XElement(CustomProps + "Properties",
            new XAttribute(XNamespace.Xmlns + "vt", VtVariant.NamespaceName),
            new XElement(CustomProps + "property",
                new XAttribute("fmtid", "{D5CDD505-2E9C-101B-9397-08002B2CF9AE}"),
                new XAttribute("pid", "2"),
                new XAttribute("name", WatermarkPropertyName),
                new XElement(VtVariant + "lpwstr", watermark))));

    /// <summary>Builds docProps/core.xml from <see cref="DocumentProperties"/>, emitting only set values.</summary>
    private static XDocument BuildCoreProperties(DocumentProperties properties)
    {
        var core = new XElement(Cp + "coreProperties",
            new XAttribute(XNamespace.Xmlns + "cp", Cp.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "dc", Dc.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "dcterms", DcTerms.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "dcmitype", DcmiType.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "xsi", Xsi.NamespaceName));

        AddIfSet(core, Dc + "title", properties.Title);
        AddIfSet(core, Dc + "creator", properties.Author);
        AddIfSet(core, Dc + "subject", properties.Subject);
        AddIfSet(core, Cp + "keywords", properties.Keywords);
        AddIfSet(core, Dc + "description", properties.Comments);
        AddIfSet(core, Cp + "lastModifiedBy", properties.LastModifiedBy);
        AddTimestamp(core, DcTerms + "created", properties.Created);
        AddTimestamp(core, DcTerms + "modified", properties.Modified);

        return new XDocument(core);

        static void AddIfSet(XElement parent, XName name, string? value)
        {
            if (!string.IsNullOrEmpty(value))
                parent.Add(new XElement(name, value));
        }

        static void AddTimestamp(XElement parent, XName name, DateTimeOffset? value)
        {
            if (value is { } v)
                parent.Add(new XElement(name,
                    new XAttribute(Xsi + "type", "dcterms:W3CDTF"),
                    ToW3CDtf(v)));
        }
    }

    private static XDocument BuildDocumentRels(
        IReadOnlyList<ImagePart> images,
        IReadOnlyDictionary<string, string> hyperlinks,
        bool includeNumbering,
        bool hasHeader,
        bool hasFooter,
        bool hasFootnotes,
        bool hasEndnotes,
        bool hasComments,
        bool hasSettings,
        IReadOnlyList<ChartPart> charts,
        IReadOnlyList<EmbeddedObjectPart> embeddedObjects,
        IReadOnlyList<SmartArtPart> smartArts)
    {
        var relationships = new XElement(Rel + "Relationships",
            new XElement(Rel + "Relationship",
                new XAttribute("Id", "rId1"),
                new XAttribute("Type", StylesRel),
                new XAttribute("Target", "styles.xml")));
        if (hasSettings)
            relationships.Add(new XElement(Rel + "Relationship",
                new XAttribute("Id", SettingsRelationshipId),
                new XAttribute("Type", SettingsRelType),
                new XAttribute("Target", "settings.xml")));
        if (includeNumbering)
            relationships.Add(new XElement(Rel + "Relationship",
                new XAttribute("Id", "rIdNumbering"),
                new XAttribute("Type", NumberingRelType),
                new XAttribute("Target", "numbering.xml")));
        if (hasHeader)
            relationships.Add(new XElement(Rel + "Relationship",
                new XAttribute("Id", HeaderRelationshipId),
                new XAttribute("Type", HeaderRel),
                new XAttribute("Target", "header1.xml")));
        if (hasFooter)
            relationships.Add(new XElement(Rel + "Relationship",
                new XAttribute("Id", FooterRelationshipId),
                new XAttribute("Type", FooterRel),
                new XAttribute("Target", "footer1.xml")));
        if (hasFootnotes)
            relationships.Add(new XElement(Rel + "Relationship",
                new XAttribute("Id", FootnotesRelationshipId),
                new XAttribute("Type", FootnotesRelType),
                new XAttribute("Target", "footnotes.xml")));
        if (hasEndnotes)
            relationships.Add(new XElement(Rel + "Relationship",
                new XAttribute("Id", EndnotesRelationshipId),
                new XAttribute("Type", EndnotesRelType),
                new XAttribute("Target", "endnotes.xml")));
        if (hasComments)
            relationships.Add(new XElement(Rel + "Relationship",
                new XAttribute("Id", CommentsRelationshipId),
                new XAttribute("Type", CommentsRelType),
                new XAttribute("Target", "comments.xml")));
        foreach (var image in images)
            relationships.Add(new XElement(Rel + "Relationship",
                new XAttribute("Id", image.RelationshipId),
                new XAttribute("Type", ImageRel),
                new XAttribute("Target", "media/" + image.FileName)));
        foreach (var chart in charts)
            relationships.Add(new XElement(Rel + "Relationship",
                new XAttribute("Id", chart.RelationshipId),
                new XAttribute("Type", ChartRelType),
                new XAttribute("Target", "charts/" + chart.FileName)));
        // The embedded OLE payload relationship (the icon's image relationship is emitted in the images loop).
        foreach (var embedded in embeddedObjects)
            relationships.Add(new XElement(Rel + "Relationship",
                new XAttribute("Id", embedded.RelationshipId),
                new XAttribute("Type", OleObjectRelType),
                new XAttribute("Target", "embeddings/" + embedded.FileName)));
        // Each SmartArt diagram contributes four relationships (data / layout / quickStyle / colors), all
        // referenced together by the inline drawing's dgm:relIds.
        foreach (var s in smartArts)
        {
            relationships.Add(new XElement(Rel + "Relationship",
                new XAttribute("Id", s.DataRelationshipId),
                new XAttribute("Type", DiagramDataRelType),
                new XAttribute("Target", "diagrams/" + s.DataFileName)));
            relationships.Add(new XElement(Rel + "Relationship",
                new XAttribute("Id", s.LayoutRelationshipId),
                new XAttribute("Type", DiagramLayoutRelType),
                new XAttribute("Target", "diagrams/" + s.LayoutFileName)));
            relationships.Add(new XElement(Rel + "Relationship",
                new XAttribute("Id", s.QuickStyleRelationshipId),
                new XAttribute("Type", DiagramStyleRelType),
                new XAttribute("Target", "diagrams/" + s.QuickStyleFileName)));
            relationships.Add(new XElement(Rel + "Relationship",
                new XAttribute("Id", s.ColorsRelationshipId),
                new XAttribute("Type", DiagramColorsRelType),
                new XAttribute("Target", "diagrams/" + s.ColorsFileName)));
        }
        foreach (var (url, relationshipId) in hyperlinks)
            relationships.Add(new XElement(Rel + "Relationship",
                new XAttribute("Id", relationshipId),
                new XAttribute("Type", HyperlinkRel),
                new XAttribute("Target", url),
                new XAttribute("TargetMode", "External")));
        return new XDocument(relationships);
    }

    private static XDocument BuildDocument(
        TextDocument document,
        IReadOnlyList<ImagePart> images,
        IReadOnlyList<ChartPart> charts,
        IReadOnlyList<EmbeddedObjectPart> embeddedObjects,
        IReadOnlyList<SmartArtPart> smartArts,
        IReadOnlyDictionary<string, string> hyperlinks,
        bool hasHeader,
        bool hasFooter)
    {
        // Reset the document-scoped bookmark + revision id counters so ids start at 1 each write.
        System.Threading.Interlocked.Exchange(ref _bookmarkId, 0);
        System.Threading.Interlocked.Exchange(ref _revisionId, 0);
        // Seed the shape docPr counter just above the image drawing ids (1..imageCount) so the two id
        // spaces never overlap. Each shape BuildRun emits takes the next id.
        System.Threading.Interlocked.Exchange(ref _shapeDrawingId, images.Count);

        // Map each image/chart run to its assigned part by replaying the same walk order CollectImages /
        // CollectCharts used, so document.xml and the rels agree on which rId belongs to which run.
        var imagesByRun = new Dictionary<Run, ImagePart>();
        var chartsByRun = new Dictionary<Run, ChartPart>();
        var embeddedByRun = new Dictionary<Run, EmbeddedObjectPart>();
        var smartArtsByRun = new Dictionary<Run, SmartArtPart>();
        var nextImage = 0;
        var nextChart = 0;
        var nextEmbedded = 0;
        var nextSmartArt = 0;
        foreach (var paragraph in EnumerateParagraphs(document))
            foreach (var run in paragraph.Runs)
            {
                if (run.Image is not null)
                    imagesByRun[run] = images[nextImage++];
                if (run.Chart is not null)
                    chartsByRun[run] = charts[nextChart++];
                if (run.EmbeddedObject is not null)
                    embeddedByRun[run] = embeddedObjects[nextEmbedded++];
                if (run.SmartArt is not null)
                    smartArtsByRun[run] = smartArts[nextSmartArt++];
            }

        var drawings = new RunDrawings(imagesByRun, chartsByRun, embeddedByRun, smartArtsByRun);

        var body = new XElement(W + "body");
        foreach (var block in document.Blocks)
            body.Add(BuildBlock(block, drawings, hyperlinks));
        body.Add(BuildSectionProperties(document.Page, hasHeader, hasFooter));

        return new XDocument(
            new XElement(W + "document",
                new XAttribute(XNamespace.Xmlns + "w", W.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "r", R.NamespaceName),
                // w14 carries the checkbox content control element (w14:checkbox in a w:sdtPr).
                new XAttribute(XNamespace.Xmlns + "w14", W14.NamespaceName),
                // m carries inline equations (m:oMath and its children).
                new XAttribute(XNamespace.Xmlns + "m", M.NamespaceName),
                // wp/a/wps carry inline DrawingML shapes & text boxes (w:drawing/wp:inline/.../wps:wsp).
                new XAttribute(XNamespace.Xmlns + "wp", Wp.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "a", A.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "wps", Wps.NamespaceName),
                // v/o carry a classic embedded OLE object's VML presentation (w:object/v:shape/o:OLEObject).
                new XAttribute(XNamespace.Xmlns + "v", V.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "o", O.NamespaceName),
                // dgm carries the SmartArt diagram reference (w:drawing/.../a:graphicData/dgm:relIds).
                new XAttribute(XNamespace.Xmlns + "dgm", Dgm.NamespaceName),
                body));
    }

    /// <summary>
    /// Bundles the per-run image and chart part maps so the run builders can resolve either inline drawing
    /// from one parameter (rather than threading two dictionaries through every helper). Empty maps are
    /// shared by header/footer/footnote builders whose runs never carry body drawings.
    /// </summary>
    private sealed record RunDrawings(
        IReadOnlyDictionary<Run, ImagePart> Images,
        IReadOnlyDictionary<Run, ChartPart> Charts,
        IReadOnlyDictionary<Run, EmbeddedObjectPart> EmbeddedObjects,
        IReadOnlyDictionary<Run, SmartArtPart> SmartArts)
    {
        public static readonly RunDrawings None = new(
            new Dictionary<Run, ImagePart>(),
            new Dictionary<Run, ChartPart>(),
            new Dictionary<Run, EmbeddedObjectPart>(),
            new Dictionary<Run, SmartArtPart>());
    }

    /// <summary>Builds a header (w:hdr) or footer (w:ftr) part from its model paragraphs.</summary>
    private static XDocument BuildHeaderFooter(XName rootName, HeaderFooter content)
    {
        var root = new XElement(rootName,
            new XAttribute(XNamespace.Xmlns + "w", W.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "r", R.NamespaceName));

        // Header/footer runs do not carry inline images (the body image walk does not reach them).
        var noDrawings = RunDrawings.None;
        var noHyperlinks = new Dictionary<string, string>(StringComparer.Ordinal);

        if (content.Paragraphs.Count == 0)
            root.Add(new XElement(W + "p"));
        else
            foreach (var paragraph in content.Paragraphs)
                root.Add(BuildParagraph(paragraph, noDrawings, noHyperlinks));

        return new XDocument(root);
    }

    /// <summary>
    /// Builds word/footnotes.xml (w:footnotes). Emits the two conventional separator footnotes
    /// (w:footnoteSeparator id=-1, w:continuationSeparator id=0) for Word-friendliness, then one
    /// w:footnote w:id="N" per modelled footnote (ascending id), each holding its paragraphs.
    /// </summary>
    private static XDocument BuildFootnotes(TextDocument document)
    {
        var footnotes = new XElement(W + "footnotes",
            new XAttribute(XNamespace.Xmlns + "w", W.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "r", R.NamespaceName));

        XElement Separator(int id, string type) =>
            new(W + "footnote",
                new XAttribute(W + "type", type),
                new XAttribute(W + "id", id),
                new XElement(W + "p",
                    new XElement(W + "r", new XElement(W + type))));

        footnotes.Add(Separator(-1, "separator"));
        footnotes.Add(Separator(0, "continuationSeparator"));

        // Footnote paragraphs carry no inline images or hyperlinks (those walks target the body).
        var noDrawings = RunDrawings.None;
        var noHyperlinks = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var footnote in document.Footnotes.Values.OrderBy(f => f.Id))
        {
            var element = new XElement(W + "footnote", new XAttribute(W + "id", footnote.Id));
            if (footnote.Content.Count == 0)
                element.Add(new XElement(W + "p"));
            else
                foreach (var paragraph in footnote.Content)
                    element.Add(BuildParagraph(paragraph, noDrawings, noHyperlinks));
            footnotes.Add(element);
        }

        return new XDocument(footnotes);
    }

    /// <summary>
    /// Builds word/endnotes.xml (w:endnotes). Emits the two conventional separator endnotes
    /// (w:endnoteSeparator id=-1, w:continuationSeparator id=0) for Word-friendliness, then one
    /// w:endnote w:id="N" per modelled endnote (ascending id), each holding its paragraphs. Mirrors
    /// <see cref="BuildFootnotes"/>.
    /// </summary>
    private static XDocument BuildEndnotes(TextDocument document)
    {
        var endnotes = new XElement(W + "endnotes",
            new XAttribute(XNamespace.Xmlns + "w", W.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "r", R.NamespaceName));

        XElement Separator(int id, string type) =>
            new(W + "endnote",
                new XAttribute(W + "type", type),
                new XAttribute(W + "id", id),
                new XElement(W + "p",
                    new XElement(W + "r", new XElement(W + type))));

        endnotes.Add(Separator(-1, "separator"));
        endnotes.Add(Separator(0, "continuationSeparator"));

        // Endnote paragraphs carry no inline images or hyperlinks (those walks target the body).
        var noDrawings = RunDrawings.None;
        var noHyperlinks = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var endnote in document.Endnotes.Values.OrderBy(e => e.Id))
        {
            var element = new XElement(W + "endnote", new XAttribute(W + "id", endnote.Id));
            if (endnote.Content.Count == 0)
                element.Add(new XElement(W + "p"));
            else
                foreach (var paragraph in endnote.Content)
                    element.Add(BuildParagraph(paragraph, noDrawings, noHyperlinks));
            endnotes.Add(element);
        }

        return new XDocument(endnotes);
    }

    /// <summary>
    /// Builds word/comments.xml (w:comments): one w:comment w:id="N" per modelled comment (ascending
    /// id), each carrying w:author / w:initials and — when set — an explicit w:date, plus the comment's
    /// paragraphs. The date is only emitted when the model carries one, keeping the writer deterministic.
    /// </summary>
    private static XDocument BuildComments(TextDocument document)
    {
        var comments = new XElement(W + "comments",
            new XAttribute(XNamespace.Xmlns + "w", W.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "r", R.NamespaceName));

        // Comment paragraphs carry no inline images or hyperlinks (those walks target the body).
        var noDrawings = RunDrawings.None;
        var noHyperlinks = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var comment in document.Comments.Values.OrderBy(c => c.Id))
        {
            var element = new XElement(W + "comment",
                new XAttribute(W + "id", comment.Id),
                new XAttribute(W + "author", comment.Author),
                new XAttribute(W + "initials", comment.Initials));
            if (comment.DateXml is { Length: > 0 } date)
                element.Add(new XAttribute(W + "date", date));

            if (comment.Content.Count == 0)
                element.Add(new XElement(W + "p"));
            else
                foreach (var paragraph in comment.Content)
                    element.Add(BuildParagraph(paragraph, noDrawings, noHyperlinks));
            comments.Add(element);
        }

        return new XDocument(comments);
    }

    private static XElement BuildBlock(Block block, RunDrawings drawings, IReadOnlyDictionary<string, string> hyperlinks) => block switch
    {
        Table table => BuildTable(table, drawings, hyperlinks),
        Paragraph paragraph => BuildParagraph(paragraph, drawings, hyperlinks),
        _ => new XElement(W + "p")
    };

    // Light fills used by the table-style toggles: a blue-grey header fill and a grey banded-row fill.
    // These are emitted as cell shading on write so the styled docx renders correctly in Word; the
    // HeaderRow/BandedRows flags themselves round-trip via w:tblLook (see BuildTableProperties).
    private const string HeaderFill = "D9E2F3";
    private const string BandedFill = "F2F2F2";

    private static XElement BuildTable(Table table, RunDrawings drawings, IReadOnlyDictionary<string, string> hyperlinks)
    {
        var tbl = new XElement(W + "tbl", BuildTableProperties(table));

        // The table grid (one w:gridCol per column) follows w:tblPr when explicit widths are known.
        if (table.ColumnWidthsPt.Count > 0)
        {
            var grid = new XElement(W + "tblGrid");
            foreach (var widthPt in table.ColumnWidthsPt)
                grid.Add(new XElement(W + "gridCol", new XAttribute(W + "w", PointsToDxa(widthPt))));
            tbl.Add(grid);
        }

        var fmt = table.Formatting;
        for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            var row = table.Rows[rowIndex];
            var isHeaderRow = fmt.HeaderRow && rowIndex == 0;
            // Banded rows shade alternate body rows. With a header, body banding starts below the header,
            // so we band every other body row (the second body row, fourth, ...).
            var bandedShade = fmt.BandedRows && !isHeaderRow && IsBandedBodyRow(rowIndex, fmt.HeaderRow);

            var tr = new XElement(W + "tr");
            // Repeat the header row across page breaks (w:trPr/w:tblHeader) when requested.
            if (isHeaderRow && fmt.RepeatHeaderRow)
                tr.Add(new XElement(W + "trPr", new XElement(W + "tblHeader")));

            foreach (var cell in row.Cells)
            {
                var tc = new XElement(W + "tc");
                // The cell's own shading wins; header/banded fills only apply to otherwise-unshaded cells.
                var effectiveShade = cell.ShadingColorHex is { Length: > 0 }
                    ? null
                    : isHeaderRow ? HeaderFill : bandedShade ? BandedFill : null;
                var tcPr = BuildCellProperties(cell, effectiveShade);
                if (tcPr is not null)
                    tc.Add(tcPr);
                if (cell.Paragraphs.Count == 0)
                    tc.Add(new XElement(W + "p"));
                else
                    foreach (var paragraph in cell.Paragraphs)
                        tc.Add(BuildParagraph(isHeaderRow ? BoldHeaderParagraph(paragraph) : paragraph, drawings, hyperlinks));
                tr.Add(tc);
            }
            tbl.Add(tr);
        }
        return tbl;
    }

    /// <summary>
    /// True when the body row at <paramref name="rowIndex"/> should be banded (shaded). Body rows are
    /// counted from the first non-header row; every other body row (the 2nd, 4th, ...) is shaded, so the
    /// header (or first row) stays unshaded and banding alternates beneath it.
    /// </summary>
    private static bool IsBandedBodyRow(int rowIndex, bool hasHeader)
    {
        var bodyIndex = hasHeader ? rowIndex - 1 : rowIndex;
        return bodyIndex >= 0 && bodyIndex % 2 == 1;
    }

    /// <summary>
    /// Returns a copy of <paramref name="paragraph"/> with every run forced bold, used to render a
    /// header-row cell's text bold without mutating the model. Non-text runs (images/fields) are copied
    /// with their marks preserved; only the run formatting's Bold flag is overridden.
    /// </summary>
    private static Paragraph BoldHeaderParagraph(Paragraph paragraph)
    {
        var copy = new Paragraph
        {
            Formatting = paragraph.Formatting,
            StyleId = paragraph.StyleId,
            BookmarkName = paragraph.BookmarkName
        };
        foreach (var run in paragraph.Runs)
        {
            copy.Runs.Add(new Run(run.Text, run.Formatting with { Bold = true })
            {
                Image = run.Image,
                Equation = run.Equation,
                Chart = run.Chart,
                HyperlinkUrl = run.HyperlinkUrl,
                HyperlinkAnchor = run.HyperlinkAnchor,
                HyperlinkTooltip = run.HyperlinkTooltip,
                FieldKind = run.FieldKind,
                FootnoteId = run.FootnoteId,
                EndnoteId = run.EndnoteId,
                CommentId = run.CommentId,
                IsCommentReference = run.IsCommentReference,
                Revision = run.Revision,
                RevisionAuthor = run.RevisionAuthor,
                RevisionDateXml = run.RevisionDateXml,
                Control = run.Control
            });
        }
        return copy;
    }

    private static XElement BuildTableProperties(Table table)
    {
        var tblPr = new XElement(W + "tblPr",
            new XElement(W + "tblW", new XAttribute(W + "w", 0), new XAttribute(W + "type", "auto")));
        if (table.Formatting.Borders)
        {
            XElement Border(string name) => new(W + name,
                new XAttribute(W + "val", "single"),
                new XAttribute(W + "sz", 4),
                new XAttribute(W + "space", 0),
                new XAttribute(W + "color", "auto"));
            tblPr.Add(new XElement(W + "tblBorders",
                Border("top"), Border("left"), Border("bottom"), Border("right"),
                Border("insideH"), Border("insideV")));
        }
        else
        {
            tblPr.Add(new XElement(W + "tblBorders",
                new XElement(W + "top", new XAttribute(W + "val", "none")),
                new XElement(W + "left", new XAttribute(W + "val", "none")),
                new XElement(W + "bottom", new XAttribute(W + "val", "none")),
                new XElement(W + "right", new XAttribute(W + "val", "none")),
                new XElement(W + "insideH", new XAttribute(W + "val", "none")),
                new XElement(W + "insideV", new XAttribute(W + "val", "none"))));
        }

        // w:tblLook carries the table-style toggles so they round-trip without a full table-style part:
        // w:firstRow="1" persists HeaderRow; w:noHBand="0" persists BandedRows (banding on). The flags are
        // recovered on read from these attributes (see DocxReader.ReadTable). Only emitted when a toggle
        // is set, so plain tables stay unchanged.
        var fmt = table.Formatting;
        if (fmt.HeaderRow || fmt.BandedRows)
        {
            tblPr.Add(new XElement(W + "tblLook",
                new XAttribute(W + "firstRow", fmt.HeaderRow ? "1" : "0"),
                new XAttribute(W + "lastRow", "0"),
                new XAttribute(W + "firstColumn", "0"),
                new XAttribute(W + "lastColumn", "0"),
                new XAttribute(W + "noHBand", fmt.BandedRows ? "0" : "1"),
                new XAttribute(W + "noVBand", "1")));
        }
        return tblPr;
    }

    // Cell properties (w:tcPr): emitted only when the cell has an explicit width, span, vertical-merge
    // state and/or shading, so plain cells stay unchanged. Width is w:tcW (dxa); horizontal merge is
    // w:gridSpan; vertical merge is w:vMerge ("restart" on the top cell, "continue" below); shading
    // mirrors paragraph w:shd (fill colour). Child order follows the CT_TcPr schema sequence.
    // <paramref name="overrideShade"/> is a header/banded fill (RRGGBB, no '#') applied when the cell has
    // no shading of its own; the cell's explicit ShadingColorHex always takes precedence.
    private static XElement? BuildCellProperties(TableCell cell, string? overrideShade = null)
    {
        var tcPr = new XElement(W + "tcPr");
        if (cell.WidthPt is { } widthPt)
            tcPr.Add(new XElement(W + "tcW",
                new XAttribute(W + "w", PointsToDxa(widthPt)),
                new XAttribute(W + "type", "dxa")));
        if (cell.GridSpan > 1)
            tcPr.Add(new XElement(W + "gridSpan", new XAttribute(W + "val", cell.GridSpan)));
        if (cell.VerticalMerge == VerticalMergeState.Restart)
            tcPr.Add(new XElement(W + "vMerge", new XAttribute(W + "val", "restart")));
        else if (cell.VerticalMerge == VerticalMergeState.Continue)
            tcPr.Add(new XElement(W + "vMerge", new XAttribute(W + "val", "continue")));
        var fill = cell.ShadingColorHex is { Length: > 0 } shading ? shading.TrimStart('#') : overrideShade;
        if (fill is { Length: > 0 })
            tcPr.Add(new XElement(W + "shd",
                new XAttribute(W + "val", "clear"),
                new XAttribute(W + "color", "auto"),
                new XAttribute(W + "fill", fill)));
        return tcPr.HasElements ? tcPr : null;
    }

    // Bookmark ids are scoped to the whole document; one monotonically increasing counter keeps
    // every w:bookmarkStart/w:bookmarkEnd pair's w:id unique across all paragraphs.
    private static int _bookmarkId;

    // Revision (w:ins/w:del) ids are scoped to the whole document; this counter keeps each wrapper's
    // w:id unique across all paragraphs. Reset alongside _bookmarkId at the start of each document.
    private static int _revisionId;

    // Inline-shape wp:docPr ids are scoped to the whole document. Shapes carry no relationship/media (so
    // they are not in the image walk), and their docPr id must not collide with the image drawing ids
    // (1..imageCount). This counter is seeded just above the image count at the start of each document and
    // incremented once per shape as BuildRun emits it (the paragraph walk order is deterministic).
    private static int _shapeDrawingId;

    /// <summary>True when two runs carry the same tracked-change kind, author and date (so they coalesce).</summary>
    private static bool SameRevision(Run a, Run b) =>
        a.Revision == b.Revision
        && string.Equals(a.RevisionAuthor, b.RevisionAuthor, StringComparison.Ordinal)
        && string.Equals(a.RevisionDateXml, b.RevisionDateXml, StringComparison.Ordinal);

    /// <summary>
    /// Builds an empty w:ins (insertion) or w:del (deletion) wrapper carrying a unique w:id plus the
    /// run's author/date attributes. The caller fills it with the wrapped run/hyperlink elements. The
    /// run is assumed to carry a non-None revision.
    /// </summary>
    private static XElement NewRevisionWrapper(Run run)
    {
        var name = run.Revision == RevisionKind.Deleted ? "del" : "ins";
        var wrapper = new XElement(W + name,
            new XAttribute(W + "id", System.Threading.Interlocked.Increment(ref _revisionId)));
        if (run.RevisionAuthor is { Length: > 0 } author)
            wrapper.Add(new XAttribute(W + "author", author));
        if (run.RevisionDateXml is { Length: > 0 } date)
            wrapper.Add(new XAttribute(W + "date", date));
        return wrapper;
    }

    /// <summary>
    /// Builds the w:sdtPr (content-control properties) for a content control. Emits w:tag / w:alias when
    /// set, then the control-kind element: w:text for a plain-text control, or a w14:checkbox carrying the
    /// checked state (w14:checked val="1"/"0") for a checkbox. This is the minimal valid shape FreeW's own
    /// reader recovers (see <see cref="DocxReader"/>).
    /// </summary>
    private static XElement BuildSdtProperties(ContentControl control)
    {
        var sdtPr = new XElement(W + "sdtPr");
        if (control.Alias is { Length: > 0 } alias)
            sdtPr.Add(new XElement(W + "alias", new XAttribute(W + "val", alias)));
        if (control.Tag is { Length: > 0 } tag)
            sdtPr.Add(new XElement(W + "tag", new XAttribute(W + "val", tag)));
        if (control.Kind == ContentControlKind.CheckBox)
            sdtPr.Add(new XElement(W14 + "checkbox",
                new XElement(W14 + "checked", new XAttribute(W14 + "val", control.Checked ? "1" : "0"))));
        else
            sdtPr.Add(new XElement(W + "text"));
        return sdtPr;
    }

    private static XElement BuildParagraph(Paragraph paragraph, RunDrawings drawings, IReadOnlyDictionary<string, string> hyperlinks)
    {
        var p = new XElement(W + "p");
        var pPr = BuildParagraphProperties(paragraph);
        if (pPr is not null)
            p.Add(pPr);

        // A bookmarked paragraph is bracketed by a w:bookmarkStart/w:bookmarkEnd pair (siblings of the
        // runs) sharing one w:id; the start also carries the bookmark's w:name.
        var bookmarkId = -1;
        if (paragraph.BookmarkName is { Length: > 0 } bookmarkName)
        {
            bookmarkId = System.Threading.Interlocked.Increment(ref _bookmarkId);
            p.Add(new XElement(W + "bookmarkStart",
                new XAttribute(W + "id", bookmarkId),
                new XAttribute(W + "name", bookmarkName)));
        }

        // Wrap maximal spans of consecutive runs sharing the same hyperlink target in a single
        // w:hyperlink. External links reference the URL's relationship id (r:id); internal links
        // reference a bookmark name via w:anchor (no relationship).
        //
        // Review comments overlay this: a run carrying a CommentId (other than the textless reference
        // run) is bracketed by a w:commentRangeStart/End pair sharing that id, emitted as siblings of
        // the runs. The textless reference run (IsCommentReference) serialises as a w:commentReference
        // run placed just after the matching range end. Comment-covered runs are not also hyperlinks in
        // the editor, so the two wrappings do not interleave in practice.
        //
        // Tracked changes overlay this again: a run carrying Revision != None is wrapped in a w:ins
        // (insertion) or w:del (deletion) element carrying the author/date attributes; the wrapped run's
        // text serialises as w:delText (not w:t) inside a w:del so Word treats it as deleted content.
        // Consecutive runs sharing the same revision kind/author/date coalesce into one wrapper. The
        // wrapper sits between the paragraph (or hyperlink) and the run elements, while comment-range and
        // bookmark markers stay as paragraph-level siblings.
        var i = 0;
        var runs = paragraph.Runs;
        var openCommentId = (int?)null;

        // The current open revision wrapper (w:ins/w:del) and the run it was opened for; run-level
        // elements are added through Content(...) so they land inside the wrapper when one is open.
        XElement? revisionWrapper = null;
        Run? revisionKey = null;

        void FlushRevision()
        {
            if (revisionWrapper is not null)
            {
                p.Add(revisionWrapper);
                revisionWrapper = null;
                revisionKey = null;
            }
        }

        // Route one run-level element (a w:r or w:hyperlink) through the active revision wrapper,
        // (re)opening or closing it to match the run's revision mark before adding the element.
        void Content(Run run, XElement element)
        {
            if (run.Revision == RevisionKind.None)
            {
                FlushRevision();
                p.Add(element);
                return;
            }
            if (revisionKey is null || !SameRevision(revisionKey, run))
            {
                FlushRevision();
                revisionWrapper = NewRevisionWrapper(run);
                revisionKey = run;
            }
            revisionWrapper!.Add(element);
        }

        while (i < runs.Count)
        {
            // Update the open comment range to match this run before emitting it. The textless
            // reference run does not open/extend a range; it only emits the reference marker below.
            var coveringId = runs[i].IsCommentReference ? null : runs[i].CommentId;
            if (openCommentId != coveringId)
            {
                // Comment range markers are paragraph-level siblings, not revision content.
                FlushRevision();
                if (openCommentId is { } closing)
                    p.Add(new XElement(W + "commentRangeEnd", new XAttribute(W + "id", closing)));
                if (coveringId is { } opening)
                    p.Add(new XElement(W + "commentRangeStart", new XAttribute(W + "id", opening)));
                openCommentId = coveringId;
            }

            // A content control (w:sdt) wraps the maximal span of consecutive runs sharing the same
            // ContentControl instance. The wrapped run(s) keep their ordinary w:r form inside w:sdtContent;
            // the sdt itself still routes through the revision wrapper so a control can sit inside a
            // tracked change. Content controls are not also hyperlinks/comments in practice.
            var control = runs[i].Control;
            if (control is not null)
            {
                var head = runs[i];
                var content = new XElement(W + "sdtContent");
                while (i < runs.Count && ReferenceEquals(runs[i].Control, control)
                    && (runs[i].IsCommentReference ? null : runs[i].CommentId) == openCommentId
                    && SameRevision(head, runs[i]))
                    content.Add(BuildRun(runs[i++], drawings));
                var sdt = new XElement(W + "sdt", BuildSdtProperties(control), content);
                Content(head, sdt);
                continue;
            }

            var url = runs[i].HyperlinkUrl;
            var anchor = runs[i].HyperlinkAnchor;
            var tooltip = runs[i].HyperlinkTooltip;
            if (url is { Length: > 0 } && hyperlinks.TryGetValue(url, out var relationshipId))
            {
                var hyperlink = new XElement(W + "hyperlink", new XAttribute(R + "id", relationshipId));
                if (tooltip is { Length: > 0 })
                    hyperlink.Add(new XAttribute(W + "tooltip", tooltip));
                var head = runs[i];
                while (i < runs.Count && runs[i].HyperlinkUrl == url && runs[i].HyperlinkTooltip == tooltip && (runs[i].IsCommentReference ? null : runs[i].CommentId) == openCommentId && SameRevision(head, runs[i]))
                    hyperlink.Add(BuildRun(runs[i++], drawings));
                Content(head, hyperlink);
            }
            else if (anchor is { Length: > 0 })
            {
                var hyperlink = new XElement(W + "hyperlink", new XAttribute(W + "anchor", anchor));
                if (tooltip is { Length: > 0 })
                    hyperlink.Add(new XAttribute(W + "tooltip", tooltip));
                var head = runs[i];
                while (i < runs.Count && runs[i].HyperlinkAnchor == anchor && runs[i].HyperlinkTooltip == tooltip && (runs[i].IsCommentReference ? null : runs[i].CommentId) == openCommentId && SameRevision(head, runs[i]))
                    hyperlink.Add(BuildRun(runs[i++], drawings));
                Content(head, hyperlink);
            }
            else
            {
                var run = runs[i++];
                Content(run, BuildRun(run, drawings));
            }
        }

        // Close any still-open revision wrapper, then any still-open comment range, at paragraph end.
        FlushRevision();
        if (openCommentId is { } trailing)
            p.Add(new XElement(W + "commentRangeEnd", new XAttribute(W + "id", trailing)));

        if (bookmarkId >= 0)
            p.Add(new XElement(W + "bookmarkEnd", new XAttribute(W + "id", bookmarkId)));

        return p;
    }

    private static XElement? BuildParagraphProperties(Paragraph paragraph)
    {
        var pPr = new XElement(W + "pPr");
        if (!string.IsNullOrEmpty(paragraph.StyleId))
            pPr.Add(new XElement(W + "pStyle", new XAttribute(W + "val", paragraph.StyleId)));

        var f = paragraph.Formatting;
        // Flow control toggles, in CT_PPr schema order: keepNext, keepLines, pageBreakBefore,
        // widowControl. Each is a toggle element emitted only when its model flag is set, mirroring how
        // w:pageBreakBefore round-trips.
        // Keep this paragraph on the same page as the next (w:keepNext).
        if (f.KeepWithNext)
            pPr.Add(new XElement(W + "keepNext"));
        // Keep all lines of this paragraph together on one page (w:keepLines).
        if (f.KeepLinesTogether)
            pPr.Add(new XElement(W + "keepLines"));
        // Force a page break before this paragraph (w:pageBreakBefore); Word honours it when paginating.
        if (f.PageBreakBefore)
            pPr.Add(new XElement(W + "pageBreakBefore"));
        // Widow/orphan control (w:widowControl); only emitted when enabled (FreeW defaults it off).
        if (f.WidowControl)
            pPr.Add(new XElement(W + "widowControl"));
        if (f.ListKind != ListKind.None)
        {
            var numId = f.ListKind switch
            {
                ListKind.Number => NumberNumId,
                ListKind.MultiLevel => MultiLevelNumId,
                _ => BulletNumId
            };
            var level = Math.Clamp(f.ListLevel, 0, ListLevelCount - 1);
            pPr.Add(new XElement(W + "numPr",
                new XElement(W + "ilvl", new XAttribute(W + "val", level)),
                new XElement(W + "numId", new XAttribute(W + "val", numId))));
        }
        if (f.Alignment != TextAlignment.Left)
            pPr.Add(new XElement(W + "jc", new XAttribute(W + "val", f.Alignment switch
            {
                TextAlignment.Center => "center",
                TextAlignment.Right => "right",
                TextAlignment.Justify => "both",
                _ => "left"
            })));
        // Tab stops (w:tabs): one w:tab per stop, position in dxa, alignment via w:val, and an
        // optional w:leader fill. Mirrors how w:ind/w:spacing carry their dxa values.
        if (f.TabStops.Count > 0)
            pPr.Add(new XElement(W + "tabs",
                f.TabStops.Select(BuildTabStop)));
        if (f.SpaceBeforePt > 0 || f.SpaceAfterPt > 0)
            pPr.Add(new XElement(W + "spacing",
                new XAttribute(W + "before", PointsToDxa(f.SpaceBeforePt)),
                new XAttribute(W + "after", PointsToDxa(f.SpaceAfterPt))));
        if (f.IndentLeftPt > 0 || f.IndentRightPt > 0 || f.FirstLineIndentPt > 0)
            pPr.Add(new XElement(W + "ind",
                new XAttribute(W + "left", PointsToDxa(f.IndentLeftPt)),
                new XAttribute(W + "right", PointsToDxa(f.IndentRightPt)),
                new XAttribute(W + "firstLine", PointsToDxa(f.FirstLineIndentPt))));
        // Paragraph border (w:pBdr): a uniform box (all four edges) by default, or a bottom-only edge
        // when the border is a horizontal rule. Each edge shares one colour/width, analogous to w:tblBorders.
        if (f.Border is { } border)
        {
            XElement Edge(string name) => new(W + name,
                new XAttribute(W + "val", "single"),
                new XAttribute(W + "sz", PointsToEighthPoints(border.WidthPt)),
                new XAttribute(W + "space", 0),
                new XAttribute(W + "color", border.ColorHex.TrimStart('#')));
            pPr.Add(border.BottomOnly
                ? new XElement(W + "pBdr", Edge("bottom"))
                : new XElement(W + "pBdr", Edge("top"), Edge("left"), Edge("bottom"), Edge("right")));
        }
        // Paragraph shading (background fill), mirroring run-level w:shd highlight.
        if (f.ShadingColorHex is { Length: > 0 } shading)
            pPr.Add(new XElement(W + "shd",
                new XAttribute(W + "val", "clear"),
                new XAttribute(W + "color", "auto"),
                new XAttribute(W + "fill", shading.TrimStart('#'))));

        // A section break carried by this paragraph: the section's w:sectPr is the LAST child of w:pPr
        // (schema order), marking this paragraph as the end of a non-final section. Non-final sections do
        // not reference header/footer parts (only the body-level section does in FreeW). Reuses the shared
        // sectPr builder so per-section properties are emitted from one code path.
        if (paragraph.SectionBreak is { } section)
            pPr.Add(BuildSectionProperties(section.Page, hasHeader: false, hasFooter: false, section.BreakKind));

        return pPr.HasElements ? pPr : null;
    }

    /// <summary>
    /// Builds one <c>w:tab</c> for a paragraph tab stop: alignment in <c>w:val</c>, position in
    /// <c>w:pos</c> (dxa), and an optional <c>w:leader</c> fill emitted only when the stop carries
    /// one (so leaderless stops round-trip byte-for-byte as before).
    /// </summary>
    private static XElement BuildTabStop(TabStop stop)
    {
        var tab = new XElement(W + "tab",
            new XAttribute(W + "val", stop.Alignment switch
            {
                TabStopAlignment.Center => "center",
                TabStopAlignment.Right => "right",
                TabStopAlignment.Decimal => "decimal",
                _ => "left"
            }),
            new XAttribute(W + "pos", PointsToDxa(stop.PositionPt)));
        if (stop.Leader != TabLeader.None)
            tab.Add(new XAttribute(W + "leader", stop.Leader switch
            {
                TabLeader.Dots => "dot",
                TabLeader.Dashes => "hyphen",
                TabLeader.Underline => "underscore",
                _ => "none"
            }));
        return tab;
    }

    /// <summary>
    /// Maps a field kind to the WordprocessingML w:fldSimple/@w:instr keyword (with the surrounding
    /// spaces Word writes). Returns null for <see cref="RunFieldKind.None"/> (an ordinary text run).
    /// </summary>
    private static string? FieldInstruction(RunFieldKind kind) => kind switch
    {
        RunFieldKind.PageNumber => " PAGE ",
        RunFieldKind.Date => " DATE ",
        RunFieldKind.Time => " TIME ",
        RunFieldKind.FileName => " FILENAME ",
        RunFieldKind.Author => " AUTHOR ",
        RunFieldKind.NumPages => " NUMPAGES ",
        _ => null
    };

    private static XElement BuildRun(Run run, RunDrawings drawings)
    {
        // An inline equation serialises as an m:oMath emitted in place of the run (a paragraph-level
        // sibling of w:r, never wrapped in one), carrying its math fragments as m:r/m:sSup/m:f.
        if (run.Equation is { } equation)
            return BuildOMath(equation);

        // An inline shape / text box serialises as a w:r wrapping a w:drawing/wp:inline/.../wps:wsp.
        if (run.Shape is { } shape)
        {
            var sr = new XElement(W + "r");
            var rPr = BuildRunProperties(run.Formatting);
            if (rPr is not null)
                sr.Add(rPr);
            sr.Add(BuildShapeDrawing(shape));
            return sr;
        }

        // Inline WordArt serialises as a w:r wrapping a w:drawing/wp:inline/.../wps:wsp text box whose run
        // carries DrawingML text effects (chosen by the style preset) on its a:rPr.
        if (run.WordArt is { } wordArt)
        {
            var wr = new XElement(W + "r");
            var rPr = BuildRunProperties(run.Formatting);
            if (rPr is not null)
                wr.Add(rPr);
            wr.Add(BuildWordArtDrawing(wordArt));
            return wr;
        }

        // A document field emits a self-contained w:fldSimple wrapping a run; the wrapped run's w:t
        // carries the last-known/cached value as fallback text for field-unaware consumers. The
        // w:instr keyword identifies the field kind (PAGE, DATE, TIME, FILENAME, AUTHOR, NUMPAGES).
        if (FieldInstruction(run.FieldKind) is { } instruction)
            return new XElement(W + "fldSimple",
                new XAttribute(W + "instr", instruction),
                BuildTextRun(run, drawings));

        // A footnote reference is a superscript run carrying a w:footnoteReference (no literal text).
        // Carry the run's real formatting (forcing vertAlign=superscript) so a bold/coloured/sized
        // marker is preserved rather than discarded.
        if (run.FootnoteId is { } footnoteId)
            return MarkerRun(run, new XElement(W + "footnoteReference", new XAttribute(W + "id", footnoteId)));

        // An endnote reference is a superscript run carrying a w:endnoteReference (no literal text).
        if (run.EndnoteId is { } endnoteId)
            return MarkerRun(run, new XElement(W + "endnoteReference", new XAttribute(W + "id", endnoteId)));

        // The textless comment anchor run carries the w:commentReference for its id (no literal text).
        if (run is { IsCommentReference: true, CommentId: { } commentRefId })
            return new XElement(W + "r",
                new XElement(W + "commentReference", new XAttribute(W + "id", commentRefId)));

        return BuildTextRun(run, drawings);
    }

    // A textless marker run (footnote/endnote reference): carries the run's own formatting forced to
    // superscript, then the marker element. Preserves bold/colour/size that a caller put on the marker.
    private static XElement MarkerRun(Run run, XElement marker)
    {
        var r = new XElement(W + "r");
        var rPr = BuildRunProperties(run.Formatting with { VerticalAlign = VerticalAlign.Superscript });
        if (rPr is not null)
            r.Add(rPr);
        r.Add(marker);
        return r;
    }

    private static XElement BuildTextRun(Run run, RunDrawings drawings)
    {
        var r = new XElement(W + "r");
        var rPr = BuildRunProperties(run.Formatting);
        if (rPr is not null)
            r.Add(rPr);
        if (run.Image is not null && drawings.Images.TryGetValue(run, out var imagePart))
            r.Add(BuildDrawing(imagePart));
        else if (run.Chart is not null && drawings.Charts.TryGetValue(run, out var chartPart))
            r.Add(BuildChartDrawing(chartPart));
        else if (run.EmbeddedObject is not null && drawings.EmbeddedObjects.TryGetValue(run, out var embeddedPart))
            r.Add(BuildEmbeddedObject(embeddedPart));
        else if (run.SmartArt is not null && drawings.SmartArts.TryGetValue(run, out var smartArtPart))
            r.Add(BuildSmartArtDrawing(smartArtPart));
        else
        {
            // A tracked deletion stores its text in w:delText (so Word renders it as deleted content);
            // all other runs use the ordinary w:t element.
            var textElement = run.Revision == RevisionKind.Deleted ? "delText" : "t";
            r.Add(new XElement(W + textElement, new XAttribute(XNamespace.Xml + "space", "preserve"), run.Text));
        }
        return r;
    }

    /// <summary>
    /// Builds an inline OMML equation (m:oMath) from an <see cref="Equation"/>. Each fragment maps to its
    /// OMML element: plain text → m:r/m:t, superscript → m:sSup (m:e base, m:sup exponent), fraction →
    /// m:f (m:num numerator, m:den denominator). This is the minimal valid shape FreeW's own reader
    /// recovers (see <see cref="DocxReader"/>).
    /// </summary>
    private static XElement BuildOMath(Equation equation)
    {
        var oMath = new XElement(M + "oMath");
        foreach (var run in equation.Runs)
            oMath.Add(BuildMathRun(run));
        return oMath;
    }

    /// <summary>Builds the OMML element for a single math fragment (m:r, m:sSup or m:f).</summary>
    private static XElement BuildMathRun(MathRun run) => run.Kind switch
    {
        MathRunKind.Superscript => new XElement(M + "sSup",
            new XElement(M + "e", MathText(run.Base)),
            new XElement(M + "sup", MathText(run.Sup))),
        MathRunKind.Fraction => new XElement(M + "f",
            new XElement(M + "num", MathText(run.Numerator)),
            new XElement(M + "den", MathText(run.Denominator))),
        _ => MathText(run.Text)
    };

    /// <summary>Builds an m:r run carrying <paramref name="text"/> in an m:t (xml:space preserved).</summary>
    private static XElement MathText(string text) =>
        new(M + "r",
            new XElement(M + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), text));

    /// <summary>
    /// Builds the picture drawing for an image part. An inline image (the default) emits
    /// <c>w:drawing/wp:inline</c> exactly as before; a floating image (<see cref="InlineImage.Wrapping"/>
    /// not <see cref="ImageWrapping.Inline"/>) emits <c>w:drawing/wp:anchor</c> with the position + the
    /// matching wrap element. Both paths share the same <c>a:graphic/pic:pic</c> payload (see
    /// <see cref="BuildPicGraphic"/>).
    /// </summary>
    private static XElement BuildDrawing(ImagePart part) =>
        part.Image.IsFloating ? BuildAnchorDrawing(part) : BuildInlineDrawing(part);

    /// <summary>Builds an inline picture: w:drawing/wp:inline/a:graphic/pic:pic referencing the blip.</summary>
    private static XElement BuildInlineDrawing(ImagePart part)
    {
        var cx = PointsToEmu(part.Image.WidthPt);
        var cy = PointsToEmu(part.Image.HeightPt);

        return new XElement(W + "drawing",
            new XElement(Wp + "inline",
                new XAttribute(XNamespace.Xmlns + "wp", Wp.NamespaceName),
                new XAttribute("distT", 0), new XAttribute("distB", 0),
                new XAttribute("distL", 0), new XAttribute("distR", 0),
                new XElement(Wp + "extent", new XAttribute("cx", cx), new XAttribute("cy", cy)),
                new XElement(Wp + "effectExtent",
                    new XAttribute("l", 0), new XAttribute("t", 0),
                    new XAttribute("r", 0), new XAttribute("b", 0)),
                BuildDocPr(part),
                BuildPicGraphic(part, cx, cy)));
    }

    /// <summary>
    /// Builds a floating picture: w:drawing/wp:anchor with @behindDoc, wp:simplePos, wp:positionH/V
    /// (relativeFrom + posOffset), wp:extent, the single wrap element matching
    /// <see cref="InlineImage.Wrapping"/>, then the same wp:docPr + a:graphic/pic:pic payload as the inline
    /// path. wp:wrapTight is emitted without a wrapPolygon (a deliberate simplification — Word fills one in).
    /// </summary>
    private static XElement BuildAnchorDrawing(ImagePart part)
    {
        var image = part.Image;
        var cx = PointsToEmu(image.WidthPt);
        var cy = PointsToEmu(image.HeightPt);
        var behindDoc = image.Wrapping == ImageWrapping.Behind ? 1 : 0;

        return new XElement(W + "drawing",
            new XElement(Wp + "anchor",
                new XAttribute(XNamespace.Xmlns + "wp", Wp.NamespaceName),
                new XAttribute("distT", 0), new XAttribute("distB", 0),
                new XAttribute("distL", 0), new XAttribute("distR", 0),
                new XAttribute("simplePos", 0),
                new XAttribute("relativeHeight", 0),
                new XAttribute("behindDoc", behindDoc),
                new XAttribute("locked", 0),
                new XAttribute("layoutInCell", 1),
                new XAttribute("allowOverlap", 1),
                new XElement(Wp + "simplePos", new XAttribute("x", 0), new XAttribute("y", 0)),
                new XElement(Wp + "positionH",
                    new XAttribute("relativeFrom", HorizontalAnchorToken(image.HorizontalAnchor)),
                    new XElement(Wp + "posOffset", PointsToEmu(image.HorizontalOffsetPt))),
                new XElement(Wp + "positionV",
                    new XAttribute("relativeFrom", VerticalAnchorToken(image.VerticalAnchor)),
                    new XElement(Wp + "posOffset", PointsToEmu(image.VerticalOffsetPt))),
                new XElement(Wp + "extent", new XAttribute("cx", cx), new XAttribute("cy", cy)),
                new XElement(Wp + "effectExtent",
                    new XAttribute("l", 0), new XAttribute("t", 0),
                    new XAttribute("r", 0), new XAttribute("b", 0)),
                BuildWrap(image.Wrapping),
                BuildDocPr(part),
                BuildPicGraphic(part, cx, cy)));
    }

    /// <summary>The wp:positionH/@relativeFrom token for a horizontal anchor.</summary>
    private static string HorizontalAnchorToken(HorizontalAnchor anchor) => anchor switch
    {
        HorizontalAnchor.Margin => "margin",
        HorizontalAnchor.Page => "page",
        _ => "column",
    };

    /// <summary>The wp:positionV/@relativeFrom token for a vertical anchor.</summary>
    private static string VerticalAnchorToken(VerticalAnchor anchor) => anchor switch
    {
        VerticalAnchor.Margin => "margin",
        VerticalAnchor.Page => "page",
        _ => "paragraph",
    };

    /// <summary>
    /// The single wrap element for a floating wrapping mode: wp:wrapSquare (square), wp:wrapTight (tight,
    /// no wrapPolygon — a simplification), wp:wrapTopAndBottom, or wp:wrapNone for the front/behind modes.
    /// </summary>
    private static XElement BuildWrap(ImageWrapping wrapping) => wrapping switch
    {
        ImageWrapping.Square => new XElement(Wp + "wrapSquare", new XAttribute("wrapText", "bothSides")),
        ImageWrapping.Tight => new XElement(Wp + "wrapTight", new XAttribute("wrapText", "bothSides")),
        ImageWrapping.TopAndBottom => new XElement(Wp + "wrapTopAndBottom"),
        _ => new XElement(Wp + "wrapNone"), // Behind / InFront both wrap none (distinguished by @behindDoc).
    };

    /// <summary>
    /// Builds the wp:docPr for an image, carrying accessibility alt text on @descr when set (omitted
    /// otherwise so images without alt text serialise exactly as before). Shared by both drawing paths.
    /// </summary>
    private static XElement BuildDocPr(ImagePart part)
    {
        var docPr = new XElement(Wp + "docPr", new XAttribute("id", part.DrawingId), new XAttribute("name", part.FileName));
        if (!string.IsNullOrEmpty(part.Image.AltText))
            docPr.Add(new XAttribute("descr", part.Image.AltText));
        return docPr;
    }

    /// <summary>
    /// Builds the shared a:graphic/a:graphicData(uri=pic)/pic:pic payload referencing the blip, used by
    /// both the inline (<see cref="BuildInlineDrawing"/>) and floating (<see cref="BuildAnchorDrawing"/>)
    /// drawing paths so the picture markup is not duplicated.
    /// </summary>
    private static XElement BuildPicGraphic(ImagePart part, long cx, long cy) =>
        new(A + "graphic",
            new XAttribute(XNamespace.Xmlns + "a", A.NamespaceName),
            new XElement(A + "graphicData",
                new XAttribute("uri", Pic.NamespaceName),
                new XElement(Pic + "pic",
                    new XAttribute(XNamespace.Xmlns + "pic", Pic.NamespaceName),
                    new XElement(Pic + "nvPicPr",
                        new XElement(Pic + "cNvPr", new XAttribute("id", (uint)part.DrawingId), new XAttribute("name", part.FileName)),
                        new XElement(Pic + "cNvPicPr")),
                    new XElement(Pic + "blipFill",
                        new XElement(A + "blip", new XAttribute(R + "embed", part.RelationshipId)),
                        new XElement(A + "stretch", new XElement(A + "fillRect"))),
                    new XElement(Pic + "spPr",
                        new XElement(A + "xfrm",
                            new XElement(A + "off", new XAttribute("x", 0), new XAttribute("y", 0)),
                            new XElement(A + "ext", new XAttribute("cx", cx), new XAttribute("cy", cy))),
                        new XElement(A + "prstGeom", new XAttribute("prst", "rect"),
                            new XElement(A + "avLst"))))));

    /// <summary>The DrawingML preset-geometry token (a:prstGeom/@prst) for a shape kind.</summary>
    private static string PresetGeometry(ShapeKind kind) => kind switch
    {
        ShapeKind.RoundedRectangle => "roundRect",
        ShapeKind.Ellipse => "ellipse",
        _ => "rect", // Rectangle and TextBox both use a plain rectangle geometry.
    };

    /// <summary>
    /// Builds an inline DrawingML shape / text box: w:drawing/wp:inline/a:graphic/a:graphicData[uri=wps]/
    /// wps:wsp, carrying a wps:spPr (preset geometry + optional a:solidFill) and, for a text box, a
    /// wps:txbx/w:txbxContent holding the body paragraphs. The shape's wp:docPr id comes from the
    /// document-scoped <see cref="_shapeDrawingId"/> counter so it never collides with image drawing ids.
    /// </summary>
    private static XElement BuildShapeDrawing(Shape shape)
    {
        var cx = PointsToEmu(shape.WidthPt);
        var cy = PointsToEmu(shape.HeightPt);
        var docPrId = System.Threading.Interlocked.Increment(ref _shapeDrawingId);
        var name = $"{shape.Kind}{(uint)docPrId}";

        // wps:spPr: position/size (a:xfrm), preset geometry, then optional solid fill.
        var spPr = new XElement(Wps + "spPr",
            new XElement(A + "xfrm",
                new XElement(A + "off", new XAttribute("x", 0), new XAttribute("y", 0)),
                new XElement(A + "ext", new XAttribute("cx", cx), new XAttribute("cy", cy))),
            new XElement(A + "prstGeom", new XAttribute("prst", PresetGeometry(shape.Kind)),
                new XElement(A + "avLst")));
        if (shape.FillColorHex is { Length: > 0 } fill)
            spPr.Add(new XElement(A + "solidFill",
                new XElement(A + "srgbClr", new XAttribute("val", fill.TrimStart('#')))));

        var wsp = new XElement(Wps + "wsp",
            new XElement(Wps + "cNvSpPr"),
            spPr);

        // A text box carries its body paragraphs in wps:txbx/w:txbxContent. Body paragraphs do not carry
        // inline images or document hyperlinks, so they build against empty maps.
        if (shape.HasText)
        {
            var txbxContent = new XElement(W + "txbxContent");
            foreach (var paragraph in shape.TextParagraphs)
                txbxContent.Add(BuildParagraph(paragraph, RunDrawings.None, EmptyHyperlinks));
            wsp.Add(new XElement(Wps + "txbx", txbxContent));
        }

        // wps:bodyPr is required by the schema for a valid wsp; defaults are fine.
        wsp.Add(new XElement(Wps + "bodyPr"));

        return new XElement(W + "drawing",
            new XElement(Wp + "inline",
                new XAttribute("distT", 0), new XAttribute("distB", 0),
                new XAttribute("distL", 0), new XAttribute("distR", 0),
                new XElement(Wp + "extent", new XAttribute("cx", cx), new XAttribute("cy", cy)),
                new XElement(Wp + "effectExtent",
                    new XAttribute("l", 0), new XAttribute("t", 0),
                    new XAttribute("r", 0), new XAttribute("b", 0)),
                new XElement(Wp + "docPr", new XAttribute("id", (uint)docPrId), new XAttribute("name", name)),
                new XElement(A + "graphic",
                    new XElement(A + "graphicData",
                        new XAttribute("uri", Wps.NamespaceName),
                        wsp))));
    }

    /// <summary>Empty hyperlink map for building text-box body paragraphs (they carry no document rels).</summary>
    private static readonly Dictionary<string, string> EmptyHyperlinks = new();

    // The fixed colours a WordArt preset paints with (kept simple and deterministic so the reader can infer
    // the preset back from which effect elements are present, not from exact colour values).
    private const string WordArtFillColor = "1F4E79";        // a deep blue text fill
    private const string WordArtGradientStart = "4472C4";    // gradient stop 0
    private const string WordArtGradientEnd = "ED7D31";      // gradient stop 1
    private const string WordArtOutlineColor = "2E2E2E";     // outline / shadow colour

    /// <summary>
    /// Builds inline WordArt: a w:drawing/wp:inline/.../wps:wsp text box (exactly like a shape's text box)
    /// whose single text run carries DrawingML text effects on its a:rPr. The effects are chosen by the
    /// WordArt style preset: a solid or gradient text fill (a:solidFill/a:gradFill), an outline (a:ln),
    /// and/or an outer shadow (a:effectLst/a:outerShdw). The wp:docPr id comes from the document-scoped
    /// <see cref="_shapeDrawingId"/> counter (shared with shapes) so it never collides with image ids.
    /// </summary>
    private static XElement BuildWordArtDrawing(WordArt wordArt)
    {
        // WordArt has no intrinsic geometry size in the FreeW model; derive a sensible text-box extent from
        // the font size and text length so the inline drawing has a non-zero extent (Word recomputes it).
        var heightPt = wordArt.FontSizePt * 1.6;
        var widthPt = Math.Max(1, wordArt.Text.Length) * wordArt.FontSizePt * 0.62;
        var cx = PointsToEmu(widthPt);
        var cy = PointsToEmu(heightPt);
        var docPrId = System.Threading.Interlocked.Increment(ref _shapeDrawingId);
        var name = $"WordArt{(uint)docPrId}";

        // A plain text-box rect carries the WordArt; the decorative effects live on the run's a:rPr.
        var spPr = new XElement(Wps + "spPr",
            new XElement(A + "xfrm",
                new XElement(A + "off", new XAttribute("x", 0), new XAttribute("y", 0)),
                new XElement(A + "ext", new XAttribute("cx", cx), new XAttribute("cy", cy))),
            new XElement(A + "prstGeom", new XAttribute("prst", "rect"),
                new XElement(A + "avLst")));

        var wsp = new XElement(Wps + "wsp",
            new XElement(Wps + "cNvSpPr"),
            spPr,
            new XElement(Wps + "txbx",
                new XElement(W + "txbxContent", BuildWordArtParagraph(wordArt))),
            new XElement(Wps + "bodyPr"));

        return new XElement(W + "drawing",
            new XElement(Wp + "inline",
                new XAttribute("distT", 0), new XAttribute("distB", 0),
                new XAttribute("distL", 0), new XAttribute("distR", 0),
                new XElement(Wp + "extent", new XAttribute("cx", cx), new XAttribute("cy", cy)),
                new XElement(Wp + "effectExtent",
                    new XAttribute("l", 0), new XAttribute("t", 0),
                    new XAttribute("r", 0), new XAttribute("b", 0)),
                new XElement(Wp + "docPr", new XAttribute("id", (uint)docPrId), new XAttribute("name", name)),
                new XElement(A + "graphic",
                    new XElement(A + "graphicData",
                        new XAttribute("uri", Wps.NamespaceName),
                        wsp))));
    }

    /// <summary>
    /// Builds the single w:p inside a WordArt text box: a w:r whose w:rPr carries the font size (w:sz, in
    /// half-points) plus the DrawingML text effects (a:solidFill/a:gradFill/a:ln/a:effectLst) selected by the
    /// style preset, followed by the w:t text. The DrawingML effect elements sit directly under w:rPr exactly
    /// as Word emits WordArt text properties.
    /// </summary>
    private static XElement BuildWordArtParagraph(WordArt wordArt)
    {
        var rPr = new XElement(W + "rPr",
            new XElement(W + "sz", new XAttribute(W + "val", PointsToHalfPoints(wordArt.FontSizePt))),
            new XElement(W + "szCs", new XAttribute(W + "val", PointsToHalfPoints(wordArt.FontSizePt))));
        foreach (var effect in WordArtEffects(wordArt.Style))
            rPr.Add(effect);

        var run = new XElement(W + "r",
            rPr,
            new XElement(W + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), wordArt.Text));

        return new XElement(W + "p", run);
    }

    /// <summary>
    /// Expands a <see cref="WordArtStyle"/> preset into the DrawingML text-effect elements placed on the
    /// WordArt run's w:rPr. The reader infers the preset back from which of these are present:
    /// gradient → GradientFill, outline (a:ln) → Outline, shadow (a:effectLst) → Shadow, else FillBlue.
    /// </summary>
    private static IEnumerable<XElement> WordArtEffects(WordArtStyle style)
    {
        switch (style)
        {
            case WordArtStyle.GradientFill:
                yield return new XElement(A + "gradFill",
                    new XElement(A + "gsLst",
                        new XElement(A + "gs", new XAttribute("pos", 0),
                            new XElement(A + "srgbClr", new XAttribute("val", WordArtGradientStart))),
                        new XElement(A + "gs", new XAttribute("pos", 100000),
                            new XElement(A + "srgbClr", new XAttribute("val", WordArtGradientEnd)))),
                    new XElement(A + "lin", new XAttribute("ang", 5400000), new XAttribute("scaled", 1)));
                break;

            case WordArtStyle.Outline:
                yield return SolidFill(WordArtFillColor);
                yield return new XElement(A + "ln", new XAttribute("w", 9525),
                    SolidFill(WordArtOutlineColor));
                break;

            case WordArtStyle.Shadow:
                yield return SolidFill(WordArtFillColor);
                yield return new XElement(A + "effectLst",
                    new XElement(A + "outerShdw",
                        new XAttribute("blurRad", 50800),
                        new XAttribute("dist", 38100),
                        new XAttribute("dir", 2700000),
                        new XAttribute("algn", "tl"),
                        new XElement(A + "srgbClr", new XAttribute("val", WordArtOutlineColor),
                            new XElement(A + "alpha", new XAttribute("val", 40000)))));
                break;

            default: // FillBlue
                yield return SolidFill(WordArtFillColor);
                break;
        }
    }

    /// <summary>Builds an a:solidFill wrapping an a:srgbClr of the given RRGGBB hex value.</summary>
    private static XElement SolidFill(string hex) =>
        new(A + "solidFill", new XElement(A + "srgbClr", new XAttribute("val", hex)));

    /// <summary>
    /// Builds the inline chart drawing: w:drawing/wp:inline/a:graphic/a:graphicData(uri=chart)/c:chart
    /// referencing the chart part by relationship id (r:id). Mirrors <see cref="BuildDrawing"/> for images,
    /// but the graphicData wraps a c:chart reference rather than a pic:pic. The c namespace is declared on
    /// the c:chart element so the reference is self-describing.
    /// </summary>
    private static XElement BuildChartDrawing(ChartPart part)
    {
        var cx = PointsToEmu(part.Chart.WidthPt);
        var cy = PointsToEmu(part.Chart.HeightPt);
        var name = $"Chart {part.DrawingId}";

        return new XElement(W + "drawing",
            new XElement(Wp + "inline",
                new XAttribute(XNamespace.Xmlns + "wp", Wp.NamespaceName),
                new XAttribute("distT", 0), new XAttribute("distB", 0),
                new XAttribute("distL", 0), new XAttribute("distR", 0),
                new XElement(Wp + "extent", new XAttribute("cx", cx), new XAttribute("cy", cy)),
                new XElement(Wp + "effectExtent",
                    new XAttribute("l", 0), new XAttribute("t", 0),
                    new XAttribute("r", 0), new XAttribute("b", 0)),
                new XElement(Wp + "docPr", new XAttribute("id", part.DrawingId), new XAttribute("name", name)),
                new XElement(A + "graphic",
                    new XAttribute(XNamespace.Xmlns + "a", A.NamespaceName),
                    new XElement(A + "graphicData",
                        new XAttribute("uri", ChartGraphicDataUri),
                        new XElement(C + "chart",
                            new XAttribute(XNamespace.Xmlns + "c", C.NamespaceName),
                            new XAttribute(R + "id", part.RelationshipId))))));
    }

    /// <summary>
    /// Builds a classic embedded OLE object as a <c>w:object</c> wrapping the VML presentation: a
    /// <c>v:shape</c> sized in points carrying an <c>o:OLEObject</c> (Type="Embed", the model's ProgID, the
    /// shape id, and an <c>r:id</c> to the embedded <c>.bin</c> part) and — when the object has an icon — a
    /// <c>v:imagedata</c> whose <c>r:id</c> points at the icon media part. The VML namespaces (v/o) are
    /// declared on the document root (see <see cref="BuildDocument"/>).
    /// SIMPLIFICATION (Y2): the VML presentation is minimised to a single v:shape (+ optional v:imagedata);
    /// only embedded (not linked) objects are emitted, and no live OLE activation data is written.
    /// </summary>
    private static XElement BuildEmbeddedObject(EmbeddedObjectPart part)
    {
        // VML shapes size in points via a CSS-style @style; width/height map directly from the model.
        var style = $"width:{FormatPt(part.EmbeddedObject.WidthPt)}pt;height:{FormatPt(part.EmbeddedObject.HeightPt)}pt";

        var shape = new XElement(V + "shape",
            new XAttribute("id", part.ShapeId),
            new XAttribute("type", "#_oleObjType"),
            new XAttribute("style", style));
        // The on-page presentation: v:imagedata references the icon media part by relationship id.
        if (part.IconPart is { } icon)
            shape.Add(new XElement(V + "imagedata",
                new XAttribute(R + "id", icon.RelationshipId),
                new XAttribute(O + "title", "")));

        var ole = new XElement(O + "OLEObject",
            new XAttribute("Type", "Embed"),
            new XAttribute("ProgID", part.EmbeddedObject.ProgId),
            new XAttribute("ShapeID", part.ShapeId),
            new XAttribute("DrawAspect", "Icon"),
            new XAttribute("ObjectID", part.ShapeId),
            new XAttribute(R + "id", part.RelationshipId));

        return new XElement(W + "object", shape, ole);
    }

    /// <summary>Formats a point measure for a VML CSS @style value (invariant, trimmed of trailing zeros).</summary>
    private static string FormatPt(double points) =>
        points.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Builds the inline w:drawing for a SmartArt diagram: an a:graphicData[uri=diagram] whose body is a
    /// dgm:relIds carrying the four relationship ids (r:dm=data, r:lo=layout, r:qs=quickStyle, r:cs=colors).
    /// Mirrors <see cref="BuildChartDrawing"/> but references four parts instead of one.
    /// </summary>
    private static XElement BuildSmartArtDrawing(SmartArtPart part)
    {
        var cx = PointsToEmu(part.SmartArt.WidthPt);
        var cy = PointsToEmu(part.SmartArt.HeightPt);
        var name = $"Diagram {part.DrawingId}";

        return new XElement(W + "drawing",
            new XElement(Wp + "inline",
                new XAttribute(XNamespace.Xmlns + "wp", Wp.NamespaceName),
                new XAttribute("distT", 0), new XAttribute("distB", 0),
                new XAttribute("distL", 0), new XAttribute("distR", 0),
                new XElement(Wp + "extent", new XAttribute("cx", cx), new XAttribute("cy", cy)),
                new XElement(Wp + "effectExtent",
                    new XAttribute("l", 0), new XAttribute("t", 0),
                    new XAttribute("r", 0), new XAttribute("b", 0)),
                new XElement(Wp + "docPr", new XAttribute("id", part.DrawingId), new XAttribute("name", name)),
                new XElement(A + "graphic",
                    new XAttribute(XNamespace.Xmlns + "a", A.NamespaceName),
                    new XElement(A + "graphicData",
                        new XAttribute("uri", DiagramGraphicDataUri),
                        new XElement(Dgm + "relIds",
                            new XAttribute(XNamespace.Xmlns + "dgm", Dgm.NamespaceName),
                            new XAttribute(XNamespace.Xmlns + "r", R.NamespaceName),
                            new XAttribute(R + "dm", part.DataRelationshipId),
                            new XAttribute(R + "lo", part.LayoutRelationshipId),
                            new XAttribute(R + "qs", part.QuickStyleRelationshipId),
                            new XAttribute(R + "cs", part.ColorsRelationshipId))))));
    }

    /// <summary>
    /// Builds the SmartArt DATA part (word/diagrams/dataN.xml — dgm:dataModel). This is the only diagram
    /// part with real content: a dgm:ptLst holding one document point (type="doc") and one node point per
    /// model node (each carrying its text in a dgm:t/a:p/a:r/a:t body), plus a dgm:cxnLst whose parOf
    /// connections record the parent→child structure (used to recover the Hierarchy tree on read). Node ids
    /// are deterministic ("node0", "node1", …) in a stable pre-order walk so write/read agree.
    /// SIMPLIFICATION (Y1): no presentation-layer points (type="pres") or dsp:dataModelExt rendered geometry
    /// are emitted — Word re-runs auto-layout on open. The node text + structure here is the round-trip unit.
    /// </summary>
    private static XDocument BuildDiagramData(SmartArt smartArt)
    {
        const string docId = "doc0";
        var ptLst = new XElement(Dgm + "ptLst",
            new XElement(Dgm + "pt",
                new XAttribute("modelId", docId),
                new XAttribute("type", "doc")));
        var cxnLst = new XElement(Dgm + "cxnLst");

        var nextId = 0;
        var nextCxn = 0;
        // Pre-order walk: emit a node point + a parOf connection from its parent, then recurse children.
        void Emit(SmartArtNode node, string parentId)
        {
            var id = $"node{nextId++}";
            ptLst.Add(new XElement(Dgm + "pt",
                new XAttribute("modelId", id),
                new XElement(Dgm + "t",
                    new XElement(A + "bodyPr"),
                    new XElement(A + "lstStyle"),
                    new XElement(A + "p",
                        new XElement(A + "r",
                            new XElement(A + "t", node.Text))))));
            cxnLst.Add(new XElement(Dgm + "cxn",
                new XAttribute("modelId", $"cxn{nextCxn++}"),
                new XAttribute("type", "parOf"),
                new XAttribute("srcId", parentId),
                new XAttribute("destId", id)));
            foreach (var child in node.Children)
                Emit(child, id);
        }

        foreach (var node in smartArt.Nodes)
            Emit(node, docId);

        return new XDocument(
            new XElement(Dgm + "dataModel",
                new XAttribute(XNamespace.Xmlns + "dgm", Dgm.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "a", A.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "r", R.NamespaceName),
                ptLst,
                cxnLst,
                new XElement(Dgm + "bg"),
                new XElement(Dgm + "whole")));
    }

    /// <summary>
    /// Builds a minimal-but-valid SmartArt LAYOUT part (word/diagrams/layoutN.xml — dgm:layoutDef). The
    /// uniqueId records which stock layout the diagram intends (list / process / hierarchy); the layout body
    /// is intentionally near-empty (Word substitutes the built-in layout for the known uniqueId). The node
    /// text never lives here, so an empty layout does not lose data.
    /// </summary>
    private static XDocument BuildDiagramLayout(SmartArtKind kind)
    {
        var uniqueId = kind switch
        {
            SmartArtKind.Process => "urn:microsoft.com/office/officeart/2005/8/layout/process1",
            SmartArtKind.Hierarchy => "urn:microsoft.com/office/officeart/2005/8/layout/hierarchy1",
            _ => "urn:microsoft.com/office/officeart/2005/8/layout/list1"
        };
        return new XDocument(
            new XElement(Dgm + "layoutDef",
                new XAttribute(XNamespace.Xmlns + "dgm", Dgm.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "a", A.NamespaceName),
                new XAttribute("uniqueId", uniqueId),
                new XElement(Dgm + "title", new XAttribute("val", string.Empty)),
                new XElement(Dgm + "desc", new XAttribute("val", string.Empty)),
                new XElement(Dgm + "catLst"),
                new XElement(Dgm + "sampData"),
                new XElement(Dgm + "styleData"),
                new XElement(Dgm + "clrData"),
                new XElement(Dgm + "layoutNode",
                    new XAttribute("name", "diagram"))));
    }

    /// <summary>
    /// Builds a minimal-but-valid SmartArt QUICKSTYLE part (word/diagrams/quickStyleN.xml — dgm:styleDef).
    /// Stock/near-empty: carries no node data, so an empty style does not lose round-trip content.
    /// </summary>
    private static XDocument BuildDiagramQuickStyle() => new(
        new XElement(Dgm + "styleDef",
            new XAttribute(XNamespace.Xmlns + "dgm", Dgm.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "a", A.NamespaceName),
            new XAttribute("uniqueId", "urn:microsoft.com/office/officeart/2005/8/quickstyle/simple1"),
            new XElement(Dgm + "title", new XAttribute("val", string.Empty)),
            new XElement(Dgm + "desc", new XAttribute("val", string.Empty)),
            new XElement(Dgm + "catLst"),
            new XElement(Dgm + "scene3d",
                new XElement(A + "camera", new XAttribute("prst", "orthographicFront")),
                new XElement(A + "lightRig", new XAttribute("rig", "threePt"), new XAttribute("dir", "t"))),
            new XElement(Dgm + "style")));

    /// <summary>
    /// Builds a minimal-but-valid SmartArt COLORS part (word/diagrams/colorsN.xml — dgm:colorsDef).
    /// Stock/near-empty: carries no node data, so an empty colour set does not lose round-trip content.
    /// </summary>
    private static XDocument BuildDiagramColors() => new(
        new XElement(Dgm + "colorsDef",
            new XAttribute(XNamespace.Xmlns + "dgm", Dgm.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "a", A.NamespaceName),
            new XAttribute("uniqueId", "urn:microsoft.com/office/officeart/2005/8/colors/accent0_1"),
            new XElement(Dgm + "title", new XAttribute("val", string.Empty)),
            new XElement(Dgm + "desc", new XAttribute("val", string.Empty)),
            new XElement(Dgm + "catLst"),
            new XElement(Dgm + "styleLbl", new XAttribute("name", "node0"),
                new XElement(Dgm + "fillClrLst", new XAttribute("meth", "repeat"),
                    new XElement(A + "schemeClr", new XAttribute("val", "accent1"))),
                new XElement(Dgm + "linClrLst", new XAttribute("meth", "repeat"),
                    new XElement(A + "schemeClr", new XAttribute("val", "accent1"))),
                new XElement(Dgm + "txFillClrLst", new XAttribute("meth", "repeat"),
                    new XElement(A + "schemeClr", new XAttribute("val", "lt1"))),
                new XElement(Dgm + "txLinClrLst", new XAttribute("meth", "repeat"),
                    new XElement(A + "schemeClr", new XAttribute("val", "lt1"))))));

    /// <summary>
    /// Builds a self-contained DrawingML chart part (c:chartSpace) for <paramref name="chart"/>. Emits one
    /// plot area holding a single chart type (c:barChart for column/bar, c:lineChart for line,
    /// c:pieChart for pie) with the document's series, plus a category-axis / value-axis pair for the
    /// cartesian kinds. Category labels and series values are embedded as literal caches (c:strCache /
    /// c:numCache) so the chart needs no companion workbook part.
    /// SIMPLIFICATION (W3 milestone): no c:externalData / embedded xlsx is referenced — the caches are the
    /// sole data source. Word renders and round-trips this fine; only "Edit Data" in Word is unavailable.
    /// </summary>
    private static XDocument BuildChartSpace(Chart chart)
    {
        // Stable axis ids referenced by the plot's series-holding chart element (cartesian kinds only).
        const long catAxisId = 111111111L;
        const long valAxisId = 222222222L;

        var plotContent = chart.Kind == ChartKind.Pie
            ? BuildPieChart(chart)
            : BuildCartesianChart(chart, catAxisId, valAxisId);

        var plotArea = new XElement(C + "plotArea",
            new XElement(C + "layout"),
            plotContent);
        if (chart.Kind != ChartKind.Pie)
        {
            plotArea.Add(BuildCategoryAxis(catAxisId, valAxisId));
            plotArea.Add(BuildValueAxis(valAxisId, catAxisId));
        }

        var chartElement = new XElement(C + "chart");
        if (chart.Title is { Length: > 0 } title)
        {
            chartElement.Add(BuildChartTitle(title));
            chartElement.Add(new XElement(C + "autoTitleDeleted", new XAttribute(C + "val", "0")));
        }
        else
        {
            chartElement.Add(new XElement(C + "autoTitleDeleted", new XAttribute(C + "val", "1")));
        }
        chartElement.Add(plotArea);
        chartElement.Add(new XElement(C + "plotVisOnly", new XAttribute(C + "val", "1")));

        return new XDocument(
            new XElement(C + "chartSpace",
                new XAttribute(XNamespace.Xmlns + "c", C.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "a", A.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "r", R.NamespaceName),
                chartElement));
    }

    /// <summary>Builds a c:title carrying a single rich-text run with the chart title text.</summary>
    private static XElement BuildChartTitle(string title) =>
        new(C + "title",
            new XElement(C + "tx",
                new XElement(C + "rich",
                    new XElement(A + "bodyPr"),
                    new XElement(A + "lstStyle"),
                    new XElement(A + "p",
                        new XElement(A + "r",
                            new XElement(A + "t", title))))),
            new XElement(C + "overlay", new XAttribute(C + "val", "0")));

    /// <summary>
    /// Builds the c:barChart (column or bar) or c:lineChart element holding the chart's series and the
    /// axis-id back-references. barDir distinguishes column (vertical bars) from bar (horizontal).
    /// </summary>
    private static XElement BuildCartesianChart(Chart chart, long catAxisId, long valAxisId)
    {
        XElement root;
        if (chart.Kind == ChartKind.Line)
        {
            root = new XElement(C + "lineChart",
                new XElement(C + "grouping", new XAttribute(C + "val", "standard")));
        }
        else
        {
            root = new XElement(C + "barChart",
                new XElement(C + "barDir", new XAttribute(C + "val", chart.Kind == ChartKind.Bar ? "bar" : "col")),
                new XElement(C + "grouping", new XAttribute(C + "val", "clustered")));
        }

        for (var i = 0; i < chart.Series.Count; i++)
            root.Add(BuildSeries(chart, chart.Series[i], i));

        root.Add(new XElement(C + "axId", new XAttribute(C + "val", catAxisId)));
        root.Add(new XElement(C + "axId", new XAttribute(C + "val", valAxisId)));
        return root;
    }

    /// <summary>Builds the c:pieChart element holding the chart's first series (pie has no axes).</summary>
    private static XElement BuildPieChart(Chart chart)
    {
        var pie = new XElement(C + "pieChart",
            new XElement(C + "varyColors", new XAttribute(C + "val", "1")));
        if (chart.Series.Count > 0)
            pie.Add(BuildSeries(chart, chart.Series[0], 0));
        return pie;
    }

    /// <summary>
    /// Builds one c:ser: its index/order, an optional c:tx (series name) string cache, the shared category
    /// labels (c:cat → c:strRef/c:strCache) and the numeric values (c:val → c:numRef/c:numCache). The
    /// caches embed the data literally so the chart is self-contained.
    /// </summary>
    private static XElement BuildSeries(Chart chart, ChartSeries series, int index)
    {
        var ser = new XElement(C + "ser",
            new XElement(C + "idx", new XAttribute(C + "val", index)),
            new XElement(C + "order", new XAttribute(C + "val", index)));

        if (series.Name is { Length: > 0 } name)
            ser.Add(new XElement(C + "tx",
                new XElement(C + "strRef",
                    new XElement(C + "f", $"Sheet1!$B${index + 1}"),
                    new XElement(C + "strCache",
                        new XElement(C + "ptCount", new XAttribute(C + "val", 1)),
                        new XElement(C + "pt", new XAttribute(C + "idx", 0),
                            new XElement(C + "v", name))))));

        ser.Add(BuildCategoryCache(chart.Categories));
        ser.Add(BuildValueCache(series.Values));
        return ser;
    }

    /// <summary>Builds c:cat → c:strRef/c:strCache: the shared category labels as a literal string cache.</summary>
    private static XElement BuildCategoryCache(IReadOnlyList<string> categories)
    {
        var cache = new XElement(C + "strCache",
            new XElement(C + "ptCount", new XAttribute(C + "val", categories.Count)));
        for (var i = 0; i < categories.Count; i++)
            cache.Add(new XElement(C + "pt", new XAttribute(C + "idx", i),
                new XElement(C + "v", categories[i])));

        return new XElement(C + "cat",
            new XElement(C + "strRef",
                new XElement(C + "f", $"Sheet1!$A$1:$A${Math.Max(1, categories.Count)}"),
                cache));
    }

    /// <summary>Builds c:val → c:numRef/c:numCache: the series values as a literal number cache.</summary>
    private static XElement BuildValueCache(IReadOnlyList<double> values)
    {
        var cache = new XElement(C + "numCache",
            new XElement(C + "formatCode", "General"),
            new XElement(C + "ptCount", new XAttribute(C + "val", values.Count)));
        for (var i = 0; i < values.Count; i++)
            cache.Add(new XElement(C + "pt", new XAttribute(C + "idx", i),
                new XElement(C + "v", values[i].ToString(System.Globalization.CultureInfo.InvariantCulture))));

        return new XElement(C + "val",
            new XElement(C + "numRef",
                new XElement(C + "f", $"Sheet1!$B$1:$B${Math.Max(1, values.Count)}"),
                cache));
    }

    /// <summary>Builds the c:catAx (category axis) referencing its own id and cross-referencing the value axis.</summary>
    private static XElement BuildCategoryAxis(long axisId, long crossAxisId) =>
        new(C + "catAx",
            new XElement(C + "axId", new XAttribute(C + "val", axisId)),
            new XElement(C + "scaling", new XElement(C + "orientation", new XAttribute(C + "val", "minMax"))),
            new XElement(C + "delete", new XAttribute(C + "val", "0")),
            new XElement(C + "axPos", new XAttribute(C + "val", "b")),
            new XElement(C + "crossAx", new XAttribute(C + "val", crossAxisId)));

    /// <summary>Builds the c:valAx (value axis) referencing its own id and cross-referencing the category axis.</summary>
    private static XElement BuildValueAxis(long axisId, long crossAxisId) =>
        new(C + "valAx",
            new XElement(C + "axId", new XAttribute(C + "val", axisId)),
            new XElement(C + "scaling", new XElement(C + "orientation", new XAttribute(C + "val", "minMax"))),
            new XElement(C + "delete", new XAttribute(C + "val", "0")),
            new XElement(C + "axPos", new XAttribute(C + "val", "l")),
            new XElement(C + "crossAx", new XAttribute(C + "val", crossAxisId)));

    private static XElement? BuildRunProperties(RunFormatting f)
    {
        // Children MUST follow the CT_RPr (EG_RPrBase) schema sequence, otherwise Word's strict
        // validator rejects the run: rFonts, b, i, caps, smallCaps, strike, color, sz, szCs, u, shd,
        // vertAlign. (FreeW's own reader is order-independent, so order bugs only surface in Word.)
        var rPr = new XElement(W + "rPr");
        if (f.FontFamily is { Length: > 0 } family)
            rPr.Add(new XElement(W + "rFonts", new XAttribute(W + "ascii", family), new XAttribute(W + "hAnsi", family)));
        if (f.Bold)
            rPr.Add(new XElement(W + "b"));
        if (f.Italic)
            rPr.Add(new XElement(W + "i"));
        if (f.AllCaps)
            rPr.Add(new XElement(W + "caps"));
        if (f.SmallCaps)
            rPr.Add(new XElement(W + "smallCaps"));
        if (f.Strikethrough)
            rPr.Add(new XElement(W + "strike"));
        if (f.ColorHex is { Length: > 0 } color)
            rPr.Add(new XElement(W + "color", new XAttribute(W + "val", color.TrimStart('#'))));
        if (f.FontSizePt is { } size)
        {
            var halfPoints = PointsToHalfPoints(size);
            rPr.Add(new XElement(W + "sz", new XAttribute(W + "val", halfPoints)));
            rPr.Add(new XElement(W + "szCs", new XAttribute(W + "val", halfPoints)));
        }
        if (f.Underline)
            rPr.Add(new XElement(W + "u", new XAttribute(W + "val", "single")));
        if (f.HighlightColorHex is { Length: > 0 } highlight)
            rPr.Add(new XElement(W + "shd",
                new XAttribute(W + "val", "clear"),
                new XAttribute(W + "color", "auto"),
                new XAttribute(W + "fill", highlight.TrimStart('#'))));
        if (f.VerticalAlign is VerticalAlign.Superscript or VerticalAlign.Subscript)
            rPr.Add(new XElement(W + "vertAlign",
                new XAttribute(W + "val", f.VerticalAlign == VerticalAlign.Superscript ? "superscript" : "subscript")));

        return rPr.HasElements ? rPr : null;
    }

    /// <summary>
    /// Builds a w:sectPr for one section's <paramref name="page"/> settings. Used for both the final
    /// (body-level) section and each non-final section (whose sectPr lives in its last paragraph's pPr),
    /// so the per-section properties are emitted from one place rather than duplicated.
    /// <paramref name="hasHeader"/>/<paramref name="hasFooter"/> wire the default header/footer references
    /// (only the body-level section references them in FreeW). <paramref name="breakKind"/>, when non-null,
    /// emits the section's w:type (the break kind that begins it); the body-level final section passes null.
    /// </summary>
    private static XElement BuildSectionProperties(
        PageSettings page,
        bool hasHeader,
        bool hasFooter,
        SectionBreakKind? breakKind = null) =>
        new(W + "sectPr",
            // Header/footer references must precede pgSz/pgMar in the sectPr schema order.
            hasHeader
                ? new XElement(W + "headerReference",
                    new XAttribute(W + "type", "default"),
                    new XAttribute(R + "id", HeaderRelationshipId))
                : null,
            hasFooter
                ? new XElement(W + "footerReference",
                    new XAttribute(W + "type", "default"),
                    new XAttribute(R + "id", FooterRelationshipId))
                : null,
            // The section break kind (w:type) precedes pgSz in the schema. "nextPage" is Word's default and
            // is emitted explicitly only for non-final sections (the body-level final section passes null).
            breakKind is { } kind
                ? new XElement(W + "type", new XAttribute(W + "val", SectionBreakToken(kind)))
                : null,
            new XElement(W + "pgSz",
                new XAttribute(W + "w", PointsToDxa(page.WidthPt)),
                new XAttribute(W + "h", PointsToDxa(page.HeightPt)),
                page.Landscape ? new XAttribute(W + "orient", "landscape") : null),
            new XElement(W + "pgMar",
                new XAttribute(W + "left", PointsToDxa(page.MarginLeftPt)),
                new XAttribute(W + "right", PointsToDxa(page.MarginRightPt)),
                new XAttribute(W + "top", PointsToDxa(page.MarginTopPt)),
                new XAttribute(W + "bottom", PointsToDxa(page.MarginBottomPt))),
            // Page border (w:pgBorders): a uniform box on all four edges, offset from the page edge.
            // Emitted only when set; w:sz is in eighths of a point, matching w:pBdr edges.
            BuildPageBorders(page.PageBorder),
            // Line numbering (w:lnNumType): emitted only when enabled. Schema order places it after
            // pgBorders and before cols. @w:countBy is the numbering interval; @w:restart is
            // "continuous" (across pages) or "newPage" (restart each page).
            BuildLineNumbering(page),
            // Equal-width columns: w:cols carries the count (w:num) and inter-column gap (w:space, dxa).
            // Emitted unconditionally; w:num="1" is harmless and keeps the section shape stable.
            new XElement(W + "cols",
                new XAttribute(W + "num", Math.Max(1, page.ColumnCount)),
                new XAttribute(W + "space", PointsToDxa(page.ColumnSpacingPt))),
            // Vertical alignment of the page content (w:vAlign): emitted only when not Top, so existing
            // documents round-trip unchanged. Schema order places it after w:cols. Justified maps to "both".
            page.VerticalAlignment != PageVerticalAlignment.Top
                ? new XElement(W + "vAlign", new XAttribute(W + "val", VerticalAlignmentToken(page.VerticalAlignment)))
                : null,
            // "Different first page" (w:titlePg): a toggle emitted only when set, after w:vAlign. FreeW
            // still stores a single header/footer; the flag lets Word honour a distinct first-page header.
            page.DifferentFirstPage ? new XElement(W + "titlePg") : null);

    /// <summary>Maps a <see cref="SectionBreakKind"/> to its w:sectPr/w:type w:val token.</summary>
    private static string SectionBreakToken(SectionBreakKind kind) => kind switch
    {
        SectionBreakKind.Continuous => "continuous",
        SectionBreakKind.EvenPage => "evenPage",
        SectionBreakKind.OddPage => "oddPage",
        _ => "nextPage"
    };

    /// <summary>Maps a <see cref="PageVerticalAlignment"/> to its w:vAlign w:val token (Justified→"both").</summary>
    private static string VerticalAlignmentToken(PageVerticalAlignment alignment) => alignment switch
    {
        PageVerticalAlignment.Center => "center",
        PageVerticalAlignment.Justified => "both",
        PageVerticalAlignment.Bottom => "bottom",
        _ => "top"
    };

    /// <summary>
    /// Builds the w:lnNumType element (line numbering in the page margin), or null when line numbering
    /// is off (<see cref="LineNumberMode.None"/>). @w:countBy is the interval (every Nth line numbered),
    /// @w:restart maps the mode to "continuous" (across pages) or "newPage" (restart per page).
    /// </summary>
    private static XElement? BuildLineNumbering(PageSettings page)
    {
        if (page.LineNumberMode == LineNumberMode.None)
            return null;

        var restart = page.LineNumberMode == LineNumberMode.RestartEachPage ? "newPage" : "continuous";
        return new XElement(W + "lnNumType",
            new XAttribute(W + "countBy", Math.Max(1, page.LineNumberCountBy)),
            new XAttribute(W + "restart", restart),
            new XAttribute(W + "start", 1));
    }

    /// <summary>
    /// Builds the w:pgBorders element (a uniform box on all four edges) for a page border, or null when
    /// no page border is set. w:offsetFrom="page" with w:space="24" places the border 24pt off the page
    /// edge — Word's default. Edge widths (w:sz) are in eighths of a point, like w:pBdr.
    /// </summary>
    private static XElement? BuildPageBorders(PageBorder? border)
    {
        if (border is null)
            return null;

        XElement Edge(string name) => new(W + name,
            new XAttribute(W + "val", "single"),
            new XAttribute(W + "sz", PointsToEighthPoints(border.WidthPt)),
            new XAttribute(W + "space", 24),
            new XAttribute(W + "color", border.ColorHex.TrimStart('#')));

        return new XElement(W + "pgBorders",
            new XAttribute(W + "offsetFrom", "page"),
            Edge("top"), Edge("left"), Edge("bottom"), Edge("right"));
    }

    /// <summary>
    /// Builds word/numbering.xml: three abstract numbering definitions — bullet (abstractNumId 0),
    /// decimal (abstractNumId 1) and a multilevel/legal outline (abstractNumId 2) — each with
    /// <see cref="ListLevelCount"/> levels, mapped to w:num ids <see cref="BulletNumId"/>/
    /// <see cref="NumberNumId"/>/<see cref="MultiLevelNumId"/>.
    /// </summary>
    /// <remarks>
    /// The bullet and decimal definitions reuse one fixed lvlText across every level. The multilevel
    /// definition instead gives each level its own lvlText that accumulates the ancestor counters —
    /// level 0 = <c>%1.</c>, level 1 = <c>%1.%2.</c>, level 2 = <c>%1.%2.%3.</c>, … — so Word renders
    /// the familiar outline form (1, 1.1, 1.1.1). Every multilevel level is w:numFmt="decimal" and the
    /// indent grows one step (18pt) per level.
    /// </remarks>
    private static XDocument BuildNumbering()
    {
        XElement Lvl(int level, string numFmt, string lvlText) =>
            new(W + "lvl",
                new XAttribute(W + "ilvl", level),
                new XElement(W + "start", new XAttribute(W + "val", 1)),
                new XElement(W + "numFmt", new XAttribute(W + "val", numFmt)),
                new XElement(W + "lvlText", new XAttribute(W + "val", lvlText)),
                new XElement(W + "lvlJc", new XAttribute(W + "val", "left")),
                new XElement(W + "pPr",
                    new XElement(W + "ind",
                        new XAttribute(W + "left", PointsToDxa(36 + level * 18)),
                        new XAttribute(W + "hanging", PointsToDxa(18)))));

        XElement AbstractNum(int abstractNumId, string numFmt, string lvlText) =>
            new(W + "abstractNum", new XAttribute(W + "abstractNumId", abstractNumId),
                Enumerable.Range(0, ListLevelCount).Select(level => Lvl(level, numFmt, lvlText)));

        // Legal/outline numbering: level n's text is "%1.%2.…%(n+1)." — the dotted run of all ancestor
        // counters. e.g. level 0 -> "%1.", level 2 -> "%1.%2.%3.".
        XElement MultiLevelAbstractNum(int abstractNumId) =>
            new(W + "abstractNum", new XAttribute(W + "abstractNumId", abstractNumId),
                new XAttribute(W + "multiLevelType", "multilevel"),
                Enumerable.Range(0, ListLevelCount).Select(level => Lvl(level, "decimal",
                    string.Concat(Enumerable.Range(1, level + 1).Select(n => $"%{n}.")))));

        XElement Num(int numId, int abstractNumId) =>
            new(W + "num", new XAttribute(W + "numId", numId),
                new XElement(W + "abstractNumId", new XAttribute(W + "val", abstractNumId)));

        var numbering = new XElement(W + "numbering",
            new XAttribute(XNamespace.Xmlns + "w", W.NamespaceName),
            AbstractNum(0, "bullet", "•"),
            AbstractNum(1, "decimal", "%1."),
            MultiLevelAbstractNum(2),
            Num(BulletNumId, 0),
            Num(NumberNumId, 1),
            Num(MultiLevelNumId, 2));

        return new XDocument(numbering);
    }

    /// <summary>
    /// Builds word/settings.xml (w:settings) carrying the document-protection element and/or the
    /// automatic-hyphenation toggle. The caller only emits this part when something needs it (a non-None
    /// protection mode and/or <paramref name="autoHyphenation"/>). w:documentProtection records w:edit
    /// (the mode token) and w:enforcement="1"; w:autoHyphenation is a bare toggle. Schema order places
    /// w:documentProtection before w:autoHyphenation.
    /// </summary>
    private static XDocument BuildSettings(ProtectionSettings protection, bool autoHyphenation)
    {
        var settings = new XElement(W + "settings",
            new XAttribute(XNamespace.Xmlns + "w", W.NamespaceName));
        if (ProtectionEditToken(protection.Mode) is { } edit)
            settings.Add(new XElement(W + "documentProtection",
                new XAttribute(W + "edit", edit),
                new XAttribute(W + "enforcement", "1")));
        if (autoHyphenation)
            settings.Add(new XElement(W + "autoHyphenation"));
        return new XDocument(settings);
    }

    private static XDocument BuildStyles(TextDocument document)
    {
        var styles = new XElement(W + "styles", new XAttribute(XNamespace.Xmlns + "w", W.NamespaceName));
        foreach (var style in document.Styles.Values)
        {
            var element = new XElement(W + "style",
                new XAttribute(W + "type", style.Type == StyleType.Character ? "character" : "paragraph"),
                new XAttribute(W + "styleId", style.Id),
                new XElement(W + "name", new XAttribute(W + "val", style.Name)));
            if (!string.IsNullOrEmpty(style.BasedOnStyleId))
                element.Add(new XElement(W + "basedOn", new XAttribute(W + "val", style.BasedOnStyleId)));
            var rPr = BuildRunProperties(style.Run);
            if (rPr is not null)
                element.Add(rPr);
            styles.Add(element);
        }

        return new XDocument(styles);
    }
}
