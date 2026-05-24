using Freexcel.Core.Model;
using System.IO.Compression;
using System.Xml.Linq;

namespace Freexcel.Core.IO;

internal static class XlsxWorkbookMetadataReader
{
    public static Dictionary<int, string> LoadNumberFormatCatalog(Stream xlsxStream)
    {
        try
        {
            using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Read, leaveOpen: true);
            var stylesEntry = archive.GetEntry("xl/styles.xml");
            if (stylesEntry is null)
                return [];

            var stylesXml = LoadXml(stylesEntry);
            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var result = new Dictionary<int, string>();
            foreach (var format in stylesXml.Root?
                         .Element(workbookNs + "numFmts")?
                         .Elements(workbookNs + "numFmt") ?? [])
            {
                var id = XlsxXmlAttributeReader.ReadIntAttribute(format, "numFmtId");
                var code = format.Attribute("formatCode")?.Value;
                if (id is >= 164 && !string.IsNullOrWhiteSpace(code))
                    result[id.Value] = code;
            }

            return result;
        }
        catch
        {
            return [];
        }
    }

    public static WorkbookProtectionState LoadProtection(Stream xlsxStream)
    {
        try
        {
            using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Read, leaveOpen: true);
            var workbookEntry = archive.GetEntry("xl/workbook.xml");
            if (workbookEntry is null)
                return WorkbookProtectionState.None;

            var workbookXml = LoadXml(workbookEntry);
            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var protection = workbookXml.Root?.Element(workbookNs + "workbookProtection");
            if (protection is null)
                return WorkbookProtectionState.None;

            var isStructureProtected =
                XlsxXmlAttributeReader.ReadBoolAttribute(protection, "lockStructure") ||
                XlsxXmlAttributeReader.ReadBoolAttribute(protection, "lockWindows");

            if (!isStructureProtected)
                return WorkbookProtectionState.None;

            var passwordHash =
                protection.Attribute("workbookPassword")?.Value ??
                protection.Attribute("revisionsPassword")?.Value;

            return new WorkbookProtectionState(true, passwordHash);
        }
        catch
        {
            return WorkbookProtectionState.None;
        }
    }

    public static bool LoadUses1904DateSystem(Stream xlsxStream)
    {
        try
        {
            using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Read, leaveOpen: true);
            var workbookEntry = archive.GetEntry("xl/workbook.xml");
            if (workbookEntry is null)
                return false;

            var workbookXml = LoadXml(workbookEntry);
            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            return XlsxXmlAttributeReader.ReadBoolAttribute(
                workbookXml.Root?.Element(workbookNs + "workbookPr"),
                "date1904");
        }
        catch
        {
            return false;
        }
    }

    public static WorkbookCalculationProperties LoadCalculationProperties(Stream xlsxStream)
    {
        try
        {
            using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Read, leaveOpen: true);
            var workbookEntry = archive.GetEntry("xl/workbook.xml");
            if (workbookEntry is null)
                return WorkbookCalculationProperties.Default;

            var workbookXml = LoadXml(workbookEntry);
            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var calcPr = workbookXml.Root?.Element(workbookNs + "calcPr");
            if (calcPr is null)
                return WorkbookCalculationProperties.Default;

            var mode = string.Equals(calcPr.Attribute("calcMode")?.Value, "manual", StringComparison.OrdinalIgnoreCase)
                ? WorkbookCalculationMode.Manual
                : string.Equals(calcPr.Attribute("calcMode")?.Value, "auto", StringComparison.OrdinalIgnoreCase) ||
                  string.Equals(calcPr.Attribute("calcMode")?.Value, "autoNoTable", StringComparison.OrdinalIgnoreCase)
                    ? WorkbookCalculationMode.Automatic
                    : (WorkbookCalculationMode?)null;

            return new WorkbookCalculationProperties(
                mode,
                XlsxXmlAttributeReader.ReadBoolAttribute(calcPr, "fullCalcOnLoad"),
                XlsxXmlAttributeReader.ReadBoolAttribute(calcPr, "forceFullCalc"),
                XlsxXmlAttributeReader.ReadBoolAttribute(calcPr, "iterate"),
                XlsxXmlAttributeReader.ReadIntAttribute(calcPr, "iterateCount"),
                XlsxXmlAttributeReader.ReadDoubleAttribute(calcPr, "iterateDelta"));
        }
        catch
        {
            return WorkbookCalculationProperties.Default;
        }
    }

    public static IReadOnlyList<XlsxWorkbookCustomView> LoadCustomViews(Stream xlsxStream)
    {
        var views = new List<XlsxWorkbookCustomView>();

        try
        {
            using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Read, leaveOpen: true);
            var workbookEntry = archive.GetEntry("xl/workbook.xml");
            if (workbookEntry is null)
                return views;

            var workbookXml = LoadXml(workbookEntry);
            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            foreach (var view in workbookXml.Root?
                         .Element(workbookNs + "customWorkbookViews")?
                         .Elements(workbookNs + "customWorkbookView") ?? [])
            {
                var id = view.Attribute("guid")?.Value;
                var name = view.Attribute("name")?.Value;
                if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(name))
                    views.Add(new XlsxWorkbookCustomView(
                        id,
                        name,
                        XlsxXmlAttributeReader.ReadBoolAttribute(view, "includePrintSettings", defaultValue: true),
                        XlsxXmlAttributeReader.ReadBoolAttribute(view, "includeHiddenRowCol", defaultValue: true)));
            }
        }
        catch
        {
            // Custom views are best-effort; ClosedXML still loads workbook content.
        }

        return views;
    }

    private static XDocument LoadXml(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

}

internal sealed record WorkbookProtectionState(bool IsStructureProtected, string? PasswordHash)
{
    public static WorkbookProtectionState None { get; } = new(false, null);
}

internal sealed record WorkbookCalculationProperties(
    WorkbookCalculationMode? Mode,
    bool FullCalculationOnLoad,
    bool ForceFullCalculation,
    bool IterativeCalculation,
    int? MaxIterations,
    double? MaxChange)
{
    public static WorkbookCalculationProperties Default { get; } = new(null, false, false, false, null, null);
}

internal sealed record XlsxWorkbookCustomView(
    string Id,
    string Name,
    bool IncludePrintSettings = true,
    bool IncludeHiddenRowsColumnsAndFilterSettings = true);

