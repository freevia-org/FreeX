using FreeX.Core.IO;

namespace FreeX.App.Services;

public static class WorkbookFileAdapterCatalog
{
    public static IReadOnlyList<IFileAdapter> CreateDefaultAdapters() =>
    [
        new XlsxFileAdapter(),
        new XltxFileAdapter(),
        new LegacyXlsFileAdapter(),
        new CsvFileAdapter(),
        new CsvUtf8FileAdapter(),
        new DelimitedTextFileAdapter(".txt", "Text (Tab delimited)", '\t'),
        new UnicodeTextFileAdapter(),
        new DelimitedTextFileAdapter(".tsv", "TSV (Tab-separated values)", '\t'),
        new DelimitedTextFileAdapter(".tab", "Tab-delimited text", '\t'),
        new SpreadsheetXmlFileAdapter(),
        new NativeJsonAdapter(),
    ];
}
