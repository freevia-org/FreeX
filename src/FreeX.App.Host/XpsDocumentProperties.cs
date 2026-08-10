using System.IO.Packaging;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Host;

internal sealed record XpsDocumentProperties(
    string? Title,
    string? Creator,
    string? Subject,
    string? Keywords)
{
    public static XpsDocumentProperties? FromWorkbook(Workbook workbook, ExportOptions options)
    {
        if (ExportDocumentPropertiesPlanner.FromWorkbook(workbook, options) is not { } properties)
            return null;

        return new XpsDocumentProperties(
            properties.Title,
            properties.Creator,
            properties.Subject,
            properties.Keywords);
    }

    public static void ApplyToPackage(Package package, XpsDocumentProperties? properties)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (properties is null)
            return;

        package.PackageProperties.Title = ExportDocumentPropertiesPlanner.Normalize(properties.Title);
        package.PackageProperties.Creator = ExportDocumentPropertiesPlanner.Normalize(properties.Creator);
        package.PackageProperties.Subject = ExportDocumentPropertiesPlanner.Normalize(properties.Subject);
        package.PackageProperties.Keywords = ExportDocumentPropertiesPlanner.Normalize(properties.Keywords);
    }
}
