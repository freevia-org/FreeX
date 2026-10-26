using FreeX.Core.IO;
using FreeX.Core.Model;
using FluentAssertions;
using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;

namespace FreeX.Core.IO.Tests;

public partial class XlsxCorpusRunnerTests
{
    private static void AssertExpectedPublicPackageTags(ManifestRow row, Stream package)
    {
        if (row.SourceType != "public")
            return;

        var tags = row.FeatureTags.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (!HasExpectedPublicPackageTags(row))
            return;

        var originalPosition = package.CanSeek ? package.Position : 0;
        if (package.CanSeek)
            package.Position = 0;

        try
        {
            using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
            if (tags.Contains("styles") || tags.Contains("formatting"))
                archive.GetEntry("xl/styles.xml").Should().NotBeNull(row.Id);

            if (tags.Contains("hyperlinks"))
                AssertPublicHyperlinkPackageGraph(archive, row.Id);

            if (tags.Contains("merged-cells"))
                PublicWorksheetElements(archive, "mergeCell").Should().NotBeEmpty(row.Id);

            if (tags.Contains("inline-strings"))
                PublicWorksheetCells(archive)
                    .Any(cell =>
                        string.Equals(cell.Attribute("t")?.Value, "inlineStr", StringComparison.Ordinal) ||
                        cell.Element(WorksheetNs + "is") is not null)
                    .Should()
                    .BeTrue(row.Id);

            if (tags.Contains("shared-string-package"))
                AssertPublicSharedStringPackageGraph(archive, row.Id);

            if (tags.Contains("cell-types"))
                PublicWorksheetCells(archive)
                    .Select(cell => cell.Attribute("t")?.Value ?? "n")
                    .Distinct(StringComparer.Ordinal)
                    .Should()
                    .HaveCountGreaterThanOrEqualTo(3, row.Id);

            if (tags.Contains("sheet-names") && tags.Contains("boundary"))
                PublicWorkbookSheetNames(archive)
                    .Should()
                    .Contain(name => name.Length == 31, row.Id);

            if (tags.Contains("chartsheet") || tags.Contains("unsupported-sheet-types"))
                AssertPublicChartsheetPackageGraph(archive, row.Id);

            if (tags.Contains("mac-excel-package"))
                AssertPublicMacExcelPackageGraph(archive, row.Id);

            if (tags.Contains("numbers-worksheet-target"))
                AssertPublicNumbersWorksheetTarget(archive, row.Id);
        }
        finally
        {
            if (package.CanSeek)
                package.Position = originalPosition;
        }
    }

