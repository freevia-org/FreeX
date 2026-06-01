using System.IO.Compression;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxStylesheetReader
{
    public static XDocument? Load(Stream xlsxStream)
    {
        try
        {
            using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Read, leaveOpen: true);
            return Load(archive);
        }
        catch
        {
            return null;
        }
    }

    internal static XDocument? Load(ZipArchive archive)
    {
        try
        {
            var stylesEntry = archive.GetEntry("xl/styles.xml");
            return stylesEntry is null ? null : XlsxPackageXmlEditor.LoadXml(stylesEntry);
        }
        catch
        {
            return null;
        }
    }
}
