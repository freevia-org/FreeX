using System.IO.Compression;
using System.Xml.Linq;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

internal static class XlsxHeaderFooterPictureReaderWriter
{
    public static bool HasPictures(Sheet sheet) =>
        XlsxHeaderFooterPicturePackagePlanner.HasPictures(sheet);

    public static XlsxHeaderFooterPictureSets Read(
        ZipArchive archive,
        string worksheetPath,
        XDocument worksheetXml) =>
        XlsxHeaderFooterPicturePackageReader.Read(archive, worksheetPath, worksheetXml);

    public static IReadOnlySet<string> FindSheetsWithUnchangedSourcePictures(Stream xlsxStream, Workbook workbook) =>
        XlsxHeaderFooterPicturePackageWriter.FindSheetsWithUnchangedSourcePictures(xlsxStream, workbook);

    public static void Save(Stream xlsxStream, Workbook workbook, IReadOnlySet<string>? sheetsToPreserve = null) =>
        XlsxHeaderFooterPicturePackageWriter.Save(xlsxStream, workbook, sheetsToPreserve);

    public static void RemoveClearedPictures(Stream xlsxStream, Workbook workbook) =>
        XlsxHeaderFooterPicturePackageWriter.RemoveClearedPictures(xlsxStream, workbook);
}
