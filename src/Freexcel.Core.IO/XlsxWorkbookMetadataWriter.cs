using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

internal static class XlsxWorkbookMetadataWriter
{
    public static void SaveWorkbookProperties(Stream xlsxStream, Workbook workbook)
    {
        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Update, leaveOpen: true);
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        if (workbookEntry is null)
            return;

        var workbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var root = workbookXml.Root;
        if (root is null)
            return;

        var workbookProperties = root.Element(workbookNs + "workbookPr");
        if (workbookProperties is null)
        {
            if (!workbook.Uses1904DateSystem)
                return;

            workbookProperties = new XElement(workbookNs + "workbookPr");
            root.AddFirst(workbookProperties);
        }

        workbookProperties.SetAttributeValue("date1904", workbook.Uses1904DateSystem ? "1" : null);
        XlsxPackageXmlEditor.ReplaceXml(archive, "xl/workbook.xml", workbookXml);
    }

    public static void SaveWorkbookViewProperties(Stream xlsxStream, Workbook workbook)
    {
        if (workbook.ShowSheetTabs is null &&
            workbook.SheetTabRatio is null &&
            workbook.FirstVisibleSheetIndex is null &&
            workbook.ActiveSheetIndex is null)
        {
            return;
        }

        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Update, leaveOpen: true);
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        if (workbookEntry is null)
            return;

        var workbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var root = workbookXml.Root;
        if (root is null)
            return;

        var bookViews = root.Element(workbookNs + "bookViews");
        if (bookViews is null)
        {
            bookViews = new XElement(workbookNs + "bookViews");
            var sheets = root.Element(workbookNs + "sheets");
            if (sheets is not null)
                sheets.AddBeforeSelf(bookViews);
            else
                root.Add(bookViews);
        }

        var primaryView = bookViews.Elements(workbookNs + "workbookView").FirstOrDefault()
            ?? new XElement(workbookNs + "workbookView");
        if (primaryView.Parent is null)
            bookViews.AddFirst(primaryView);

        primaryView.SetAttributeValue("showSheetTabs", workbook.ShowSheetTabs is { } showSheetTabs ? showSheetTabs ? "1" : "0" : null);
        primaryView.SetAttributeValue("tabRatio", ClampWorkbookViewInteger(workbook.SheetTabRatio, 0, 1000));
        primaryView.SetAttributeValue("firstSheet", ClampWorkbookViewInteger(workbook.FirstVisibleSheetIndex, 0, Math.Max(0, workbook.Sheets.Count - 1)));
        primaryView.SetAttributeValue("activeTab", ClampWorkbookViewInteger(workbook.ActiveSheetIndex, 0, Math.Max(0, workbook.Sheets.Count - 1)));

        XlsxPackageXmlEditor.ReplaceXml(archive, "xl/workbook.xml", workbookXml);
    }

    public static void SaveFileSharing(Stream xlsxStream, Workbook workbook)
    {
        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Update, leaveOpen: true);
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        if (workbookEntry is null)
            return;

        var workbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var root = workbookXml.Root;
        if (root is null)
            return;

        var existingFileSharing = root.Element(workbookNs + "fileSharing");
        var fileSharing = existingFileSharing is not null
            ? new XElement(existingFileSharing)
            : new XElement(workbookNs + "fileSharing");
        existingFileSharing?.Remove();
        if (workbook.FileSharing is null)
        {
            XlsxPackageXmlEditor.ReplaceXml(archive, "xl/workbook.xml", workbookXml);
            return;
        }

        fileSharing.Attribute("readOnlyRecommended")?.Remove();
        fileSharing.Attribute("userName")?.Remove();
        fileSharing.Attribute("reservationPassword")?.Remove();
        fileSharing.SetAttributeValue(
            "readOnlyRecommended",
            workbook.FileSharing.ReadOnlyRecommended is { } readOnlyRecommended ? readOnlyRecommended ? "1" : "0" : null);
        fileSharing.SetAttributeValue(
            "userName",
            string.IsNullOrWhiteSpace(workbook.FileSharing.UserName) ? null : workbook.FileSharing.UserName);
        fileSharing.SetAttributeValue(
            "reservationPassword",
            string.IsNullOrWhiteSpace(workbook.FileSharing.ReservationPassword) ? null : workbook.FileSharing.ReservationPassword);

        var workbookProtection = root.Element(workbookNs + "workbookProtection");
        if (workbookProtection is not null)
            workbookProtection.AddBeforeSelf(fileSharing);
        else
        {
            var sheets = root.Element(workbookNs + "sheets");
            if (sheets is not null)
                sheets.AddBeforeSelf(fileSharing);
            else
                root.Add(fileSharing);
        }

        XlsxPackageXmlEditor.ReplaceXml(archive, "xl/workbook.xml", workbookXml);
    }

    public static void SaveFileRecoveryProperties(Stream xlsxStream, Workbook workbook)
    {
        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Update, leaveOpen: true);
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        if (workbookEntry is null)
            return;

        var workbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var root = workbookXml.Root;
        if (root is null)
            return;

        root.Elements(workbookNs + "fileRecoveryPr").Remove();
        if (workbook.FileRecoveryProperties.Count == 0)
        {
            XlsxPackageXmlEditor.ReplaceXml(archive, "xl/workbook.xml", workbookXml);
            return;
        }

        var recoveryElements = workbook.FileRecoveryProperties.Select(item =>
        {
            var element = new XElement(workbookNs + "fileRecoveryPr");
            foreach (var attribute in item.NativeAttributes)
            {
                if (!string.IsNullOrWhiteSpace(attribute.Key) &&
                    attribute.Key is not "autoRecover" and not "crashSave" and not "dataExtractLoad" and not "repairLoad")
                {
                    element.SetAttributeValue(XName.Get(attribute.Key), attribute.Value);
                }
            }

            SetBooleanAttribute(element, "autoRecover", item.AutoRecover);
            SetBooleanAttribute(element, "crashSave", item.CrashSave);
            SetBooleanAttribute(element, "dataExtractLoad", item.DataExtractLoad);
            SetBooleanAttribute(element, "repairLoad", item.RepairLoad);
            return element;
        }).ToArray();

        var extensionList = root.Element(workbookNs + "extLst");
        if (extensionList is not null)
            extensionList.AddBeforeSelf(recoveryElements);
        else
            root.Add(recoveryElements);

        XlsxPackageXmlEditor.ReplaceXml(archive, "xl/workbook.xml", workbookXml);

        static void SetBooleanAttribute(XElement element, string name, bool? value) =>
            element.SetAttributeValue(name, value is { } boolValue ? boolValue ? "1" : "0" : null);
    }

    public static void SaveFileVersion(Stream xlsxStream, Workbook workbook)
    {
        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Update, leaveOpen: true);
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        if (workbookEntry is null)
            return;

        var workbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var root = workbookXml.Root;
        if (root is null)
            return;

        root.Element(workbookNs + "fileVersion")?.Remove();
        if (workbook.FileVersion is null)
        {
            XlsxPackageXmlEditor.ReplaceXml(archive, "xl/workbook.xml", workbookXml);
            return;
        }

        var fileVersion = new XElement(workbookNs + "fileVersion");
        foreach (var attribute in workbook.FileVersion.NativeAttributes)
        {
            if (!string.IsNullOrWhiteSpace(attribute.Key) &&
                attribute.Key is not "appName" and not "lastEdited" and not "lowestEdited" and not "rupBuild" and not "codeName")
            {
                fileVersion.SetAttributeValue(XName.Get(attribute.Key), attribute.Value);
            }
        }

        fileVersion.SetAttributeValue("appName", NullIfWhiteSpace(workbook.FileVersion.AppName));
        fileVersion.SetAttributeValue("lastEdited", NullIfWhiteSpace(workbook.FileVersion.LastEdited));
        fileVersion.SetAttributeValue("lowestEdited", NullIfWhiteSpace(workbook.FileVersion.LowestEdited));
        fileVersion.SetAttributeValue("rupBuild", NullIfWhiteSpace(workbook.FileVersion.RupBuild));
        fileVersion.SetAttributeValue("codeName", NullIfWhiteSpace(workbook.FileVersion.CodeName));

        root.AddFirst(fileVersion);
        XlsxPackageXmlEditor.ReplaceXml(archive, "xl/workbook.xml", workbookXml);

        static string? NullIfWhiteSpace(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
    }

    public static void SaveProtection(Stream xlsxStream, Workbook workbook)
    {
        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Update, leaveOpen: true);
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        if (workbookEntry is null)
            return;

        var workbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var root = workbookXml.Root;
        if (root is null)
            return;

        root.Element(workbookNs + "workbookProtection")?.Remove();
        var protection = new XElement(workbookNs + "workbookProtection",
            new XAttribute("lockStructure", "1"));
        if (!string.IsNullOrWhiteSpace(workbook.StructureProtectionPassword))
            protection.SetAttributeValue("workbookPassword", ToLegacyPasswordHash(workbook.StructureProtectionPassword));

        var sheets = root.Element(workbookNs + "sheets");
        if (sheets is not null)
            sheets.AddBeforeSelf(protection);
        else
            root.Add(protection);

        XlsxPackageXmlEditor.ReplaceXml(archive, "xl/workbook.xml", workbookXml);
    }

    public static void SaveCalculationProperties(Stream xlsxStream, Workbook workbook)
    {
        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Update, leaveOpen: true);
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        if (workbookEntry is null)
            return;

        var workbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var root = workbookXml.Root;
        if (root is null)
            return;

        var calcPr = root.Element(workbookNs + "calcPr");
        if (calcPr is null)
        {
            calcPr = new XElement(workbookNs + "calcPr");
            root.Add(calcPr);
        }

        calcPr.SetAttributeValue("calcMode", workbook.CalculationMode == WorkbookCalculationMode.Manual ? "manual" : "auto");
        SetBooleanAttribute(calcPr, "fullCalcOnLoad", workbook.FullCalculationOnLoad);
        SetBooleanAttribute(calcPr, "forceFullCalc", workbook.ForceFullCalculation);
        SetBooleanAttribute(calcPr, "iterate", workbook.IterativeCalculation);
        calcPr.SetAttributeValue(
            "iterateCount",
            workbook.MaxCalculationIterations is { } maxIterations ? maxIterations.ToString(CultureInfo.InvariantCulture) : null);
        calcPr.SetAttributeValue(
            "iterateDelta",
            workbook.MaxCalculationChange is { } maxChange ? maxChange.ToString(CultureInfo.InvariantCulture) : null);

        XlsxPackageXmlEditor.ReplaceXml(archive, "xl/workbook.xml", workbookXml);

        static void SetBooleanAttribute(XElement element, string name, bool value) =>
            element.SetAttributeValue(name, value ? "1" : null);
    }

    private static string ToLegacyPasswordHash(string passwordOrHash)
    {
        if (IsLegacyPasswordHash(passwordOrHash))
            return passwordOrHash.ToUpperInvariant();

        var hash = 0;
        for (var i = 0; i < passwordOrHash.Length; i++)
        {
            var value = passwordOrHash[i] << (i + 1);
            var rotatedBits = value >> 15;
            value &= 0x7fff;
            hash ^= value | rotatedBits;
        }

        hash ^= passwordOrHash.Length;
        hash ^= 0xCE4B;
        return hash.ToString("X4", CultureInfo.InvariantCulture);
    }

    private static bool IsLegacyPasswordHash(string value) =>
        value.Length is > 0 and <= 4 &&
        value.All(ch =>
            ch is >= '0' and <= '9' ||
            ch is >= 'A' and <= 'F' ||
            ch is >= 'a' and <= 'f');

    private static int? ClampWorkbookViewInteger(int? value, int min, int max) =>
        value is { } intValue ? Math.Clamp(intValue, min, max) : null;
}
