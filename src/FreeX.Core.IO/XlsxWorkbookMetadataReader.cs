using FreeX.Core.Model;
using System.IO.Compression;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxWorkbookMetadataReader
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    public static XlsxWorkbookMetadataSnapshot LoadWorkbookMetadata(Stream xlsxStream)
    {
        try
        {
            using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Read, leaveOpen: true);
            return LoadWorkbookMetadata(archive);
        }
        catch
        {
            return XlsxWorkbookMetadataSnapshot.Default;
        }
    }

    internal static XlsxWorkbookMetadataSnapshot LoadWorkbookMetadata(ZipArchive archive)
    {
        try
        {
            var workbookEntry = archive.GetEntry("xl/workbook.xml");
            if (workbookEntry is null)
                return XlsxWorkbookMetadataSnapshot.Default;

            var workbookXml = LoadXml(workbookEntry);
            return LoadWorkbookMetadata(workbookXml);
        }
        catch
        {
            return XlsxWorkbookMetadataSnapshot.Default;
        }
    }

    private static XlsxWorkbookMetadataSnapshot LoadWorkbookMetadata(XDocument workbookXml) =>
        new(
            ReadOrDefault(() => LoadUses1904DateSystem(workbookXml), false),
            ReadOrDefault(() => LoadShowInkAnnotations(workbookXml), true),
            ReadOrDefault(() => LoadWorkbookProperties(workbookXml), (NativeXmlPreserveBag?)null),
            ReadOrDefault(() => LoadWorkbookViewProperties(workbookXml), WorkbookViewProperties.Empty),
            ReadOrDefault(() => LoadFileSharing(workbookXml), (WorkbookFileSharingModel?)null),
            ReadOrDefault(() => LoadFileRecoveryProperties(workbookXml), []),
            ReadOrDefault(() => LoadFileVersion(workbookXml), (WorkbookFileVersionModel?)null),
            ReadOrDefault(() => LoadFunctionGroups(workbookXml), (WorkbookFunctionGroupsModel?)null),
            ReadOrDefault(() => LoadSmartTags(workbookXml), (WorkbookSmartTagMetadataModel?)null),
            ReadOrDefault(() => XlsxWorkbookAdditionalViewMapper.Read(workbookXml), (WorkbookAdditionalViewsModel?)null),
            ReadOrDefault(() => LoadProtection(workbookXml), WorkbookProtectionState.None),
            ReadOrDefault(() => LoadProtectionMetadata(workbookXml), (NativeXmlPreserveBag?)null),
            ReadOrDefault(() => LoadCalculationProperties(workbookXml), WorkbookCalculationProperties.Default),
            ReadOrDefault(() => LoadCustomViews(workbookXml), []));

    private static T ReadOrDefault<T>(Func<T> read, T fallback)
    {
        try
        {
            return read();
        }
        catch
        {
            return fallback;
        }
    }

    public static Dictionary<int, string> LoadNumberFormatCatalog(Stream xlsxStream)
    {
        var stylesXml = XlsxStylesheetReader.Load(xlsxStream);
        return LoadNumberFormatCatalog(stylesXml);
    }

    public static Dictionary<int, string> LoadNumberFormatCatalog(XDocument? stylesXml)
    {
        try
        {
            if (stylesXml?.Root is null)
                return [];

            var result = new Dictionary<int, string>();
            foreach (var format in stylesXml.Root
                         .Element(WorkbookNs + "numFmts")?
                         .Elements(WorkbookNs + "numFmt") ?? [])
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

            var isStructureProtected = XlsxXmlAttributeReader.ReadBoolAttribute(protection, "lockStructure");
            var passwordHash = ReadWorkbookPasswordHash(protection);

            if (!isStructureProtected && passwordHash is null)
                return WorkbookProtectionState.None;

            return new WorkbookProtectionState(isStructureProtected, passwordHash);
        }
        catch
        {
            return WorkbookProtectionState.None;
        }
    }

    public static NativeXmlPreserveBag? LoadProtectionMetadata(Stream xlsxStream)
    {
        try
        {
            using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Read, leaveOpen: true);
            var workbookEntry = archive.GetEntry("xl/workbook.xml");
            if (workbookEntry is null)
                return null;

            var workbookXml = LoadXml(workbookEntry);
            return LoadProtectionMetadata(workbookXml);
        }
        catch
        {
            return null;
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

    public static NativeXmlPreserveBag? LoadWorkbookProperties(Stream xlsxStream)
    {
        try
        {
            using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Read, leaveOpen: true);
            var workbookEntry = archive.GetEntry("xl/workbook.xml");
            if (workbookEntry is null)
                return null;

            var workbookXml = LoadXml(workbookEntry);
            return LoadWorkbookProperties(workbookXml);
        }
        catch
        {
            return null;
        }
    }

    public static WorkbookViewProperties LoadWorkbookViewProperties(Stream xlsxStream)
    {
        try
        {
            using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Read, leaveOpen: true);
            var workbookEntry = archive.GetEntry("xl/workbook.xml");
            if (workbookEntry is null)
                return WorkbookViewProperties.Empty;

            var workbookXml = LoadXml(workbookEntry);
            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var primaryView = FindPrimaryWorkbookView(workbookXml, workbookNs);

            if (primaryView is null)
                return WorkbookViewProperties.Empty;

            return new WorkbookViewProperties(
                XlsxXmlAttributeReader.ReadNullableBoolAttribute(primaryView, "showSheetTabs"),
                XlsxXmlAttributeReader.ReadIntAttribute(primaryView, "tabRatio"),
                XlsxXmlAttributeReader.ReadIntAttribute(primaryView, "firstSheet"),
                XlsxXmlAttributeReader.ReadIntAttribute(primaryView, "activeTab"));
        }
        catch
        {
            return WorkbookViewProperties.Empty;
        }
    }

    public static WorkbookFileSharingModel? LoadFileSharing(Stream xlsxStream)
    {
        try
        {
            using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Read, leaveOpen: true);
            var workbookEntry = archive.GetEntry("xl/workbook.xml");
            if (workbookEntry is null)
                return null;

            var workbookXml = LoadXml(workbookEntry);
            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var fileSharing = workbookXml.Root?.Element(workbookNs + "fileSharing");
            if (fileSharing is null)
                return null;

            return new WorkbookFileSharingModel
            {
                ReadOnlyRecommended = XlsxXmlAttributeReader.ReadNullableBoolAttribute(fileSharing, "readOnlyRecommended"),
                UserName = fileSharing.Attribute("userName")?.Value,
                ReservationPassword = fileSharing.Attribute("reservationPassword")?.Value
            };
        }
        catch
        {
            return null;
        }
    }

    public static List<WorkbookFileRecoveryPropertiesModel> LoadFileRecoveryProperties(Stream xlsxStream)
    {
        try
        {
            using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Read, leaveOpen: true);
            var workbookEntry = archive.GetEntry("xl/workbook.xml");
            if (workbookEntry is null)
                return [];

            var workbookXml = LoadXml(workbookEntry);
            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            return workbookXml.Root?
                .Elements(workbookNs + "fileRecoveryPr")
                .Select(XlsxWorkbookMetadataMapper.ToFileRecoveryProperties)
                .ToList() ?? [];
        }
        catch
        {
            return [];
        }
    }

    public static WorkbookFileVersionModel? LoadFileVersion(Stream xlsxStream)
    {
        try
        {
            using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Read, leaveOpen: true);
            var workbookEntry = archive.GetEntry("xl/workbook.xml");
            if (workbookEntry is null)
                return null;

            var workbookXml = LoadXml(workbookEntry);
            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var fileVersion = workbookXml.Root?.Element(workbookNs + "fileVersion");
            return fileVersion is null ? null : XlsxWorkbookMetadataMapper.ToFileVersion(fileVersion);
        }
        catch
        {
            return null;
        }
    }

    public static WorkbookFunctionGroupsModel? LoadFunctionGroups(Stream xlsxStream)
    {
        try
        {
            using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Read, leaveOpen: true);
            var workbookEntry = archive.GetEntry("xl/workbook.xml");
            if (workbookEntry is null)
                return null;

            var workbookXml = LoadXml(workbookEntry);
            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var functionGroups = workbookXml.Root?.Element(workbookNs + "functionGroups");
            return functionGroups is null ? null : XlsxWorkbookMetadataMapper.ToFunctionGroups(functionGroups, workbookNs);
        }
        catch
        {
            return null;
        }
    }

    public static WorkbookSmartTagMetadataModel? LoadSmartTags(Stream xlsxStream)
    {
        try
        {
            using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Read, leaveOpen: true);
            var workbookEntry = archive.GetEntry("xl/workbook.xml");
            if (workbookEntry is null)
                return null;

            var workbookXml = LoadXml(workbookEntry);
            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var smartTagProperties = workbookXml.Root?.Element(workbookNs + "smartTagPr");
            var smartTagTypes = workbookXml.Root?.Element(workbookNs + "smartTagTypes");
            if (smartTagProperties is null && smartTagTypes is null)
                return null;

            return XlsxWorkbookMetadataMapper.ToSmartTags(smartTagProperties, smartTagTypes, workbookNs);
        }
        catch
        {
            return null;
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
                : string.Equals(calcPr.Attribute("calcMode")?.Value, "autoNoTable", StringComparison.OrdinalIgnoreCase)
                    ? WorkbookCalculationMode.AutomaticExceptDataTables
                    : string.Equals(calcPr.Attribute("calcMode")?.Value, "auto", StringComparison.OrdinalIgnoreCase)
                        ? WorkbookCalculationMode.Automatic
                        : (WorkbookCalculationMode?)null;

            return new WorkbookCalculationProperties(
                mode,
                XlsxXmlAttributeReader.ReadBoolAttribute(calcPr, "fullCalcOnLoad"),
                XlsxXmlAttributeReader.ReadBoolAttribute(calcPr, "forceFullCalc"),
                XlsxXmlAttributeReader.ReadBoolAttribute(calcPr, "iterate"),
                XlsxXmlAttributeReader.ReadIntAttribute(calcPr, "iterateCount"),
                XlsxXmlAttributeReader.ReadDoubleAttribute(calcPr, "iterateDelta"),
                XlsxXmlAttributeReader.ReadBoolAttribute(calcPr, "fullPrecision", defaultValue: true));
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
            var sheetIdToIndex = XlsxWorkbookMetadataMapper.BuildSheetIdToIndexMap(
                workbookXml.Root?.Element(workbookNs + "sheets"), workbookNs);
            foreach (var view in workbookXml.Root?
                         .Element(workbookNs + "customWorkbookViews")?
                         .Elements(workbookNs + "customWorkbookView") ?? [])
            {
                var customView = XlsxWorkbookMetadataMapper.ToCustomView(view, sheetIdToIndex);
                if (!string.IsNullOrWhiteSpace(customView.Id) && !string.IsNullOrWhiteSpace(customView.Name))
                    views.Add(customView);
            }
        }
        catch
        {
            // Custom views are best-effort; ClosedXML still loads workbook content.
        }

        return views;
    }

    private static WorkbookProtectionState LoadProtection(XDocument workbookXml)
    {
        var protection = workbookXml.Root?.Element(WorkbookNs + "workbookProtection");
        if (protection is null)
            return WorkbookProtectionState.None;

        var isStructureProtected = XlsxXmlAttributeReader.ReadBoolAttribute(protection, "lockStructure");
        var passwordHash = ReadWorkbookPasswordHash(protection);

        // A workbookPassword can legitimately be present even when lockStructure is absent -- e.g.
        // Excel's Protect Workbook dialog with only "Windows" checked still writes the password the
        // user typed. Dropping it here (as if the element carried nothing worth keeping) would lose
        // it permanently on the next full rebuild save, since ApplyProtection only ever re-emits the
        // password from this state's PasswordHash. Only collapse to None when there is truly nothing
        // to preserve (no structure lock and no password).
        if (!isStructureProtected && passwordHash is null)
            return WorkbookProtectionState.None;

        return new WorkbookProtectionState(isStructureProtected, passwordHash);
    }

    /// <summary>
    /// Reads the legacy 4-hex <c>workbookPassword</c> attribute when present, otherwise falls back to
    /// the modern ISO 29500 salted/iterated hash (<c>algorithmName</c>/<c>hashValue</c>/<c>saltValue</c>/
    /// <c>spinCount</c>) Excel writes by default since Excel 2013 — encoded so
    /// <see cref="ProtectionPasswordHelper.VerifyStoredPassword"/> can verify against it. Returns null
    /// when neither scheme is present (structure locked with no password at all).
    /// <para>
    /// The legacy attribute is only accepted when it has the exact 4-hex-digit shape Excel always
    /// writes (<see cref="ProtectionPasswordHelper.IsLegacyPasswordHash"/>). A malformed value (e.g. a
    /// hand-edited or corrupted file) is never a real Excel-authored hash; accepting it verbatim here
    /// would let it flow into <see cref="ProtectionPasswordHelper.ToLegacyPasswordHash"/> at save time,
    /// which -- unable to tell a stored hash from a freshly-typed password -- would re-hash the garbage
    /// into a plausible-looking 4-hex value instead of the schema normalizer having a chance to strip
    /// it, silently laundering invalid data into a fake-valid password on round-trip.
    /// </para>
    /// </summary>
    private static string? ReadWorkbookPasswordHash(XElement protection)
    {
        var legacyPassword = protection.Attribute("workbookPassword")?.Value;
        if (!string.IsNullOrEmpty(legacyPassword) && ProtectionPasswordHelper.IsLegacyPasswordHash(legacyPassword))
            return legacyPassword;

        var hashValue = protection.Attribute("workbookHashValue")?.Value;
        if (string.IsNullOrEmpty(hashValue))
            return null;

        return ProtectionPasswordHelper.EncodeIso29500Hash(
            protection.Attribute("workbookAlgorithmName")?.Value,
            protection.Attribute("workbookSpinCount")?.Value,
            protection.Attribute("workbookSaltValue")?.Value,
            hashValue);
    }

    private static NativeXmlPreserveBag? LoadProtectionMetadata(XDocument workbookXml)
        => ReadNativeBag(
            workbookXml.Root?.Element(WorkbookNs + "workbookProtection"),
            "workbookProtection",
            attribute =>
                !string.Equals(attribute.Name.LocalName, "lockStructure", StringComparison.Ordinal) &&
                !string.Equals(attribute.Name.LocalName, "workbookPassword", StringComparison.Ordinal));

    private static bool LoadUses1904DateSystem(XDocument workbookXml) =>
        XlsxXmlAttributeReader.ReadBoolAttribute(
            workbookXml.Root?.Element(WorkbookNs + "workbookPr"),
            "date1904");

    private static bool LoadShowInkAnnotations(XDocument workbookXml)
    {
        var workbookProperties = workbookXml.Root?.Element(WorkbookNs + "workbookPr");
        return workbookProperties?.Attribute("showInkAnnotation") is null ||
               XlsxXmlAttributeReader.ReadBoolAttribute(workbookProperties, "showInkAnnotation");
    }

    private static NativeXmlPreserveBag? LoadWorkbookProperties(XDocument workbookXml)
        => ReadNativeBag(
            workbookXml.Root?.Element(WorkbookNs + "workbookPr"),
            "workbookPr",
            attribute => !string.Equals(attribute.Name.LocalName, "date1904", StringComparison.Ordinal));

    private static NativeXmlPreserveBag? ReadNativeBag(
        XElement? element,
        string bagName,
        Func<XAttribute, bool> shouldPreserveAttribute)
    {
        if (element is null)
            return null;

        var attrs = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var attribute in element.Attributes())
        {
            if (attribute.IsNamespaceDeclaration || !shouldPreserveAttribute(attribute))
                continue;

            attrs[attribute.Name.ToString()] = attribute.Value;
        }

        var children = element.Elements()
            .Select(child => child.ToString(SaveOptions.DisableFormatting))
            .ToList();

        var serialized = XmlNativeBagSerializer.Serialize(attrs, children);
        if (serialized is null)
            return null;

        var bag = new NativeXmlPreserveBag();
        bag.Set(bagName, serialized);
        return bag;
    }

    private static WorkbookViewProperties LoadWorkbookViewProperties(XDocument workbookXml)
    {
        var primaryView = FindPrimaryWorkbookView(workbookXml, WorkbookNs);

        if (primaryView is null)
            return WorkbookViewProperties.Empty;

        return new WorkbookViewProperties(
            XlsxXmlAttributeReader.ReadNullableBoolAttribute(primaryView, "showSheetTabs"),
            XlsxXmlAttributeReader.ReadIntAttribute(primaryView, "tabRatio"),
            XlsxXmlAttributeReader.ReadIntAttribute(primaryView, "firstSheet"),
            XlsxXmlAttributeReader.ReadIntAttribute(primaryView, "activeTab"));
    }

    private static XElement? FindPrimaryWorkbookView(XDocument workbookXml, XNamespace workbookNs)
    {
        var bookViews = workbookXml.Root?.Element(workbookNs + "bookViews");
        if (bookViews is null)
            return null;

        // OOXML/Excel treats the first <workbookView> under <bookViews> as authoritative
        // (mirrors the writer's FindFirstWorkbookView contract). Do not prefer a later
        // zero/absent-activeTab entry -- that would silently discard the real saved
        // selection whenever an earlier view has a genuine non-zero activeTab.
        foreach (var view in bookViews.Elements(workbookNs + "workbookView"))
        {
            return view;
        }

        return null;
    }

    private static WorkbookFileSharingModel? LoadFileSharing(XDocument workbookXml)
    {
        var fileSharing = workbookXml.Root?.Element(WorkbookNs + "fileSharing");
        if (fileSharing is null)
            return null;

        return new WorkbookFileSharingModel
        {
            ReadOnlyRecommended = XlsxXmlAttributeReader.ReadNullableBoolAttribute(fileSharing, "readOnlyRecommended"),
            UserName = fileSharing.Attribute("userName")?.Value,
            ReservationPassword = fileSharing.Attribute("reservationPassword")?.Value
        };
    }

    private static List<WorkbookFileRecoveryPropertiesModel> LoadFileRecoveryProperties(XDocument workbookXml) =>
        workbookXml.Root?
            .Elements(WorkbookNs + "fileRecoveryPr")
            .Select(XlsxWorkbookMetadataMapper.ToFileRecoveryProperties)
            .ToList() ?? [];

    private static WorkbookFileVersionModel? LoadFileVersion(XDocument workbookXml)
    {
        var fileVersion = workbookXml.Root?.Element(WorkbookNs + "fileVersion");
        return fileVersion is null ? null : XlsxWorkbookMetadataMapper.ToFileVersion(fileVersion);
    }

    private static WorkbookFunctionGroupsModel? LoadFunctionGroups(XDocument workbookXml)
    {
        var functionGroups = workbookXml.Root?.Element(WorkbookNs + "functionGroups");
        return functionGroups is null ? null : XlsxWorkbookMetadataMapper.ToFunctionGroups(functionGroups, WorkbookNs);
    }

    private static WorkbookSmartTagMetadataModel? LoadSmartTags(XDocument workbookXml)
    {
        var smartTagProperties = workbookXml.Root?.Element(WorkbookNs + "smartTagPr");
        var smartTagTypes = workbookXml.Root?.Element(WorkbookNs + "smartTagTypes");
        if (smartTagProperties is null && smartTagTypes is null)
            return null;

        return XlsxWorkbookMetadataMapper.ToSmartTags(smartTagProperties, smartTagTypes, WorkbookNs);
    }

    private static WorkbookCalculationProperties LoadCalculationProperties(XDocument workbookXml)
    {
        var calcPr = workbookXml.Root?.Element(WorkbookNs + "calcPr");
        if (calcPr is null)
            return WorkbookCalculationProperties.Default;

        var mode = string.Equals(calcPr.Attribute("calcMode")?.Value, "manual", StringComparison.OrdinalIgnoreCase)
            ? WorkbookCalculationMode.Manual
            : string.Equals(calcPr.Attribute("calcMode")?.Value, "autoNoTable", StringComparison.OrdinalIgnoreCase)
                ? WorkbookCalculationMode.AutomaticExceptDataTables
                : string.Equals(calcPr.Attribute("calcMode")?.Value, "auto", StringComparison.OrdinalIgnoreCase)
                    ? WorkbookCalculationMode.Automatic
                    : (WorkbookCalculationMode?)null;

        return new WorkbookCalculationProperties(
            mode,
            XlsxXmlAttributeReader.ReadBoolAttribute(calcPr, "fullCalcOnLoad"),
            XlsxXmlAttributeReader.ReadBoolAttribute(calcPr, "forceFullCalc"),
            XlsxXmlAttributeReader.ReadBoolAttribute(calcPr, "iterate"),
            XlsxXmlAttributeReader.ReadIntAttribute(calcPr, "iterateCount"),
            XlsxXmlAttributeReader.ReadDoubleAttribute(calcPr, "iterateDelta"),
            XlsxXmlAttributeReader.ReadBoolAttribute(calcPr, "fullPrecision", defaultValue: true));
    }

    private static IReadOnlyList<XlsxWorkbookCustomView> LoadCustomViews(XDocument workbookXml)
    {
        var views = new List<XlsxWorkbookCustomView>();
        var sheetIdToIndex = XlsxWorkbookMetadataMapper.BuildSheetIdToIndexMap(
            workbookXml.Root?.Element(WorkbookNs + "sheets"), WorkbookNs);
        foreach (var view in workbookXml.Root?
                     .Element(WorkbookNs + "customWorkbookViews")?
                     .Elements(WorkbookNs + "customWorkbookView") ?? [])
        {
            var customView = XlsxWorkbookMetadataMapper.ToCustomView(view, sheetIdToIndex);
            if (!string.IsNullOrWhiteSpace(customView.Id) && !string.IsNullOrWhiteSpace(customView.Name))
                views.Add(customView);
        }

        return views;
    }

    private static XDocument LoadXml(ZipArchiveEntry entry)
    {
        return XlsxPackageXmlEditor.LoadXml(entry);
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
    double? MaxChange,
    bool FullPrecision)
{
    public static WorkbookCalculationProperties Default { get; } = new(null, false, false, false, null, null, true);
}

