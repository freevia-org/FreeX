using FreeX.Core.Model;

namespace FreeX.App.Host;

internal sealed record PdfDocumentProperties(
    string? Title,
    string? Author,
    string? Subject,
    string? Keywords)
{
    public static PdfDocumentProperties? FromWorkbook(Workbook workbook, ExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(workbook);

        if (!options.IncludeDocumentProperties)
            return null;

        return new PdfDocumentProperties(
            Normalize(workbook.Name),
            "FreeX",
            "FreeX workbook export",
            "FreeX, spreadsheet");
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
