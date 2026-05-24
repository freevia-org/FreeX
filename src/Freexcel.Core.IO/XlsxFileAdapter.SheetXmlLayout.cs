using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;

using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

public sealed partial class XlsxFileAdapter
{
    // Worksheet XML layout metadata loading and XLSX XML helper methods.
    private sealed record SheetXmlLayout(
        HashSet<uint> HiddenRows,
        HashSet<uint> HiddenCols,
        bool IsProtected,
        string? ProtectionPasswordHash,
        IReadOnlyList<GridRange> AllowEditRanges,
        WorksheetViewMode ViewMode,
        bool ShowGridlines,
        bool ShowHeadings,
        bool ShowRulers,
        int ZoomPercent,
        bool ShowFormulas,
        double? DefaultColumnWidth,
        double? DefaultRowHeight,
        bool FullCalculationOnLoad,
        WorksheetPhoneticProperties? PhoneticProperties,
        string? PaneState,
        uint? PaneRowSplit,
        uint? PaneColumnSplit,
        uint? ViewTopRow,
        uint? ViewLeftCol,
        uint? ActiveRow,
        uint? ActiveCol,
        bool? UsePrinterDefaults,
        int? PrintCopies,
        WorksheetBackgroundImage? BackgroundImage,
        XlsxHeaderFooterPictureSets HeaderFooterPictures,
        Dictionary<uint, int> RowOutlineLevels,
        Dictionary<uint, int> ColOutlineLevels,
        HashSet<uint> GroupHiddenRows,
        HashSet<uint> GroupHiddenCols,
        Dictionary<uint, double> RowHeights,
        Dictionary<uint, double> ColumnWidths,
        IReadOnlyList<(uint Row, uint Col, string Text)> Comments,
        IReadOnlyList<XlsxChartPackagePart> ChartParts,
        IReadOnlyList<XlsxPicturePackagePart> PictureParts,
        IReadOnlyList<XlsxTextBoxPackagePart> TextBoxParts,
        IReadOnlyList<XlsxShapePackagePart> ShapeParts,
        IReadOnlyList<SparklineModel> Sparklines,
        IReadOnlyList<ConditionalFormat> AdvancedConditionalFormats,
        IReadOnlyList<DataValidationNativeMetadata> DataValidationNativeMetadata,
        IgnoredErrorLayout IgnoredErrors,
        IReadOnlyList<CellAddress> CellWatches,
        IReadOnlyList<WorkbookScenario> Scenarios,
        IReadOnlyList<XlsxWorksheetCustomViewState> CustomViews,
        IReadOnlyList<WorksheetCustomProperty> CustomProperties,
        Dictionary<(uint Row, uint Col), ErrorValue> CachedFormulaErrors,
        IReadOnlyList<(uint Row, uint Col, int StyleIndex)> ExplicitStyleOnlyCells,
        string? CodeName);

    private static Dictionary<string, SheetXmlLayout> LoadSheetXmlLayout(Stream xlsxStream)
    {
        var result = new Dictionary<string, SheetXmlLayout>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Read, leaveOpen: true);
            var workbookEntry = archive.GetEntry("xl/workbook.xml");
            var relsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels");
            if (workbookEntry is null || relsEntry is null)
                return result;

            var workbookXml = LoadXml(workbookEntry);
            var relsXml = LoadXml(relsEntry);

            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

            var relTargets = XlsxRelationshipReader.ReadTargets(
                relsXml,
                packageRelNs,
                XlsxPackagePath.NormalizeWorkbookTarget);

            var differentialStyles = XlsxDifferentialStyleReader.ReadAll(archive, workbookNs);

            foreach (var sheetElement in workbookXml.Root?.Element(workbookNs + "sheets")?.Elements(workbookNs + "sheet") ?? [])
            {
                var name = sheetElement.Attribute("name")?.Value;
                var relId = sheetElement.Attribute(relNs + "id")?.Value;
                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(relId))
                    continue;
                if (!relTargets.TryGetValue(relId, out var worksheetPath))
                    continue;

