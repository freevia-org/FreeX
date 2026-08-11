using System.IO.Packaging;
using FreeX.App.Services;

namespace FreeX.App.Host;

internal static class XpsPackagePropertiesAdapter
{
    public static void Apply(Package package, ExportDocumentProperties? properties)
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
