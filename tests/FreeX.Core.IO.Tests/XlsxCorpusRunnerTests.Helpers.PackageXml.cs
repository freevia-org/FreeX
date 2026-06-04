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
                PublicWorksheetElements(archive, "hyperlink").Should().NotBeEmpty(row.Id);

            if (tags.Contains("merged-cells"))
                PublicWorksheetElements(archive, "mergeCell").Should().NotBeEmpty(row.Id);

            if (tags.Contains("inline-strings"))
                PublicWorksheetCells(archive)
                    .Any(cell =>
                        string.Equals(cell.Attribute("t")?.Value, "inlineStr", StringComparison.Ordinal) ||
                        cell.Element(WorksheetNs + "is") is not null)
                    .Should()
                    .BeTrue(row.Id);

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

            if (tags.Contains("unsupported-sheet-types"))
                archive.Entries.Should().Contain(entry => entry.FullName.StartsWith("xl/chartsheets/", StringComparison.Ordinal), row.Id);
        }
        finally
        {
            if (package.CanSeek)
                package.Position = originalPosition;
        }
    }

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
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        workbookEntry.Should().NotBeNull("public workbook packages should contain workbook.xml");

        return LoadPackageXml(workbookEntry!)
            .Descendants(WorksheetNs + "sheet")
            .Select(sheet => sheet.Attribute("name")?.Value ?? "")
            .Where(name => name.Length > 0)
            .ToArray();
    }

    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
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
               tags.Contains("cell-types") ||
               (tags.Contains("sheet-names") && tags.Contains("boundary")) ||
               tags.Contains("unsupported-sheet-types");
    }

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

                // OPC part names are compared case-insensitively, so two names differing only by case
                // (e.g. ClosedXML's xl/drawings/vmldrawing2.vml vs Excel's xl/drawings/vmlDrawing2.vml)
                // make the package unreadable in Excel even though the zip entries are distinct.
                entries.Select(name => name.ToLowerInvariant())
                    .Should().OnlyHaveUniqueItems($"{because}: OPC part names must be unique case-insensitively");

                var entrySet = entries.ToHashSet(StringComparer.OrdinalIgnoreCase);
                archive.GetEntry("[Content_Types].xml").Should().NotBeNull(because);
                foreach (var xmlEntry in archive.Entries.Where(entry =>
                             entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ||
                             entry.FullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase)))
                {
                    using var xmlStream = xmlEntry.Open();
                    var load = () => XDocument.Load(xmlStream);
                    load.Should().NotThrow($"{because}: {xmlEntry.FullName} should be parseable XML");
                }

                foreach (var relsEntry in archive.Entries.Where(entry => entry.FullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase)))
                {
                    var sourcePart = RelationshipSourcePart(relsEntry.FullName.Replace('\\', '/'));
                    var sourceDirectory = Path.GetDirectoryName(sourcePart)?.Replace('\\', '/') ?? string.Empty;
                    var relsXml = LoadPackageXml(relsEntry);
                    XNamespace relNs = "http://schemas.openxmlformats.org/package/2006/relationships";
                    var relationships = relsXml.Root?.Elements(relNs + "Relationship").ToArray() ?? [];
                    relationships.Should().NotBeEmpty($"{because}: {relsEntry.FullName} should contain at least one relationship");
                    foreach (var relationship in relationships)
                    {
                        if (string.Equals(relationship.Attribute("TargetMode")?.Value, "External", StringComparison.OrdinalIgnoreCase))
                            continue;

                        var target = relationship.Attribute("Target")?.Value;
                        if (string.IsNullOrWhiteSpace(target) || target.StartsWith("/", StringComparison.Ordinal))
                            continue;

                        target = Uri.UnescapeDataString(target);
                        var resolved = NormalizePackagePath(string.IsNullOrWhiteSpace(sourceDirectory)
                            ? target
                            : $"{sourceDirectory}/{target}");
                        entrySet.Should().Contain(resolved, $"{because}: {relsEntry.FullName} relationship target should exist");
                    }
                }
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
        path.Equals("docProps/core.xml", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("docProps/app.xml", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("docProps/custom.xml", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/embeddings/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("customXml/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("customUI/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("_xmlsignatures/", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".rels", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> ReadRelationshipTargets(ZipArchiveEntry relsEntry)
    {
        XDocument relsXml;
        using (var stream = relsEntry.Open())
            relsXml = XDocument.Load(stream);

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
        XDocument relsXml;
        using (var stream = relsEntry.Open())
            relsXml = XDocument.Load(stream);

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

        XDocument contentTypesXml;
        using (var stream = entry.Open())
            contentTypesXml = XDocument.Load(stream);

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

    private static XDocument LoadPackageXml(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

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
