using FreeX.Core.IO;

namespace FreeX.App.Services;

public static class WorkbookFileAdapterCatalog
{
    public static IReadOnlyList<IFileAdapter> CreateDefaultAdapters() =>
    [
        new XlsxFileAdapter(),
        new LegacyXlsFileAdapter(),
        new CsvFileAdapter(),
        new DelimitedTextFileAdapter(".txt", "Text (Tab delimited)", '\t'),
        new DelimitedTextFileAdapter(".tsv", "TSV (Tab-separated values)", '\t'),
        new DelimitedTextFileAdapter(".tab", "Tab-delimited text", '\t'),
        new SpreadsheetXmlFileAdapter(),
        new NativeJsonAdapter(),
    ];
}