                var worksheetEntry = archive.GetEntry(worksheetPath);
                if (worksheetEntry is null)
                    continue;

                result[name] = ReadHiddenSheetLayout(archive, worksheetPath, worksheetEntry, differentialStyles);
            }
        }
        catch
        {
            // Worksheet XML metadata is best-effort; ClosedXML still loads workbook content.
        }

        return result;
    }

    private static XDocument LoadXml(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    private static void ReplacePackageXml(ZipArchive archive, string entryName, XDocument document)
        => XlsxPackageXmlEditor.ReplaceXml(archive, entryName, document);

    private static Dictionary<string, string> LoadRelationshipTargets(
        ZipArchive archive,
        string relsPath,
        string sourcePart,
        XNamespace packageRelNs) =>
        XlsxRelationshipReader.LoadTargets(archive, relsPath, sourcePart, packageRelNs);

    private static int? ReadIntAttribute(XElement element, string attributeName) =>
        XlsxXmlAttributeReader.ReadIntAttribute(element, attributeName);

    private static double? ReadDoubleAttribute(XElement element, string attributeName) =>
        XlsxXmlAttributeReader.ReadDoubleAttribute(element, attributeName);

    private static bool ReadBoolAttribute(XElement? element, string attributeName, bool defaultValue = false) =>
        XlsxXmlAttributeReader.ReadBoolAttribute(element, attributeName, defaultValue);

    private static CfThresholdType FromCfvoType(string? type) =>
        type?.ToLowerInvariant() switch
        {
            "max" => CfThresholdType.Max,
            "num" => CfThresholdType.Number,
            "percent" => CfThresholdType.Percent,
            "percentile" => CfThresholdType.Percentile,
            "formula" => CfThresholdType.Formula,
            _ => CfThresholdType.Min
        };

    private static void EnsureContentType(ZipArchive archive, string extension, string contentType)
        => XlsxPackageXmlEditor.EnsureDefaultContentType(archive, extension, contentType);

    private static void EnsureSpecificContentType(ZipArchive archive, string partName, string contentType)
        => XlsxPackageXmlEditor.EnsureSpecificContentType(archive, partName, contentType);

    private static SheetXmlLayout ReadHiddenSheetLayout(
        ZipArchive archive,
        string worksheetPath,
        ZipArchiveEntry worksheetEntry,
        IReadOnlyList<CellStyle> differentialStyles)
    {
        var hiddenRows = new HashSet<uint>();
        var hiddenCols = new HashSet<uint>();
        var rowOutlineLevels = new Dictionary<uint, int>();
        var colOutlineLevels = new Dictionary<uint, int>();
        var groupHiddenRows = new HashSet<uint>();
        var groupHiddenCols = new HashSet<uint>();
        var rowHeights = new Dictionary<uint, double>();
        var columnWidths = new Dictionary<uint, double>();
        var worksheetXml = LoadXml(worksheetEntry);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        foreach (var row in worksheetXml.Descendants(worksheetNs + "row"))
        {
            if (!uint.TryParse(row.Attribute("r")?.Value, out var rowNumber))
                continue;

            if (IsTruthy(row.Attribute("hidden")?.Value))
                hiddenRows.Add(rowNumber);

            if (ParseOptionalDouble(row.Attribute("ht")?.Value) is { } heightPoints && heightPoints > 0)
                rowHeights[rowNumber] = heightPoints * (96.0 / 72.0);

            var outlineStr = row.Attribute("outlineLevel")?.Value;
            if (int.TryParse(outlineStr, out var outlineLevel) && outlineLevel > 0)
            {
                rowOutlineLevels[rowNumber] = outlineLevel;
                if (IsTruthy(row.Attribute("collapsed")?.Value))
                    groupHiddenRows.Add(rowNumber);
            }
        }

        foreach (var col in worksheetXml.Descendants(worksheetNs + "col"))
        {
            if (!uint.TryParse(col.Attribute("min")?.Value, out var min))
                continue;
            if (!uint.TryParse(col.Attribute("max")?.Value, out var max))
                continue;
            if (min > max)
                continue;

            if (IsTruthy(col.Attribute("hidden")?.Value))
            {
                for (var colNumber = min; colNumber <= max; colNumber++)
                    hiddenCols.Add(colNumber);
            }

            var colOutlineStr = col.Attribute("outlineLevel")?.Value;
            if (int.TryParse(colOutlineStr, out var colOutlineLevel) && colOutlineLevel > 0)
            {
                var collapsed = IsTruthy(col.Attribute("collapsed")?.Value);
                for (var colNumber = min; colNumber <= max; colNumber++)
                {
                    colOutlineLevels[colNumber] = colOutlineLevel;
                    if (collapsed)
                        groupHiddenCols.Add(colNumber);
                }
            }

            if (IsTruthy(col.Attribute("customWidth")?.Value) &&
                ParseOptionalDouble(col.Attribute("width")?.Value) is { } width &&
                width > 0)
            {
                if (col.Attribute("style") is not null && width <= 9.2)
                    continue;

                width = Math.Floor(width);
                for (var colNumber = min; colNumber <= max; colNumber++)
                    columnWidths[colNumber] = width;
            }
        }

        var protection = worksheetXml.Root?.Element(worksheetNs + "sheetProtection");
        var isProtected = IsTruthy(protection?.Attribute("sheet")?.Value);
        var passwordHash =
            protection?.Attribute("password")?.Value ??
            protection?.Attribute("hashValue")?.Value;
        var allowEditRanges = XlsxAllowEditRangeMapper.Read(worksheetXml, worksheetNs);

        var sheetView = worksheetXml.Root?
            .Element(worksheetNs + "sheetViews")?
            .Elements(worksheetNs + "sheetView")
            .FirstOrDefault();
        var sheetCalcPr = worksheetXml.Root?.Element(worksheetNs + "sheetCalcPr");
        var sheetFormatPr = worksheetXml.Root?.Element(worksheetNs + "sheetFormatPr");
        var pageSetup = worksheetXml.Root?.Element(worksheetNs + "pageSetup");
        var phoneticPr = worksheetXml.Root?.Element(worksheetNs + "phoneticPr");
        var pane = sheetView?.Element(worksheetNs + "pane");
        var viewTopLeft = ParseOptionalCellReference(sheetView?.Attribute("topLeftCell")?.Value);
        var activeCell = ParseOptionalCellReference(
            sheetView?
                .Elements(worksheetNs + "selection")
                .Select(selection => selection.Attribute("activeCell")?.Value)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)));
        var background = XlsxWorksheetBackgroundReaderWriter.Read(archive, worksheetPath, worksheetXml);
        var headerFooterPictures = XlsxHeaderFooterPictureReaderWriter.Read(archive, worksheetPath, worksheetXml);
        var drawingParts = XlsxWorksheetDrawingPartReader.ReadParts(archive, worksheetPath, worksheetXml);
        var sparklines = XlsxSparklineMapper.Read(worksheetXml);
        var advancedConditionalFormats = ReadAdvancedConditionalFormats(worksheetXml, worksheetNs, differentialStyles);
        var dataValidationNativeMetadata = XlsxDataValidationNativeMetadataMapper.Read(worksheetXml, worksheetNs);
        var ignoredErrors = XlsxWorksheetDiagnosticsMapper.ReadIgnoredErrors(worksheetXml, worksheetNs);
        var cellWatches = XlsxWorksheetDiagnosticsMapper.ReadCellWatches(worksheetXml, worksheetNs);
        var scenarios = XlsxWorksheetScenarioMapper.Read(worksheetXml, worksheetNs);
        var customViews = XlsxCustomViewMapper.ReadWorksheetViews(worksheetXml, worksheetNs);
        var customProperties = XlsxWorksheetCustomPropertyMapper.Read(worksheetXml, worksheetNs);
        var cachedFormulaErrors = ReadCachedFormulaErrors(worksheetXml, worksheetNs);
        var explicitStyleOnlyCells = ReadExplicitStyleOnlyCells(worksheetXml, worksheetNs);
        var comments = XlsxWorksheetCommentReader.Read(archive, worksheetPath);
        var codeName = worksheetXml.Root?
            .Element(worksheetNs + "sheetPr")?
            .Attribute("codeName")?
            .Value;

        return new SheetXmlLayout(
            hiddenRows,
            hiddenCols,
            isProtected,
            passwordHash,
            allowEditRanges,
            ParseWorksheetViewMode(sheetView?.Attribute("view")?.Value),
            !IsFalse(sheetView?.Attribute("showGridLines")?.Value),
            !IsFalse(sheetView?.Attribute("showRowColHeaders")?.Value),
            !IsFalse(sheetView?.Attribute("showRuler")?.Value),
            ParseZoomPercent(sheetView?.Attribute("zoomScale")?.Value),
            IsTruthy(sheetView?.Attribute("showFormulas")?.Value),
            ParseOptionalDouble(sheetFormatPr?.Attribute("defaultColWidth")?.Value),
            ParseOptionalDouble(sheetFormatPr?.Attribute("defaultRowHeight")?.Value) is { } defaultRowHeightPoints
                ? defaultRowHeightPoints * (96.0 / 72.0)
                : null,
            XlsxWorksheetCalculationPropertyMapper.ReadFullCalculationOnLoad(sheetCalcPr),
            XlsxWorksheetPhoneticPropertyMapper.Read(phoneticPr),
            pane?.Attribute("state")?.Value,
            ParsePaneSplit(pane?.Attribute("ySplit")?.Value),
            ParsePaneSplit(pane?.Attribute("xSplit")?.Value),
            viewTopLeft?.Row,
            viewTopLeft?.Col,
            activeCell?.Row,
            activeCell?.Col,
            ParseOptionalBool(pageSetup?.Attribute("usePrinterDefaults")?.Value),
            ParseOptionalPositiveInt(pageSetup?.Attribute("copies")?.Value),
            background,
            headerFooterPictures,
            rowOutlineLevels,
            colOutlineLevels,
            groupHiddenRows,
            groupHiddenCols,
            rowHeights,
            columnWidths,
            comments,
            drawingParts.ChartParts,
            drawingParts.PictureParts,
            drawingParts.TextBoxParts,
            drawingParts.ShapeParts,
            sparklines,
            advancedConditionalFormats,
            dataValidationNativeMetadata,
            ignoredErrors,
            cellWatches,
            scenarios,
            customViews,
            customProperties,
            cachedFormulaErrors,
            explicitStyleOnlyCells,
            codeName);
    }

    private static IReadOnlyList<(uint Row, uint Col, int StyleIndex)> ReadExplicitStyleOnlyCells(XDocument worksheetXml, XNamespace worksheetNs)
    {
        var result = new List<(uint Row, uint Col, int StyleIndex)>();

        foreach (var cell in worksheetXml.Descendants(worksheetNs + "c"))
        {
            if (!int.TryParse(cell.Attribute("s")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var styleIndex) ||
                cell.Element(worksheetNs + "f") is not null ||
                cell.Element(worksheetNs + "v") is not null ||
                cell.Element(worksheetNs + "is") is not null)
            {
                continue;
            }

            var reference = cell.Attribute("r")?.Value;
            if (string.IsNullOrWhiteSpace(reference) || !CellAddress.TryParse(reference, SheetId.New(), out var address))
                continue;

            result.Add((address.Row, address.Col, styleIndex));
        }

        return result;
    }

    private static Dictionary<(uint Row, uint Col), ErrorValue> ReadCachedFormulaErrors(XDocument worksheetXml, XNamespace worksheetNs)
    {
        var result = new Dictionary<(uint Row, uint Col), ErrorValue>();

        foreach (var cell in worksheetXml.Descendants(worksheetNs + "c"))
        {
            if (!string.Equals(cell.Attribute("t")?.Value, "e", StringComparison.OrdinalIgnoreCase))
                continue;
            if (cell.Element(worksheetNs + "f") is null)
                continue;
            var rawValue = cell.Element(worksheetNs + "v")?.Value;
            if (string.IsNullOrWhiteSpace(rawValue))
                continue;
            var reference = cell.Attribute("r")?.Value;
            if (string.IsNullOrWhiteSpace(reference) || !CellAddress.TryParse(reference, SheetId.New(), out var address))
                continue;

            result[(address.Row, address.Col)] = MapCachedFormulaError(rawValue);
        }

        return result;
    }

    private static ErrorValue MapCachedFormulaError(string rawValue) =>
        rawValue.ToUpperInvariant() switch
        {
            "#NULL!" => ErrorValue.Null,
            "#DIV/0!" => ErrorValue.DivByZero,
            "#VALUE!" => ErrorValue.Value,
            "#REF!" => ErrorValue.Ref,
            "#NAME?" => ErrorValue.Name,
            "#NUM!" => ErrorValue.Num,
            "#N/A" => ErrorValue.NA,
            "#SPILL!" => ErrorValue.Spill,
            "#CALC!" => ErrorValue.Calc,
            _ => new ErrorValue(rawValue)
        };

    private static bool TryParseSqrefToken(string token, SheetId sheet, out GridRange range)
    {
        range = default;
        var parts = token.Split(':');
        if (parts.Length == 1)
        {
            if (!CellAddress.TryParse(parts[0], sheet, out var address))
                return false;

            range = new GridRange(address, address);
            return true;
        }

        if (parts.Length == 2 &&
            CellAddress.TryParse(parts[0], sheet, out var start) &&
            CellAddress.TryParse(parts[1], sheet, out var end))
        {
            range = new GridRange(start, end);
            return true;
        }

        return false;
    }

    private static uint? ParsePaneSplit(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (uint.TryParse(value, out var integer))
            return integer;

        if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var floating) &&
            floating > 0 &&
            floating <= uint.MaxValue)
            return (uint)Math.Round(floating);

        return null;
    }

    private static CellAddress? ParseOptionalCellReference(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return null;

        return CellAddress.TryParse(reference.Split(':')[0], SheetId.New(), out var address)
            ? address
            : null;
    }

    private static bool IsTruthy(string? value) =>
        value is "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    private static bool IsFalse(string? value) =>
        value is "0" || string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);

    private static bool? ParseOptionalBool(string? value)
    {
        if (IsTruthy(value))
            return true;
        if (IsFalse(value))
            return false;
        return null;
    }

    private static int? ParseOptionalPositiveInt(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed
            : null;

    private static int ParseZoomPercent(string? value) =>
        int.TryParse(value, out var zoom) && zoom is >= 10 and <= 400 ? zoom : 100;

    private static double? ParseOptionalDouble(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) &&
        double.IsFinite(parsed) &&
        parsed > 0
            ? parsed
            : null;

    private static WorksheetViewMode ParseWorksheetViewMode(string? value) =>
        value switch
        {
            "pageBreakPreview" => WorksheetViewMode.PageBreakPreview,
            "pageLayout" => WorksheetViewMode.PageLayout,
            _ => WorksheetViewMode.Normal
        };

    private static bool IsValidWorksheetRow(uint row) =>
        row is >= 1 and <= CellAddress.MaxRow;

    private static bool IsValidWorksheetColumn(uint column) =>
        column is >= 1 and <= CellAddress.MaxCol;

    private static bool IsValidRepeatRange(WorksheetRepeatRange range, uint max) =>
        range.Start >= 1 && range.End >= range.Start && range.End <= max;

    private static bool IsSupportedTextRotation(int rotation) =>
        rotation == 255 || rotation is >= -90 and <= 90;

    private static uint ValidFrozenRowsOrZero(uint row) =>
        row <= CellAddress.MaxRow ? row : 0;

    private static uint ValidFrozenColumnsOrZero(uint column) =>
        column <= CellAddress.MaxCol ? column : 0;

    private static bool IsSupportedFontSize(double fontSize) =>
        double.IsFinite(fontSize) && fontSize is >= 1 and <= 409;

}
