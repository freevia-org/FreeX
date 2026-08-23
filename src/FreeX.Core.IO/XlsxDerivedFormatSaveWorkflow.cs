using System.IO.Compression;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxDerivedFormatSaveWorkflow
{
    private const string WorkbookPartName = "/xl/workbook.xml";

    public static XlsxSaveResult Save(
        XlsxFileAdapter xlsx,
        Workbook workbook,
        Stream destination,
        string workbookContentType,
        bool preserveVbaProject,
        bool collectWarnings)
    {
        ArgumentNullException.ThrowIfNull(xlsx);
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentException.ThrowIfNullOrWhiteSpace(workbookContentType);

        using var package = new MemoryStream();
        var result = SavePackage(
            xlsx,
            workbook,
            package,
            preserveVbaProject,
            collectWarnings);

        package.Position = 0;
        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
            XlsxPackageXmlEditor.EnsureSpecificContentType(archive, WorkbookPartName, workbookContentType);

        package.Position = 0;
        SaveStreamPreparer.TruncateFromCurrentPosition(destination);
        package.CopyTo(destination);
        return result;
    }

    private static XlsxSaveResult SavePackage(
        XlsxFileAdapter xlsx,
        Workbook workbook,
        Stream package,
        bool preserveVbaProject,
        bool collectWarnings)
    {
        if (collectWarnings)
        {
            return preserveVbaProject
                ? xlsx.SaveWithWarningsPreservingVbaProject(workbook, package)
                : xlsx.SaveWithWarnings(workbook, package);
        }

        if (preserveVbaProject)
            xlsx.SavePreservingVbaProject(workbook, package);
        else
            xlsx.Save(workbook, package);

        return XlsxSaveResult.Clean;
    }
}
