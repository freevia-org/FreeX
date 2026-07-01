namespace FreeW.Core.IO;

/// <summary>
/// The single data-change point for FreeW's supported file formats. Adding a format is one line here plus a
/// new adapter and a registration-test tuple — no edits to the file-command dispatch or dialog code. FreeW
/// has no DI container (unlike the sibling FreeX app), so this is a plain static factory consumed where the
/// file commands are constructed; adapters are stateless, so a fresh list per host is fine.
/// </summary>
public static class DocumentFileAdapterCatalog
{
    /// <summary>
    /// Standard editable/importable document formats shown by normal Open and Save flows. Lossy PDF text
    /// import is intentionally excluded from this list and exposed through <see cref="CreatePdfImportAdapters"/>.
    /// </summary>
    public static IReadOnlyList<IDocumentFileAdapter> CreateDefaultAdapters() =>
    [
        DocxFileAdapter.Docx(),
        DocxFileAdapter.Docm(),
        DocxFileAdapter.Dotx(),
        DocxFileAdapter.Dotm(),
        DocxFileAdapter.Strict(),
        new WordXmlFileAdapter(),
        Wordml2003FileAdapter.Wordml2003(),
        new RtfFileAdapter(),
        HtmlFileAdapter.Filtered(),
        HtmlFileAdapter.WebPage(),
        new MhtmlFileAdapter(),
        new LegacyDocFileAdapter(),
        OdtFileAdapter.Odt(),
        OdtFileAdapter.Ott(),
        new PlainTextFileAdapter(),
    ];

    /// <summary>Import-only adapters that need an explicit host command instead of the normal Open dialog.</summary>
    public static IReadOnlyList<IDocumentFileAdapter> CreatePdfImportAdapters() =>
    [
        new PdfFileAdapter(),
    ];
}
