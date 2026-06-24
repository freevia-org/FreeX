using FreeX.Core.IO;

namespace FreeX.App.Services;

public static class WorkbookFileAdapterCatalog
{
    public static IReadOnlyList<IFileAdapter> CreateDefaultAdapters() =>
    [
        new XlsxFileAdapter(),
        new XltxFileAdapter(),
        new XlsmFileAdapter(),
        new XltmFileAdapter(),
        new LegacyXlsFileAdapter(),
        new CsvFileAdapter(),
        new CsvUtf8FileAdapter(),
        new DelimitedTextFileAdapter(".txt", "Text (Tab delimited)", '\t'),
        new UnicodeTextFileAdapter(),
        new PrnFileAdapter(),
        new DelimitedTextFileAdapter(".tsv", "TSV (Tab-separated values)", '\t'),
        new DelimitedTextFileAdapter(".tab", "Tab-delimited text", '\t'),
        new SpreadsheetXmlFileAdapter(),
        new OdsFileAdapter(),
        new SlkFileAdapter(),
        new DifFileAdapter(),
        new DbfFileAdapter(),
        new HtmlFileAdapter(),
        new MhtFileAdapter(),
        new NativeJsonAdapter(),
    ];
}
