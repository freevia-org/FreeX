using Freexcel.Core.Model;

namespace Freexcel.App.Host;

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
            "Freexcel",
            "Freexcel workbook export",
            "Freexcel, spreadsheet");
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