internal sealed record WorkbookViewProperties(
    bool? ShowSheetTabs,
    int? SheetTabRatio,
    int? FirstVisibleSheetIndex,
    int? ActiveSheetIndex)
{
    public static WorkbookViewProperties Empty { get; } = new(null, null, null, null);
}

internal sealed record XlsxWorkbookMetadataSnapshot(
    bool Uses1904DateSystem,
    bool ShowInkAnnotations,
    NativeXmlPreserveBag? WorkbookProperties,
    WorkbookViewProperties WorkbookViewProperties,
    WorkbookFileSharingModel? FileSharing,
    IReadOnlyList<WorkbookFileRecoveryPropertiesModel> FileRecoveryProperties,
    WorkbookFileVersionModel? FileVersion,
    WorkbookFunctionGroupsModel? FunctionGroups,
    WorkbookSmartTagMetadataModel? SmartTags,
    WorkbookAdditionalViewsModel? AdditionalViews,
    WorkbookProtectionState Protection,
    NativeXmlPreserveBag? ProtectionMetadata,
    WorkbookCalculationProperties CalculationProperties,
    IReadOnlyList<XlsxWorkbookCustomView> CustomViews)
{
    public static XlsxWorkbookMetadataSnapshot Default { get; } = new(
        false,
        true,
        null,
        WorkbookViewProperties.Empty,
        null,
        [],
        null,
        null,
        null,
        null,
        WorkbookProtectionState.None,
        null,
        WorkbookCalculationProperties.Default,
        []);
}

internal sealed record XlsxWorkbookCustomView(
    string Id,
    string Name,
    bool IncludePrintSettings = true,
    bool IncludeHiddenRowsColumnsAndFilterSettings = true,
    int? ActiveSheetIndex = null);

