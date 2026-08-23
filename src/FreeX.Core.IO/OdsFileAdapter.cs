using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using Free.Shared.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

/// <summary>
/// OpenDocument Spreadsheet (.ods) file adapter. An ODS file is a ZIP package of flat ODF XML:
/// <list type="bullet">
///   <item><c>mimetype</c> — a stored (uncompressed) first entry holding the ODF media type.</item>
///   <item><c>content.xml</c> — <c>office:document-content</c> → <c>office:automatic-styles</c>
///   (cell styles, column/row styles, number-format styles) + <c>office:body/office:spreadsheet</c>
///   with one <c>table:table</c> per sheet, <c>table:table-row</c>/<c>table:table-cell</c> grids,
///   <c>table:number-columns-repeated</c>/<c>number-rows-repeated</c> runs, and
///   <c>table:number-columns-spanned</c>/<c>rows-spanned</c> merges.</item>
///   <item><c>styles.xml</c> — document styles (we emit a minimal one; cell styles live in content's
///   automatic-styles for round-trip simplicity).</item>
///   <item><c>META-INF/manifest.xml</c> — the package manifest.</item>
/// </list>
///
/// <para><b>Round-trips faithfully (Full in the ODS capability profile):</b> cell values + types
/// (number/text/bool/date/percentage/currency), A1↔OpenFormula formulas, number formats, fonts
/// (name/size/bold/italic/underline/strike/color), fills (background), borders, alignment
/// (h/v/wrap/rotation/indent), merged cells, multiple sheets + names, column widths, row heights.</para>
///
/// <para><b>Deferred (None/Lossy in the profile):</b> charts, images, data validation, conditional
/// formatting, pivot tables, freeze panes — ODF can hold these but they are not yet mapped; their loss
/// is an expected ceiling, not a bug, per the §3a profile.</para>
/// </summary>
public sealed partial class OdsFileAdapter : IFileAdapter
{
    internal const string MimeType = "application/vnd.oasis.opendocument.spreadsheet";

    // ODF namespaces used across content.xml / styles.xml / manifest.xml.
    internal static readonly XNamespace OfficeNs = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
    internal static readonly XNamespace TableNs = "urn:oasis:names:tc:opendocument:xmlns:table:1.0";
    internal static readonly XNamespace TextNs = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";
    internal static readonly XNamespace StyleNs = "urn:oasis:names:tc:opendocument:xmlns:style:1.0";
    internal static readonly XNamespace FoNs = "urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0";
    internal static readonly XNamespace NumberNs = "urn:oasis:names:tc:opendocument:xmlns:datastyle:1.0";
    internal static readonly XNamespace SvgNs = "urn:oasis:names:tc:opendocument:xmlns:svg-compatible:1.0";

    private static readonly OpenDocumentXmlEntryOptions XmlEntryOptions = new(
        OmitXmlDeclaration: false,
        Indent: false,
        NewLineChars: "\n",
        NewLineHandling: NewLineHandling.Entitize,
        CloseOutput: false);

    private static readonly OpenDocumentManifestOptions ManifestOptions = new(
        Version: "1.2",
        RootEntryVersion: "1.2",
        IncludeXmlDeclaration: false);

    public string Extension => ".ods";
    public string FormatName => "OpenDocument Spreadsheet";

    public IReadOnlyList<FileFormatDescriptor> Formats { get; } =
    [
        new FileFormatDescriptor(".ods", "OpenDocument Spreadsheet", CanOpen: true, CanSave: true)
    ];

    public Workbook Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var archive = new ZipArchive(EnsureSeekable(stream), ZipArchiveMode.Read, leaveOpen: true);
        // Reject zip-bomb / oversized packages before any decompression-heavy reads (same guard xlsx uses).
        WorkbookOpenSizeGuard.EnsureArchiveWithinLimits(archive);
        var contentEntry = archive.GetEntry("content.xml")
            ?? throw new InvalidDataException("The ODS package does not contain a content.xml part.");

        XDocument contentDoc;
        using (var contentStream = contentEntry.Open())
            contentDoc = LoadXml(contentStream);

        // styles.xml may carry additional named styles; load it best-effort so styles referenced from
        // content (but defined in styles.xml) still resolve. Most LibreOffice files put per-cell automatic
        // styles in content.xml, which we read directly.
        XDocument? stylesDoc = null;
        var stylesEntry = archive.GetEntry("styles.xml");
        if (stylesEntry is not null)
        {
            try
            {
                using var s = stylesEntry.Open();
                stylesDoc = LoadXml(s);
            }
            catch (XmlException) { /* tolerate a malformed styles.xml; content.xml is authoritative */ }
        }

        return ReadWorkbook(contentDoc, stylesDoc);
    }

    public void Save(Workbook workbook, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(stream);
        SaveStreamPreparer.TruncateFromCurrentPosition(stream);

        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);

        // The mimetype part MUST be the first entry and stored uncompressed per the ODF packaging spec.
        OpenDocumentPackageWriter.WriteMimeType(archive, MimeType);

        var contentDoc = WriteContent(workbook);
        OpenDocumentPackageWriter.WriteXmlEntry(archive, "content.xml", contentDoc, XmlEntryOptions);
        OpenDocumentPackageWriter.WriteXmlEntry(archive, "styles.xml", BuildMinimalStyles(), XmlEntryOptions);
        OpenDocumentPackageWriter.WriteXmlEntry(
            archive,
            "META-INF/manifest.xml",
            OpenDocumentPackageWriter.BuildManifest(
                MimeType,
                [
                    new OpenDocumentManifestEntry("content.xml", "text/xml"),
                    new OpenDocumentManifestEntry("styles.xml", "text/xml"),
                    new OpenDocumentManifestEntry("META-INF/manifest.xml", "text/xml"),
                ],
                ManifestOptions),
            XmlEntryOptions);
    }

    private XDocument BuildMinimalStyles()
    {
        var root = new XElement(OfficeNs + "document-styles",
            new XAttribute(XNamespace.Xmlns + "office", OfficeNs.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "style", StyleNs.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "fo", FoNs.NamespaceName),
            new XAttribute(OfficeNs + "version", "1.2"),
            new XElement(OfficeNs + "styles"),
            new XElement(OfficeNs + "automatic-styles"),
            new XElement(OfficeNs + "master-styles"));
        return new XDocument(root);
    }

    // ---- xml helpers -----------------------------------------------------------------------------

    private static XDocument LoadXml(Stream stream)
    {
        // Use the shared hardened reader policy (DTD prohibited, no external resolver, and a
        // MaxCharactersInDocument ceiling) so a crafted content.xml/styles.xml part can't exhaust
        // memory the same way the other package-based adapters (XLSX/DOCX/ODT) are already guarded.
        var settings = SecureXmlReaderSettings.Create();
        settings.IgnoreWhitespace = false;
        using var reader = XmlReader.Create(stream, settings);
        return XDocument.Load(reader, LoadOptions.None);
    }

    /// <summary>
    /// <see cref="ZipArchive"/> in read mode needs a seekable stream. Most callers pass a FileStream or
    /// MemoryStream (already seekable); when a non-seekable stream arrives, buffer it once.
    /// </summary>
    private static Stream EnsureSeekable(Stream stream)
    {
        if (stream.CanSeek)
            return stream;
        var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        buffer.Position = 0;
        return buffer;
    }
}