    private static void AssertPublicSharedStringPackageGraph(ZipArchive archive, string because)
    {
        const string sharedStringContentType =
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml";
        const string sharedStringRelationshipType =
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/sharedStrings";

        var sharedStringsEntry = archive.GetEntry("xl/sharedStrings.xml");
        sharedStringsEntry.Should().NotBeNull(because);

        AssertContentTypeOverride(archive, "/xl/sharedStrings.xml", sharedStringContentType, because);
        PublicWorkbookRelationships(archive)
            .Should()
            .ContainSingle(rel =>
                string.Equals(AttributeValue(rel, "Type"), sharedStringRelationshipType, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(ResolveWorkbookRelationshipTarget(AttributeValue(rel, "Target")), "xl/sharedStrings.xml", StringComparison.OrdinalIgnoreCase),
                because);

        var sharedStringsXml = LoadPackageXml(sharedStringsEntry!);
        var sharedStringItems = sharedStringsXml.Root?
            .Elements(WorksheetNs + "si")
            .ToArray() ?? [];
        sharedStringItems.Should().NotBeEmpty(because);

        var indexes = PublicWorksheetCells(archive)
            .Where(cell => string.Equals(cell.Attribute("t")?.Value, "s", StringComparison.Ordinal))
            .Select(cell => int.TryParse(cell.Element(WorksheetNs + "v")?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var index)
                ? index
                : -1)
            .ToArray();

        indexes.Should().NotBeEmpty(because);
        indexes.Should().OnlyContain(index => index >= 0 && index < sharedStringItems.Length, because);
    }

    private static void AssertPublicHyperlinkPackageGraph(ZipArchive archive, string because)
    {
        const string hyperlinkRelationshipType =
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink";

        var inspectedExternalHyperlinks = 0;
        foreach (var worksheetEntry in archive.Entries
                     .Where(entry => entry.FullName.StartsWith("xl/worksheets/", StringComparison.Ordinal) &&
                                     entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(entry => entry.FullName, StringComparer.OrdinalIgnoreCase))
        {
            var worksheetXml = LoadPackageXml(worksheetEntry);
            var relationshipIds = worksheetXml
                .Descendants(WorksheetNs + "hyperlink")
                .Select(hyperlink => hyperlink.Attribute(OfficeRelationshipNs + "id")?.Value)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToArray();

            if (relationshipIds.Length == 0)
                continue;

            var worksheetPath = worksheetEntry.FullName.Replace('\\', '/');
            var slashIndex = worksheetPath.LastIndexOf('/');
            var relationshipPart = slashIndex < 0
                ? $"_rels/{worksheetPath}.rels"
                : $"{worksheetPath[..slashIndex]}/_rels/{worksheetPath[(slashIndex + 1)..]}.rels";

            var relationshipsEntry = archive.GetEntry(relationshipPart);
            relationshipsEntry.Should().NotBeNull(because);
            var relationships = LoadPackageXml(relationshipsEntry!)
                .Root!
                .Elements(PackageRelationshipNs + "Relationship")
                .ToArray();

            foreach (var relationshipId in relationshipIds)
            {
                var relationship = relationships.SingleOrDefault(rel =>
                    string.Equals(AttributeValue(rel, "Id"), relationshipId, StringComparison.Ordinal));
                relationship.Should().NotBeNull(because);
                relationship!.Attribute("Target")?.Value.Should().NotBeNullOrWhiteSpace(because);
                relationship.Attribute("TargetMode")!.Value.Should().Be("External", because);
                relationship.Attribute("Type")!.Value.Should().Be(hyperlinkRelationshipType, because);
                inspectedExternalHyperlinks++;
            }
        }

        inspectedExternalHyperlinks.Should().BeGreaterThan(0, because);
    }

    private static void AssertPublicChartsheetPackageGraph(ZipArchive archive, string because)
    {
        const string chartsheetContentType =
            "application/vnd.openxmlformats-officedocument.spreadsheetml.chartsheet+xml";
        const string chartsheetRelationshipType =
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chartsheet";

        var workbookXml = LoadPublicWorkbookXml(archive);
        var workbookRelationships = PublicWorkbookRelationships(archive).ToArray();
        var chartsheetParts = workbookXml.Root!
            .Element(WorksheetNs + "sheets")!
            .Elements(WorksheetNs + "sheet")
            .Select(sheet => sheet.Attribute(XName.Get("id", "http://schemas.openxmlformats.org/officeDocument/2006/relationships"))?.Value)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => workbookRelationships.SingleOrDefault(rel =>
                string.Equals(AttributeValue(rel, "Id"), id, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(AttributeValue(rel, "Type"), chartsheetRelationshipType, StringComparison.OrdinalIgnoreCase)))
            .Where(rel => rel is not null)
            .Select(rel => ResolveWorkbookRelationshipTarget(AttributeValue(rel!, "Target")))
            .Where(target => target.StartsWith("xl/chartsheets/", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        chartsheetParts.Should().NotBeEmpty(because);

        foreach (var chartsheetPart in chartsheetParts)
        {
            var chartsheetEntry = archive.GetEntry(chartsheetPart);
            chartsheetEntry.Should().NotBeNull(because);
            LoadPackageXml(chartsheetEntry!).Root!.Name.Should().Be(WorksheetNs + "chartsheet", because);
            AssertContentTypeOverride(archive, "/" + chartsheetPart, chartsheetContentType, because);
        }
    }

    private static void AssertPublicMacExcelPackageGraph(ZipArchive archive, string because)
    {
        AssertPublicMacExcelAppMetadata(archive, because);
        AssertPublicMacExcelThemeGraph(archive, because);
        AssertPublicMacExcelSharedStringAnchor(archive, because);
        AssertPublicMacExcelStyleTable(archive, because);
    }

    private static void AssertPublicMacExcelAppMetadata(ZipArchive archive, string because)
    {
        const string extendedPropertiesContentType =
            "application/vnd.openxmlformats-officedocument.extended-properties+xml";
        const string extendedPropertiesRelationshipType =
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties";

        var appEntry = archive.GetEntry("docProps/app.xml");
        appEntry.Should().NotBeNull(because);
        AssertContentTypeOverride(archive, "/docProps/app.xml", extendedPropertiesContentType, because);
        PublicPackageRootRelationships(archive)
            .Should()
            .ContainSingle(rel =>
                string.Equals(AttributeValue(rel, "Type"), extendedPropertiesRelationshipType, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(ResolvePackageRootRelationshipTarget(AttributeValue(rel, "Target")), "docProps/app.xml", StringComparison.OrdinalIgnoreCase),
                because);

        var appXml = LoadPackageXml(appEntry!);
        appXml.Root!.Name.Should().Be(ExtendedPropertiesNs + "Properties", because);
        appXml.Root.Element(ExtendedPropertiesNs + "Application")?.Value
            .Should().Be("Microsoft Macintosh Excel", because);
        appXml.Root.Element(ExtendedPropertiesNs + "AppVersion")?.Value
            .Should().Be("14.0300", because);
    }

    private static void AssertPublicMacExcelThemeGraph(ZipArchive archive, string because)
    {
        const string themeContentType =
            "application/vnd.openxmlformats-officedocument.theme+xml";
        const string themeRelationshipType =
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/theme";

        var themeEntry = archive.GetEntry("xl/theme/theme1.xml");
        themeEntry.Should().NotBeNull(because);
        AssertContentTypeOverride(archive, "/xl/theme/theme1.xml", themeContentType, because);
        PublicWorkbookRelationships(archive)
            .Should()
            .ContainSingle(rel =>
                string.Equals(AttributeValue(rel, "Type"), themeRelationshipType, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(ResolveWorkbookRelationshipTarget(AttributeValue(rel, "Target")), "xl/theme/theme1.xml", StringComparison.OrdinalIgnoreCase),
                because);

        var themeXml = LoadPackageXml(themeEntry!);
        themeXml.Root!.Name.Should().Be(DrawingNs + "theme", because);
        var themeElements = themeXml.Root.Element(DrawingNs + "themeElements");
        themeElements.Should().NotBeNull(because);
        themeElements!.Element(DrawingNs + "clrScheme")?.Attribute("name")?.Value
            .Should().Be("Office", because);
        themeElements.Element(DrawingNs + "fontScheme")?.Attribute("name")?.Value
            .Should().Be("Office", because);
        themeElements.Element(DrawingNs + "fmtScheme")?.Attribute("name")?.Value
            .Should().Be("Office", because);
    }

    private static void AssertPublicMacExcelSharedStringAnchor(ZipArchive archive, string because)
    {
        AssertPublicSharedStringPackageGraph(archive, because);

        var sharedStringsXml = LoadPackageXml(archive, "xl/sharedStrings.xml");
        var sharedStringItems = sharedStringsXml.Root!
            .Elements(WorksheetNs + "si")
            .ToArray();

        var firstCell = PublicWorksheetCells(archive)
            .SingleOrDefault(cell => string.Equals(cell.Attribute("r")?.Value, "A1", StringComparison.Ordinal));
        firstCell.Should().NotBeNull(because);
        firstCell!.Attribute("t")!.Value.Should().Be("s", because);

        int.TryParse(firstCell.Element(WorksheetNs + "v")?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var sharedStringIndex)
            .Should().BeTrue(because);
        sharedStringIndex.Should().BeGreaterThanOrEqualTo(0, because);
        sharedStringIndex.Should().BeLessThan(sharedStringItems.Length, because);
        string.Concat(sharedStringItems[sharedStringIndex].Descendants(WorksheetNs + "t").Select(text => text.Value))
            .Should().NotBeNullOrWhiteSpace(because);
    }

    private static void AssertPublicMacExcelStyleTable(ZipArchive archive, string because)
    {
        const string stylesContentType =
            "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml";
        const string stylesRelationshipType =
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles";

        var stylesEntry = archive.GetEntry("xl/styles.xml");
        stylesEntry.Should().NotBeNull(because);
        AssertContentTypeOverride(archive, "/xl/styles.xml", stylesContentType, because);
        PublicWorkbookRelationships(archive)
            .Should()
            .ContainSingle(rel =>
                string.Equals(AttributeValue(rel, "Type"), stylesRelationshipType, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(ResolveWorkbookRelationshipTarget(AttributeValue(rel, "Target")), "xl/styles.xml", StringComparison.OrdinalIgnoreCase),
                because);

        var stylesXml = LoadPackageXml(stylesEntry!);
        stylesXml.Root!.Name.Should().Be(WorksheetNs + "styleSheet", because);
        var cellXfsCount = GetPackageElementCount(RequireStyleElement(stylesXml, "cellXfs", because));
        cellXfsCount.Should().BeGreaterThanOrEqualTo(2, because);

        var cellStyleXfsCount = GetPackageElementCount(RequireStyleElement(stylesXml, "cellStyleXfs", because));
        var cellStylesCount = GetPackageElementCount(RequireStyleElement(stylesXml, "cellStyles", because));
        cellStyleXfsCount.Should().BeGreaterThan(0, because);
        cellStylesCount.Should().BeGreaterThan(0, because);

        var styleIndexes = PublicWorksheetCells(archive)
            .Select(cell => int.TryParse(cell.Attribute("s")?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var styleIndex)
                ? styleIndex
                : -1)
            .Where(styleIndex => styleIndex >= 0)
            .ToArray();
        styleIndexes.Should().NotBeEmpty(because);
        styleIndexes.Should().OnlyContain(styleIndex => styleIndex < cellXfsCount, because);
    }

    private static void AssertPublicNumbersWorksheetTarget(ZipArchive archive, string because)
    {
        const string worksheetContentType =
            "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml";
        const string worksheetRelationshipType =
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet";

        var workbookXml = LoadPublicWorkbookXml(archive);
        var sheetRelationshipIds = workbookXml.Root!
            .Element(WorksheetNs + "sheets")!
            .Elements(WorksheetNs + "sheet")
            .Select(sheet => sheet.Attribute(XName.Get("id", "http://schemas.openxmlformats.org/officeDocument/2006/relationships"))?.Value)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToArray();

        sheetRelationshipIds.Should().ContainSingle(because);
        var sheetRelationshipId = sheetRelationshipIds[0]!;
        var worksheetRelationship = PublicWorkbookRelationships(archive)
            .SingleOrDefault(rel =>
                string.Equals(AttributeValue(rel, "Id"), sheetRelationshipId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(AttributeValue(rel, "Type"), worksheetRelationshipType, StringComparison.OrdinalIgnoreCase));
        worksheetRelationship.Should().NotBeNull(because);

        ResolveWorkbookRelationshipTarget(AttributeValue(worksheetRelationship!, "Target"))
            .Should().Be("xl/worksheets/sheet.xml", because);
        archive.GetEntry("xl/worksheets/sheet1.xml").Should().BeNull(because);

        var worksheetEntry = archive.GetEntry("xl/worksheets/sheet.xml");
        worksheetEntry.Should().NotBeNull(because);
        AssertContentTypeOverride(archive, "/xl/worksheets/sheet.xml", worksheetContentType, because);
        LoadPackageXml(worksheetEntry!).Root!.Name.Should().Be(WorksheetNs + "worksheet", because);
    }

    private static XElement RequireStyleElement(XDocument stylesXml, string localName, string because)
    {
        var element = stylesXml.Root!.Element(WorksheetNs + localName);
        element.Should().NotBeNull(because);
        return element!;
    }

    private static int GetPackageElementCount(XElement element) =>
        int.TryParse(element.Attribute("count")?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var count)
            ? count
            : element.Elements().Count();

    private static IReadOnlyList<XElement> PublicWorksheetElements(ZipArchive archive, string localName)
    {
        return PublicWorksheetXmlDocuments(archive)
            .SelectMany(document => document.Descendants(WorksheetNs + localName))
            .ToArray();
    }

    private static IReadOnlyList<XElement> PublicWorksheetCells(ZipArchive archive) =>
        PublicWorksheetElements(archive, "c");

    private static IReadOnlyList<XDocument> PublicWorksheetXmlDocuments(ZipArchive archive)
    {
        return archive.Entries
            .Where(entry => entry.FullName.StartsWith("xl/worksheets/", StringComparison.Ordinal) &&
                            entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .Select(LoadPackageXml)
            .ToArray();
    }

    private static IReadOnlyList<string> PublicWorkbookSheetNames(ZipArchive archive)
    {
        return LoadPublicWorkbookXml(archive)
            .Descendants(WorksheetNs + "sheet")
            .Select(sheet => sheet.Attribute("name")?.Value ?? "")
            .Where(name => name.Length > 0)
            .ToArray();
    }

    private static XDocument LoadPublicWorkbookXml(ZipArchive archive)
    {
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        workbookEntry.Should().NotBeNull("public workbook packages should contain workbook.xml");
        return LoadPackageXml(workbookEntry!);
    }

    private static IReadOnlyList<XElement> PublicWorkbookRelationships(ZipArchive archive)
    {
        var workbookRelsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels");
        workbookRelsEntry.Should().NotBeNull("public workbook packages should contain workbook relationships");
        return LoadPackageXml(workbookRelsEntry!)
            .Root!
            .Elements(PackageRelationshipNs + "Relationship")
            .ToArray();
    }

    private static IReadOnlyList<XElement> PublicPackageRootRelationships(ZipArchive archive)
    {
        var packageRelsEntry = archive.GetEntry("_rels/.rels");
        packageRelsEntry.Should().NotBeNull("public workbook packages should contain package root relationships");
        return LoadPackageXml(packageRelsEntry!)
            .Root!
            .Elements(PackageRelationshipNs + "Relationship")
            .ToArray();
    }

    private static string ResolveWorkbookRelationshipTarget(string? target)
    {
        if (string.IsNullOrWhiteSpace(target))
            return "";

        target = target.Replace('\\', '/').Trim();
        if (target.StartsWith("/", StringComparison.Ordinal))
            return NormalizePackagePart(target);

        return NormalizePackagePart("xl/" + target);
    }

    private static string ResolvePackageRootRelationshipTarget(string? target)
    {
        if (string.IsNullOrWhiteSpace(target))
            return "";

        return NormalizePackagePart(target.Replace('\\', '/').Trim());
    }

    private const string RelationshipPartContentType =
        "application/vnd.openxmlformats-package.relationships+xml";
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace DrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace ExtendedPropertiesNs =
        "http://schemas.openxmlformats.org/officeDocument/2006/extended-properties";
    private static readonly XNamespace PackageContentTypeNs =
        "http://schemas.openxmlformats.org/package/2006/content-types";
    private static readonly XNamespace PackageRelationshipNs =
        "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace OfficeRelationshipNs =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly string[] ModeledIgnoredErrorFlags =
    [
        "numberStoredAsText",
        "evalError",
        "formula",
        "emptyCellReference"
    ];

    private static bool HasExpectedPublicPackageTags(ManifestRow row)
    {
        if (row.SourceType != "public")
            return false;

        var tags = row.FeatureTags.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return tags.Contains("styles") ||
               tags.Contains("formatting") ||
               tags.Contains("hyperlinks") ||
               tags.Contains("merged-cells") ||
               tags.Contains("inline-strings") ||
               tags.Contains("shared-string-package") ||
               tags.Contains("cell-types") ||
               (tags.Contains("sheet-names") && tags.Contains("boundary")) ||
               tags.Contains("chartsheet") ||
               tags.Contains("unsupported-sheet-types") ||
               tags.Contains("mac-excel-package") ||
               tags.Contains("numbers-worksheet-target");
    }

    private static bool HasEditStablePublicPackageTags(ManifestRow row)
    {
        if (!HasExpectedPublicPackageTags(row))
            return false;

        var tags = row.FeatureTags.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return tags.Any(tag => tag is not "numbers-worksheet-target" && HasEditStablePublicPackageTag(tag, tags));
    }

    private static bool HasEditStablePublicPackageTag(string tag, string[] tags) =>
        tag is "styles" or
            "formatting" or
            "hyperlinks" or
            "merged-cells" or
            "inline-strings" or
            "shared-string-package" or
            "cell-types" or
            "chartsheet" or
            "unsupported-sheet-types" or
            "mac-excel-package" ||
        (tag == "sheet-names" && tags.Contains("boundary"));

    private static DataValidationSummary CaptureDataValidationSummary(DataValidation validation) =>
        new(
            validation.Type,
            validation.Operator,
            validation.Formula1 ?? "",
            validation.Formula2 ?? "",
            validation.AllowBlank,
            validation.ShowDropdown,
            validation.AlertStyle,
            validation.ShowInputMessage,
            validation.ShowErrorMessage,
            validation.ErrorTitle ?? "",
            validation.ErrorMessage ?? "",
            validation.PromptTitle ?? "",
            validation.PromptMessage ?? "",
            ToRangeSummary(validation.AppliesTo),
            validation.AdditionalRanges.Select(ToRangeSummary).ToArray());

    private static WorkbookSummary CapturePublicComparableSummary(Workbook workbook)
    {
        var summary = CaptureSummary(workbook);
        return summary with
        {
            Sheets = summary.Sheets
                .Select(sheet => sheet with
                {
                    Cells = [],
                    HeaderFooterAlignWithMargins = true,
                    HeaderFooterScaleWithDocument = true,
                    DefaultColumnWidth = 0,
                    DefaultRowHeight = 0,
                    ColumnWidths = [],
                    RowHeights = [],
                    StyleOnlyCells = [],
                    StyleOnlyCellCount = 0
                })
                .ToArray()
        };
    }

    private static PackagePartSummary CapturePackageSummary(Stream stream)
    {
        var originalPosition = stream.CanSeek ? stream.Position : 0;
        if (stream.CanSeek)
            stream.Position = 0;

        try
        {
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            return new PackagePartSummary(
                archive.Entries
                    .Select(entry => entry.FullName.Replace('\\', '/'))
                    .Where(IsFidelityCriticalPart)
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                archive.Entries
                    .Where(entry => entry.FullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase))
                    .SelectMany(ReadRelationshipTargets)
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                archive.Entries
                    .Where(entry => entry.FullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase))
                    .SelectMany(ReadRelationshipDetails)
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                ReadCriticalContentTypeOverrides(archive)
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToArray());
        }
        finally
        {
            if (stream.CanSeek)
                stream.Position = originalPosition;
        }
    }

    private static DataValidationPackageXmlSummary CaptureDataValidationPackageSummary(Stream stream)
    {
        var originalPosition = stream.CanSeek ? stream.Position : 0;
        if (stream.CanSeek)
            stream.Position = 0;

        try
        {
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            var worksheetEntry = archive.GetEntry("xl/worksheets/sheet1.xml");
            worksheetEntry.Should().NotBeNull("generated-dv-count-package-003 must contain xl/worksheets/sheet1.xml");

            var worksheetXml = LoadPackageXml(worksheetEntry!);
            XNamespace sheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var container = worksheetXml.Root!.Element(sheetNs + "dataValidations");
            container.Should().NotBeNull("generated-dv-count-package-003 must include a dataValidations container");

            return new DataValidationPackageXmlSummary(
                container!.Attribute("count")?.Value ?? "",
                container.Elements(sheetNs + "dataValidation")
                    .Select(element =>
                    {
                        var type = element.Attribute("type")?.Value ?? "";
                        return new DataValidationRuleXmlSummary(
                            type,
                            NormalizeDataValidationOperator(type, element.Attribute("operator")?.Value ?? ""),
                            element.Attribute("sqref")?.Value ?? "",
                            NormalizeDataValidationFormula(type, element.Element(sheetNs + "formula1")?.Value ?? ""),
                            element.Element(sheetNs + "formula2")?.Value ?? "");
                    })
                    .ToArray());
        }
        finally
        {
            if (stream.CanSeek)
                stream.Position = originalPosition;
        }
    }

    private static WorksheetSortFilterPackageXmlSummary CaptureWorksheetSortFilterPackageSummary(Stream stream)
    {
        var originalPosition = stream.CanSeek ? stream.Position : 0;
        if (stream.CanSeek)
            stream.Position = 0;

        try
        {
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            var worksheetEntry = archive.GetEntry("xl/worksheets/sheet1.xml");
            worksheetEntry.Should().NotBeNull("generated-worksheet-sort-state-001 must contain xl/worksheets/sheet1.xml");

            var worksheetXml = LoadPackageXml(worksheetEntry!);
            XNamespace sheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var autoFilter = worksheetXml.Root!.Element(sheetNs + "autoFilter");
            var sortState = worksheetXml.Root.Element(sheetNs + "sortState");

            autoFilter.Should().NotBeNull("generated-worksheet-sort-state-001 must include worksheet AutoFilter metadata");
            sortState.Should().NotBeNull("generated-worksheet-sort-state-001 must include worksheet sortState metadata");

            return new WorksheetSortFilterPackageXmlSummary(
                CaptureXmlElementSummary(autoFilter!),
                CaptureXmlElementSummary(sortState!));
        }
        finally
        {
            if (stream.CanSeek)
                stream.Position = originalPosition;
        }
    }

    private static WorksheetIgnoredErrorsPackageXmlSummary CaptureWorksheetIgnoredErrorsPackageSummary(Stream stream)
    {
        var originalPosition = stream.CanSeek ? stream.Position : 0;
        if (stream.CanSeek)
            stream.Position = 0;

        try
        {
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            var worksheetEntry = archive.GetEntry("xl/worksheets/sheet1.xml");
            worksheetEntry.Should().NotBeNull("generated-worksheet-ignored-errors-001 must contain xl/worksheets/sheet1.xml");

            var worksheetXml = LoadPackageXml(worksheetEntry!);
            XNamespace sheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var container = worksheetXml.Root!.Element(sheetNs + "ignoredErrors");
            container.Should().NotBeNull("generated-worksheet-ignored-errors-001 must include worksheet ignoredErrors metadata");

            return new WorksheetIgnoredErrorsPackageXmlSummary(
                container!.Attributes()
                    .Where(attribute => !attribute.IsNamespaceDeclaration)
                    .OrderBy(attribute => attribute.Name.ToString(), StringComparer.Ordinal)
                    .Select(attribute => new NativeAttributeSummary(attribute.Name.ToString(), attribute.Value))
                    .ToArray(),
                container.Elements(sheetNs + "ignoredError")
                    .Select(element => new WorksheetIgnoredErrorXmlSummary(
                        element.Attribute("sqref")?.Value ?? "",
                        HasModeledIgnoredErrorFlag(element),
                        CaptureRetainedIgnoredErrorAttributes(element)))
                    .ToArray());
        }
        finally
        {
            if (stream.CanSeek)
                stream.Position = originalPosition;
        }
    }

    private static bool HasModeledIgnoredErrorFlag(XElement ignoredError) =>
        ModeledIgnoredErrorFlags.Any(flag => IsTruthyXmlBoolean(ignoredError.Attribute(flag)?.Value));

    private static IReadOnlyList<NativeAttributeSummary> CaptureRetainedIgnoredErrorAttributes(XElement ignoredError) =>
        ignoredError.Attributes()
            .Where(attribute =>
                !attribute.IsNamespaceDeclaration &&
                !string.Equals(attribute.Name.LocalName, "sqref", StringComparison.Ordinal) &&
                !ModeledIgnoredErrorFlags.Contains(attribute.Name.LocalName, StringComparer.Ordinal))
            .OrderBy(attribute => attribute.Name.ToString(), StringComparer.Ordinal)
            .Select(attribute => new NativeAttributeSummary(attribute.Name.ToString(), attribute.Value))
            .ToArray();

    private static bool IsTruthyXmlBoolean(string? value) =>
        string.Equals(value, "1", StringComparison.Ordinal) ||
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    private static WorksheetElementXmlSummary CaptureXmlElementSummary(XElement element) =>
        new(
            element.Name.ToString(),
            element.Attributes()
                .Where(attribute => !attribute.IsNamespaceDeclaration)
                .OrderBy(attribute => attribute.Name.ToString(), StringComparer.Ordinal)
                .Select(attribute => new NativeAttributeSummary(attribute.Name.ToString(), attribute.Value))
                .ToArray(),
            element.Elements().Any() ? "" : element.Value.Trim(),
            element.Elements()
                .Select(CaptureXmlElementSummary)
                .ToArray());

    private static string NormalizeDataValidationOperator(string type, string op)
    {
        if (type is "list" or "custom" && string.Equals(op, "between", StringComparison.OrdinalIgnoreCase))
            return "";

        if (!string.IsNullOrWhiteSpace(op))
            return op;

        return type is "whole" or "decimal" or "date" or "time" or "textLength" ? "between" : "";
    }

    private static string NormalizeDataValidationFormula(string type, string formula)
    {
        if (type != "list" || formula.Length < 2 || formula[0] != '"' || formula[^1] != '"')
            return formula;

        return formula[1..^1].Replace("\"\"", "\"", StringComparison.Ordinal);
    }

    private static void AssertPackageHealth(Stream stream, string because)
    {
        var originalPosition = stream.CanSeek ? stream.Position : 0;
        if (stream.CanSeek)
            stream.Position = 0;

        try
        {
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
            {
                var entries = archive.Entries
                    .Select(entry => entry.FullName.Replace('\\', '/'))
                    .ToArray();
                entries.Should().OnlyHaveUniqueItems(because);
                var packageEntryIssues = FindPackageEntryIssues(archive);
                packageEntryIssues.Should().BeEmpty($"{because}: OPC package part names should be canonical and unique");

                // OPC part names are compared case-insensitively, so two names differing only by case
                // (e.g. ClosedXML's xl/drawings/vmldrawing2.vml vs Excel's xl/drawings/vmlDrawing2.vml)
                // make the package unreadable in Excel even though the zip entries are distinct.
                entries.Select(name => name.ToLowerInvariant())
                    .Should().OnlyHaveUniqueItems($"{because}: OPC part names must be unique case-insensitively");

                var packageParts = archive.Entries
                    .Where(entry => !string.IsNullOrEmpty(entry.Name))
                    .Select(entry => NormalizePackagePart(entry.FullName))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var contentTypesEntry = archive.GetEntry("[Content_Types].xml");
                contentTypesEntry.Should().NotBeNull(because);

                if (contentTypesEntry is not null)
                {
                    var contentTypesXml = LoadPackageXml(contentTypesEntry);
                    var contentTypeIssues = FindPackageContentTypeIssues(contentTypesXml, packageParts);
                    contentTypeIssues.Should().BeEmpty($"{because}: package content types should be complete and consistent");
                }

                foreach (var xmlEntry in archive.Entries.Where(entry =>
                             entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ||
                             entry.FullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase)))
                {
                    using var xmlStream = xmlEntry.Open();
                    var load = () => XDocument.Load(xmlStream);
                    load.Should().NotThrow($"{because}: {xmlEntry.FullName} should be parseable XML");
                }

                var relationshipIssues = FindPackageRelationshipIssues(archive, packageParts);
                relationshipIssues.Should().BeEmpty($"{because}: package relationships should be well-formed and target existing internal parts");
            }

            // The definitive check: the Open XML SDK (same OPC layer Excel uses) must be able to open
            // the package. A "Format error in package" here is exactly what makes Excel refuse the file
            // and strip features on repair.
            stream.Position = 0;
            var openPackage = () =>
            {
                using var document = SpreadsheetDocument.Open(stream, isEditable: false);
                _ = document.WorkbookPart;
            };
            openPackage.Should().NotThrow($"{because}: saved package must be OPC-readable (Excel can open it)");
        }
        finally
        {
            if (stream.CanSeek)
                stream.Position = originalPosition;
        }
    }

    private static string RelationshipSourcePart(string relsPath)
    {
        if (string.Equals(relsPath, "_rels/.rels", StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        var relsMarker = "/_rels/";
        var markerIndex = relsPath.IndexOf(relsMarker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0 || !relsPath.EndsWith(".rels", StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        var prefix = relsPath[..markerIndex];
        var fileName = relsPath[(markerIndex + relsMarker.Length)..^".rels".Length];
        return string.IsNullOrWhiteSpace(prefix) ? fileName : $"{prefix}/{fileName}";
    }

    private static List<string> FindPackageEntryIssues(ZipArchive archive)
    {
        var issues = new List<string>();
        var exactNames = new HashSet<string>(StringComparer.Ordinal);
        var packagePartNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries.Where(entry => !string.IsNullOrEmpty(entry.Name)))
        {
            var rawName = entry.FullName;
            var normalizedName = rawName.Replace('\\', '/');

            if (rawName.Contains('\\', StringComparison.Ordinal))
                issues.Add($"{rawName} uses a backslash in the package part name");
            if (normalizedName.StartsWith("/", StringComparison.Ordinal))
                issues.Add($"{rawName} starts with '/'");
            if (normalizedName.Contains("//", StringComparison.Ordinal))
                issues.Add($"{rawName} has an empty path segment");

            var segments = normalizedName.Split('/', StringSplitOptions.None);
            if (segments.Any(segment => segment is "." or ".."))
                issues.Add($"{rawName} has a relative path segment");

            if (!exactNames.Add(normalizedName))
            {
                issues.Add($"{rawName} duplicates package part {normalizedName}");
                continue;
            }

            if (packagePartNames.TryGetValue(normalizedName, out var existingName))
                issues.Add($"{rawName} collides with package part {existingName} when compared case-insensitively");
            else
                packagePartNames.Add(normalizedName, normalizedName);
        }

        return issues;
    }

    private static List<string> FindPackageContentTypeIssues(
        XDocument contentTypesXml,
        IReadOnlySet<string> packageParts)
    {
        var issues = new List<string>();
        if (contentTypesXml.Root?.Name != PackageContentTypeNs + "Types")
        {
            issues.Add("[Content_Types].xml has an invalid root element");
            return issues;
        }

        AddPackageContentTypeDeclarationIssues(contentTypesXml, packageParts, issues);

        foreach (var part in packageParts
                     .Where(part => !string.Equals(part, "[Content_Types].xml", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(part => part, StringComparer.OrdinalIgnoreCase))
        {
            var contentType = GetEffectivePackageContentType(contentTypesXml, part);
            if (string.IsNullOrWhiteSpace(contentType))
            {
                issues.Add($"{part} has no effective content type");
                continue;
            }

            AddPackageContentTypeConsistencyIssues(part, contentType, issues);
        }

        return issues;
    }

    private static void AddPackageContentTypeDeclarationIssues(
        XDocument contentTypesXml,
        IReadOnlySet<string> packageParts,
        List<string> issues)
    {
        var root = contentTypesXml.Root;
        if (root is null)
            return;

        foreach (var element in root.Elements())
        {
            if (element.Name != PackageContentTypeNs + "Default" &&
                element.Name != PackageContentTypeNs + "Override")
            {
                issues.Add($"unexpected [Content_Types].xml child element '{element.Name}'");
            }
        }

        var defaultExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var element in root.Elements(PackageContentTypeNs + "Default"))
        {
            var extension = element.Attribute("Extension")?.Value;
            var declarationLabel = string.IsNullOrWhiteSpace(extension)
                ? "Default declaration"
                : $"Default extension '{extension}'";

            if (string.IsNullOrWhiteSpace(extension))
            {
                issues.Add("Default declaration missing Extension");
            }
            else
            {
                var trimmedExtension = extension.Trim();
                declarationLabel = $"Default extension '{trimmedExtension}'";

                if (!string.Equals(extension, trimmedExtension, StringComparison.Ordinal))
                    issues.Add($"Default extension '{extension}' has leading or trailing whitespace");

                if (trimmedExtension.IndexOf('/') >= 0 ||
                    trimmedExtension.IndexOf('\\') >= 0 ||
                    trimmedExtension.IndexOf('.') >= 0 ||
                    trimmedExtension.Any(char.IsWhiteSpace))
                {
                    issues.Add($"Default extension '{trimmedExtension}' is not a bare package extension");
                }

                if (!defaultExtensions.Add(trimmedExtension))
                    issues.Add($"duplicate Default extension '{trimmedExtension}'");
            }

            AddContentTypeAttributeIssues(element, declarationLabel, issues);
        }

        var overridePartNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var element in root.Elements(PackageContentTypeNs + "Override"))
        {
            var partName = element.Attribute("PartName")?.Value;
            var declarationLabel = string.IsNullOrWhiteSpace(partName)
                ? "Override declaration"
                : $"Override PartName '{partName}'";

            if (string.IsNullOrWhiteSpace(partName))
            {
                issues.Add("Override declaration missing PartName");
            }
            else
            {
                var trimmedPartName = partName.Trim();

                if (!string.Equals(partName, trimmedPartName, StringComparison.Ordinal))
                    issues.Add($"Override PartName '{partName}' has leading or trailing whitespace");

                if (!trimmedPartName.StartsWith("/", StringComparison.Ordinal))
                    issues.Add($"Override PartName '{partName}' must start with '/'");

                if (trimmedPartName.IndexOf('\\') >= 0)
                    issues.Add($"Override PartName '{partName}' must use forward slashes");

                if (trimmedPartName.IndexOf('?') >= 0 || trimmedPartName.IndexOf('#') >= 0)
                    issues.Add($"Override PartName '{partName}' must not include query or fragment text");

                var pathWithoutRootSlash = trimmedPartName.TrimStart('/');
                if (!TryNormalizePackagePathSegments(pathWithoutRootSlash, out var overridePart))
                {
                    issues.Add($"Override PartName '{partName}' escapes the package root");
                }
                else if (string.IsNullOrWhiteSpace(overridePart))
                {
                    issues.Add($"Override PartName '{partName}' does not reference a package part");
                }
                else
                {
                    declarationLabel = $"Override PartName '/{overridePart}'";
                    var rawNormalizedPart = NormalizePackagePart(trimmedPartName);
                    if (!string.Equals(overridePart, rawNormalizedPart, StringComparison.Ordinal))
                        issues.Add($"Override PartName '{partName}' is not canonical");

                    if (!overridePartNames.Add(overridePart))
                        issues.Add($"duplicate Override PartName '/{overridePart}'");

                    if (!packageParts.Contains(overridePart))
                        issues.Add($"Override PartName '/{overridePart}' references missing package part");
                }
            }

            AddContentTypeAttributeIssues(element, declarationLabel, issues);
        }
    }

    private static void AddContentTypeAttributeIssues(
        XElement element,
        string declarationLabel,
        List<string> issues)
    {
        var contentType = element.Attribute("ContentType")?.Value;
        if (string.IsNullOrWhiteSpace(contentType))
        {
            issues.Add($"{declarationLabel} missing ContentType");
            return;
        }

        if (!string.Equals(contentType, contentType.Trim(), StringComparison.Ordinal))
            issues.Add($"{declarationLabel} ContentType has leading or trailing whitespace");

        if (!contentType.Contains("/", StringComparison.Ordinal))
            issues.Add($"{declarationLabel} ContentType '{contentType}' is not a media type");
    }

    private static void AddPackageContentTypeConsistencyIssues(
        string part,
        string contentType,
        List<string> issues)
    {
        var isRelationshipPart = IsPackageRelationshipPart(part);
        var hasRelationshipExtension = part.EndsWith(".rels", StringComparison.OrdinalIgnoreCase);
        var hasRelationshipContentType = string.Equals(
            contentType,
            RelationshipPartContentType,
            StringComparison.OrdinalIgnoreCase);

        if (isRelationshipPart && !hasRelationshipContentType)
        {
            issues.Add($"{part} must use relationship content type {RelationshipPartContentType}; actual {contentType}");
        }
        else if (!isRelationshipPart && hasRelationshipContentType)
        {
            issues.Add($"{part} uses relationship content type but is not a valid relationship part");
        }

        if (hasRelationshipExtension && !isRelationshipPart)
            issues.Add($"{part} has .rels extension outside a valid relationship part location");
    }

    private static string? GetEffectivePackageContentType(XDocument contentTypesXml, string normalizedPartName)
    {
        var normalizedContentTypePartName = $"/{NormalizePackagePart(normalizedPartName)}";
        var overrideContentType = contentTypesXml.Root?
            .Elements(PackageContentTypeNs + "Override")
            .FirstOrDefault(element => string.Equals(
                NormalizeContentTypePartName(element.Attribute("PartName")?.Value),
                normalizedContentTypePartName,
                StringComparison.OrdinalIgnoreCase))
            ?.Attribute("ContentType")
            ?.Value;

        if (!string.IsNullOrWhiteSpace(overrideContentType))
            return overrideContentType;

        var extension = GetPackagePartExtension(normalizedPartName);
        if (string.IsNullOrWhiteSpace(extension))
            return null;

        return contentTypesXml.Root?
            .Elements(PackageContentTypeNs + "Default")
            .FirstOrDefault(element => string.Equals(
                element.Attribute("Extension")?.Value,
                extension,
                StringComparison.OrdinalIgnoreCase))
            ?.Attribute("ContentType")
            ?.Value;
    }

    private static string NormalizeContentTypePartName(string? partName) =>
        $"/{NormalizePackagePart(partName ?? string.Empty)}";

    private static string GetPackagePartExtension(string partName)
    {
        var fileName = NormalizePackagePart(partName);
        var slashIndex = fileName.LastIndexOf('/');
        if (slashIndex >= 0)
            fileName = fileName[(slashIndex + 1)..];

        var dotIndex = fileName.LastIndexOf('.');
        return dotIndex >= 0 && dotIndex < fileName.Length - 1
            ? fileName[(dotIndex + 1)..]
            : string.Empty;
    }

    private static List<string> FindPackageRelationshipIssues(
        ZipArchive archive,
        IReadOnlySet<string> packageParts)
    {
        var issues = new List<string>();
        foreach (var entry in archive.Entries.Where(entry => IsPackageRelationshipPart(entry.FullName)))
        {
            var relationshipPart = NormalizePackagePart(entry.FullName);
            if (!string.Equals(relationshipPart, "_rels/.rels", StringComparison.OrdinalIgnoreCase))
            {
                var ownerPart = RelationshipSourcePart(relationshipPart);
                if (string.IsNullOrWhiteSpace(ownerPart) || !packageParts.Contains(ownerPart))
                    issues.Add($"{relationshipPart} has no owning package part {ownerPart}");
            }

            XDocument relationshipsXml;
            try
            {
                relationshipsXml = LoadPackageXml(entry);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.Xml.XmlException)
            {
                issues.Add($"{relationshipPart} is not parseable relationship XML: {ex.Message}");
                continue;
            }

            if (relationshipsXml.Root?.Name != PackageRelationshipNs + "Relationships")
            {
                issues.Add($"{relationshipPart} has an invalid Relationships root element");
                continue;
            }

            foreach (var element in relationshipsXml.Root.Elements())
            {
                if (element.Name != PackageRelationshipNs + "Relationship")
                    issues.Add($"{relationshipPart} has unexpected child element '{element.Name}'");
            }

            var relationships = relationshipsXml.Root
                .Elements(PackageRelationshipNs + "Relationship")
                .ToArray();
            if (relationships.Length == 0)
            {
                issues.Add($"{relationshipPart} has no Relationship elements");
                continue;
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var relationship in relationships)
            {
                AddPackageRelationshipIssues(relationshipPart, relationship, packageParts, ids, issues);
            }
        }

        return issues;
    }

    private static void AddPackageRelationshipIssues(
        string relationshipPart,
        XElement relationship,
        IReadOnlySet<string> packageParts,
        HashSet<string> ids,
        List<string> issues)
    {
        var id = relationship.Attribute("Id")?.Value;
        var relationshipLabel = $"{relationshipPart} Relationship {FormatRelationshipIssueId(id)}";
        if (relationship.Elements().Any())
            issues.Add($"{relationshipLabel} must not contain child elements");

        if (string.IsNullOrWhiteSpace(id))
        {
            issues.Add($"{relationshipPart} has a Relationship without Id");
        }
        else if (!string.Equals(id, id.Trim(), StringComparison.Ordinal))
        {
            issues.Add($"{relationshipPart} Relationship Id '{id}' has leading or trailing whitespace");
        }
        else if (!ids.Add(id))
        {
            issues.Add($"{relationshipPart} has duplicate Relationship Id {id}");
        }

        var type = relationship.Attribute("Type")?.Value;
        if (string.IsNullOrWhiteSpace(type))
        {
            issues.Add($"{relationshipPart} Relationship {FormatRelationshipIssueId(id)} has no Type");
        }
        else
        {
            if (!string.Equals(type, type.Trim(), StringComparison.Ordinal))
                issues.Add($"{relationshipLabel} Type has leading or trailing whitespace");

            if (!Uri.TryCreate(type.Trim(), UriKind.Absolute, out var typeUri) ||
                string.IsNullOrWhiteSpace(typeUri.Scheme))
            {
                issues.Add($"{relationshipLabel} Type '{type}' is not an absolute URI");
            }
        }

        var target = relationship.Attribute("Target")?.Value;
        if (string.IsNullOrWhiteSpace(target))
        {
            issues.Add($"{relationshipPart} Relationship {FormatRelationshipIssueId(id)} has no Target");
            return;
        }

        if (!string.Equals(target, target.Trim(), StringComparison.Ordinal))
            issues.Add($"{relationshipLabel} Target has leading or trailing whitespace");
        target = target.Trim();

        var targetMode = relationship.Attribute("TargetMode")?.Value;
        if (!string.IsNullOrWhiteSpace(targetMode) &&
            !string.Equals(targetMode, targetMode.Trim(), StringComparison.Ordinal))
        {
            issues.Add($"{relationshipLabel} TargetMode has leading or trailing whitespace");
        }

        targetMode = targetMode?.Trim();
        if (string.Equals(targetMode, "External", StringComparison.OrdinalIgnoreCase))
            return;

        if (!string.IsNullOrWhiteSpace(targetMode) &&
            !string.Equals(targetMode, "Internal", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add($"{relationshipPart} Relationship {FormatRelationshipIssueId(id)} has invalid TargetMode {targetMode}");
            return;
        }

        if (target.IndexOf('\\') >= 0)
            issues.Add($"{relationshipLabel} Target uses backslashes instead of package URI separators");

        bool resolvesInsidePackage = TryResolvePackageRelationshipTarget(
            relationshipPart,
            target,
            out var resolvedTarget,
            out var error);

        // Resolve valid package targets before applying external-URI heuristics. Root-relative OPC
        // targets can be promoted to file: URIs by Unix runtimes, but an existing resolved package
        // part is unambiguously internal regardless of that platform interpretation.
        if (resolvesInsidePackage && packageParts.Contains(resolvedTarget))
            return;

        if (IsAbsoluteRelationshipTarget(target))
        {
            issues.Add($"{relationshipPart} Relationship {FormatRelationshipIssueId(id)} targets external URI without TargetMode=External: {target}");
            return;
        }

        if (!resolvesInsidePackage)
        {
            issues.Add($"{relationshipPart} Relationship {FormatRelationshipIssueId(id)} has invalid Target {target}: {error}");
            return;
        }

        issues.Add($"{relationshipPart} Relationship {FormatRelationshipIssueId(id)} targets missing package part {resolvedTarget}");
    }

    private static string FormatRelationshipIssueId(string? id) =>
        string.IsNullOrWhiteSpace(id) ? "(no Id)" : id;

    private static bool IsPackageRelationshipPart(string part)
    {
        var normalizedPart = NormalizePackagePart(part);
        return normalizedPart.EndsWith(".rels", StringComparison.OrdinalIgnoreCase) &&
            (string.Equals(normalizedPart, "_rels/.rels", StringComparison.OrdinalIgnoreCase) ||
                normalizedPart.Contains("/_rels/", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsAbsoluteRelationshipTarget(string target)
    {
        // Classify the syntax written in the relationship, not System.Uri's platform-dependent
        // interpretation. OPC package targets never contain an authority delimiter; external web
        // and file URIs do. Keep the few standard non-authority schemes explicit.
        int authorityDelimiter = target.IndexOf("://", StringComparison.Ordinal);
        return authorityDelimiter > 0 ||
            target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ||
            target.StartsWith("urn:", StringComparison.OrdinalIgnoreCase) ||
            target.StartsWith("data:", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryResolvePackageRelationshipTarget(
        string relationshipPart,
        string target,
        out string resolvedTarget,
        out string error)
    {
        resolvedTarget = string.Empty;
        error = string.Empty;

        target = StripRelationshipTargetFragment(target.Replace('\\', '/'));
        if (string.IsNullOrWhiteSpace(target))
        {
            error = "empty internal target";
            return false;
        }

        try
        {
            target = Uri.UnescapeDataString(target);
        }
        catch (UriFormatException ex)
        {
            error = ex.Message;
            return false;
        }

        string combined;
        if (target.StartsWith("/", StringComparison.Ordinal))
        {
            combined = target.TrimStart('/');
        }
        else
        {
            var ownerPart = RelationshipSourcePart(relationshipPart);
            var ownerDirectory = ownerPart.Contains('/', StringComparison.Ordinal)
                ? ownerPart[..ownerPart.LastIndexOf('/')]
                : string.Empty;
            combined = string.IsNullOrWhiteSpace(ownerDirectory)
                ? target
                : $"{ownerDirectory}/{target}";
        }

        if (!TryNormalizePackagePathSegments(combined, out resolvedTarget))
        {
            error = "target escapes the package root";
            return false;
        }

        return !string.IsNullOrWhiteSpace(resolvedTarget);
    }

    private static string StripRelationshipTargetFragment(string target)
    {
        var fragmentIndex = target.IndexOf('#', StringComparison.Ordinal);
        var queryIndex = target.IndexOf('?', StringComparison.Ordinal);
        var endIndex = fragmentIndex < 0
            ? queryIndex
            : queryIndex < 0
                ? fragmentIndex
                : Math.Min(fragmentIndex, queryIndex);
        return endIndex < 0 ? target : target[..endIndex];
    }

    private static bool TryNormalizePackagePathSegments(string path, out string normalizedPath)
    {
        var segments = new List<string>();
        foreach (var segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
                continue;

            if (segment == "..")
            {
                if (segments.Count == 0)
                {
                    normalizedPath = string.Empty;
                    return false;
                }

                segments.RemoveAt(segments.Count - 1);
                continue;
            }

            segments.Add(segment);
        }

        normalizedPath = NormalizePackagePart(string.Join("/", segments));
        return true;
    }

    private static string NormalizePackagePart(string part) =>
        part.Replace('\\', '/').TrimStart('/');

    private static string NormalizePackagePath(string path)
    {
        var parts = new List<string>();
        foreach (var part in path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".")
                continue;
            if (part == "..")
            {
                if (parts.Count > 0)
                    parts.RemoveAt(parts.Count - 1);
                continue;
            }

            parts.Add(part);
        }

        return string.Join("/", parts);
    }

    private static bool IsFidelityCriticalPart(string path) =>
        path.StartsWith("xl/drawings/", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("xl/workbook.xml", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("xl/theme/theme1.xml", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("xl/styles.xml", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("xl/worksheets/sheet1.xml", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/charts/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/media/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/tables/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/pivot", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/slicer", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/timeline", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/externalLinks/", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("xl/calcChain.xml", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("xl/connections.xml", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/query", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/queries/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/model/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/datamodel/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/powerpivot/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/richData/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/threadedComments/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/persons/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/revisionHeaders/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/revisions/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/activeX/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/ctrlProps/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/webextensions/", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("xl/webPublishItems.xml", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/diagrams/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/chartsheets/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/dialogSheets/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/macroSheets/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/printerSettings/", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("xl/vbaProject.bin", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("xl/vbaProjectSignature.bin", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("xl/volatileDependencies.xml", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("docProps/core.xml", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("docProps/app.xml", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("docProps/custom.xml", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("docProps/thumbnail.png", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("docProps/thumbnail.jpeg", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/embeddings/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("customXml/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("customUI/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("_xmlsignatures/", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".rels", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> ReadRelationshipTargets(ZipArchiveEntry relsEntry)
    {
        var relsXml = LoadPackageXml(relsEntry);

        XNamespace relNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        return relsXml.Root?
            .Elements(relNs + "Relationship")
            .Select(rel => rel.Attribute("Target")?.Value)
            .Where(target => !string.IsNullOrWhiteSpace(target))
            .Where(target => !target!.Contains("/package/services/metadata/core-properties/", StringComparison.OrdinalIgnoreCase))
            .Select(target => $"{relsEntry.FullName.Replace('\\', '/')}=>{target!.Replace('\\', '/')}")
            .ToArray() ?? [];
    }

    private static IEnumerable<string> ReadRelationshipDetails(ZipArchiveEntry relsEntry)
    {
        var relsXml = LoadPackageXml(relsEntry);

        XNamespace relNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        return relsXml.Root?
            .Elements(relNs + "Relationship")
            .Where(rel => !string.IsNullOrWhiteSpace(rel.Attribute("Target")?.Value))
            .Where(rel => !rel.Attribute("Target")!.Value.Contains("/package/services/metadata/core-properties/", StringComparison.OrdinalIgnoreCase))
            .Select(rel =>
            {
                var target = NormalizeRelationshipDetailTarget(
                    relsEntry.FullName.Replace('\\', '/'),
                    rel.Attribute("Target")!.Value,
                    rel.Attribute("TargetMode")?.Value);
                var type = rel.Attribute("Type")?.Value ?? "";
                var targetMode = rel.Attribute("TargetMode")?.Value ?? "";
                return $"{relsEntry.FullName.Replace('\\', '/')}=>{target}|type={type}|mode={targetMode}";
            })
            .ToArray() ?? [];
    }

    private static string NormalizeRelationshipDetailTarget(string relsPath, string target, string? targetMode)
    {
        target = target.Replace('\\', '/');
        if (string.Equals(targetMode, "External", StringComparison.OrdinalIgnoreCase))
            return target;

        if (target.StartsWith("/", StringComparison.Ordinal))
            return NormalizePackagePath(target);

        var sourcePart = RelationshipSourcePart(relsPath);
        var sourceDirectory = Path.GetDirectoryName(sourcePart)?.Replace('\\', '/') ?? string.Empty;
        return NormalizePackagePath(string.IsNullOrWhiteSpace(sourceDirectory)
            ? target
            : $"{sourceDirectory}/{target}");
    }

    private static IEnumerable<string> ReadCriticalContentTypeOverrides(ZipArchive archive)
    {
        var entry = archive.GetEntry("[Content_Types].xml");
        if (entry is null)
            return [];

        var contentTypesXml = LoadPackageXml(entry);

        XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
        return contentTypesXml.Root?
            .Elements(contentTypeNs + "Override")
            .Select(element => new
            {
                PartName = element.Attribute("PartName")?.Value,
                ContentType = element.Attribute("ContentType")?.Value
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.PartName))
            .Select(item => new
            {
                PartName = item.PartName!.TrimStart('/').Replace('\\', '/'),
                ContentType = item.ContentType ?? ""
            })
            .Where(item => IsFidelityCriticalPart(item.PartName))
            .Select(item => $"/{item.PartName}=>{item.ContentType}")
            .ToArray() ?? [];
    }

    private static XDocument LoadPackageXml(ZipArchiveEntry entry) =>
        XlsxPackageTestFixtures.LoadPackageXml(entry);

    private static XDocument LoadPackageXml(ZipArchive archive, string entryName) =>
        XlsxPackageTestFixtures.LoadPackageXml(archive, entryName, entryName);

    private static void ReplacePackageXml(ZipArchive archive, string entryName, XDocument document)
    {
        archive.GetEntry(entryName)?.Delete();
        var entry = archive.CreateEntry(entryName);
        using var stream = entry.Open();
        document.Save(stream);
    }

    private static void WritePackageEntry(ZipArchive archive, string entryName, string content)
    {
        try
        {
            archive.GetEntry(entryName)?.Delete();
        }
        catch (NotSupportedException)
        {
            // ZipArchiveMode.Create does not allow entry lookup.
        }

        var entry = archive.CreateEntry(entryName);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }

}
