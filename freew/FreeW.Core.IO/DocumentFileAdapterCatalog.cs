namespace FreeW.Core.IO;

/// <summary>
/// The single data-change point for FreeW's supported file formats. Adding a format is one line here plus a
/// new adapter and a registration-test tuple — no edits to the file-command dispatch or dialog code. FreeW
/// has no DI container (unlike the sibling FreeX app), so this is a plain static factory consumed where the
/// file commands are constructed; adapters are stateless, so a fresh list per host is fine.
/// </summary>
public static class DocumentFileAdapterCatalog
{
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
        new PdfFileAdapter(),
        new LegacyDocFileAdapter(),
        OdtFileAdapter.Odt(),
        OdtFileAdapter.Ott(),
        new PlainTextFileAdapter(),
    ];
}
