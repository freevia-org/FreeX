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

        return SaveWorkbook(workbook);
    }

    public static MemoryStream SaveWorkbook(Workbook workbook)
    {
        var package = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, package);
        package.Position = 0;
        return package;
    }

    /// <summary>
    /// Saves <paramref name="workbook"/> via a fresh <see cref="XlsxFileAdapter"/> and
    /// returns the resulting bytes.  Use this overload when the test does not need
    /// a specific adapter instance.
    /// </summary>
    public static byte[] SaveToBytes(Workbook workbook)
    {
        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        return stream.ToArray();
    }

    /// <summary>
    /// Saves <paramref name="workbook"/> via the supplied <paramref name="adapter"/> and
    /// returns the resulting bytes.  Use this overload when the test controls the adapter
    /// (e.g. to test non-XLSX adapters or adapter-specific settings).
    /// </summary>
    public static byte[] SaveToBytes(IFileAdapter adapter, Workbook workbook)
    {
        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);
        return stream.ToArray();
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
        return XlsxPackageTestFixtures.LoadPackageXml(archive, path, path);
    }

    public static void PatchWorksheetXml(MemoryStream package, Action<XDocument> patchDocument) =>
        PatchPackageXml(package, DefaultWorksheetPath, patchDocument);

    public static void PatchPackageXml(MemoryStream package, string path, Action<XDocument> patchDocument)
    {
        package.Position = 0;
        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = archive.GetEntry(path)!;
            var document = XlsxPackageTestFixtures.LoadPackageXml(entry);

            patchDocument(document);

            entry.Delete();
            var replacement = archive.CreateEntry(path);
            using var writer = new StreamWriter(replacement.Open());
            document.Save(writer);
        }

        package.Position = 0;
    }
}
