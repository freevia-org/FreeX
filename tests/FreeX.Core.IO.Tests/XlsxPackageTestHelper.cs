using System.IO.Compression;
using System.Xml.Linq;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

internal static class XlsxPackageTestHelper
{
    private const string DefaultWorksheetPath = "xl/worksheets/sheet1.xml";

    public static MemoryStream CreateSingleCellWorkbookPackage()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));

        var package = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, package);
        package.Position = 0;
        return package;
    }

    public static MemoryStream CreatePackageWithPatchedWorksheet(Action<XElement> patchRoot)
    {
        var package = CreateSingleCellWorkbookPackage();
        PatchPackageXml(package, DefaultWorksheetPath, document => patchRoot(document.Root!));
        return package;
    }

    public static XDocument ReadWorksheetXml(MemoryStream package) =>
        ReadPackageXml(package, DefaultWorksheetPath);

    public static XDocument ReadPackageXml(MemoryStream package, string path)
    {
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        using var reader = new StreamReader(archive.GetEntry(path)!.Open());
        return XDocument.Load(reader);
    }

    public static void PatchWorksheetXml(MemoryStream package, Action<XDocument> patchDocument) =>
        PatchPackageXml(package, DefaultWorksheetPath, patchDocument);

    public static void PatchPackageXml(MemoryStream package, string path, Action<XDocument> patchDocument)
    {
        package.Position = 0;
        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = archive.GetEntry(path)!;
            XDocument document;
            using (var reader = new StreamReader(entry.Open()))
                document = XDocument.Load(reader);

            patchDocument(document);

            entry.Delete();
            var replacement = archive.CreateEntry(path);
            using var writer = new StreamWriter(replacement.Open());
            document.Save(writer);
        }

        package.Position = 0;
    }
}
