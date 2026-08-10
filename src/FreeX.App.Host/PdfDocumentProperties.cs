using FreeX.App.Services;
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
        if (ExportDocumentPropertiesPlanner.FromWorkbook(workbook, options) is not { } properties)
            return null;

        return new PdfDocumentProperties(
            properties.Title,
            properties.Creator,
            properties.Subject,
            properties.Keywords);
    }
}
