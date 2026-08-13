using FreeX.Core.Model;

namespace FreeX.App.Services;

public sealed record ExportDocumentProperties(
    string? Title,
    string? Creator,
    string? Subject,
    string? Keywords);

public static class ExportDocumentPropertiesPlanner
{
    public const string DefaultCreator = "FreeX";
    public const string DefaultSubject = "FreeX workbook export";
    public const string DefaultKeywords = "FreeX, spreadsheet";

    public static ExportDocumentProperties? FromWorkbook(Workbook workbook, ExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(workbook);

        if (!options.IncludeDocumentProperties)
            return null;

        return new ExportDocumentProperties(
            Normalize(workbook.Name),
            Normalize(workbook.FileSharing?.UserName) ?? DefaultCreator,
            DefaultSubject,
            DefaultKeywords);
    }

    public static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
