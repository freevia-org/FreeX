using System.IO.Packaging;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

internal sealed record XpsDocumentProperties(
    string? Title,
    string? Creator,
    string? Subject,
    string? Keywords)
{
    public static XpsDocumentProperties? FromWorkbook(Workbook workbook, ExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(workbook);

        if (!options.IncludeDocumentProperties)
            return null;

        return new XpsDocumentProperties(
            Normalize(workbook.Name),
            "Freexcel",
            "Freexcel workbook export",
            "Freexcel, spreadsheet");
    }

    public static void ApplyToPackage(Package package, XpsDocumentProperties? properties)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (properties is null)
            return;

        package.PackageProperties.Title = Normalize(properties.Title);
        package.PackageProperties.Creator = Normalize(properties.Creator);
        package.PackageProperties.Subject = Normalize(properties.Subject);
        package.PackageProperties.Keywords = Normalize(properties.Keywords);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
