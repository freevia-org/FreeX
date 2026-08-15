using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetRowColumnLayoutReader
{
    private const double PointsToDips = 96.0 / 72.0;
    private const double CachedAutoFitRowHeightScale = 14.5 / 15.0;
    private static readonly SheetId ParseOnlySheetId = default;

    private static readonly HashSet<string> ModeledRowAttributes = new(StringComparer.Ordinal)
    {
        "r",
        "spans",
        "s",
        "customFormat",
        "ht",
        "hidden",
        "outlineLevel",
        "collapsed",
        "thickTop",
        "thickBot",
        "ph",
        "customHeight",
        "dyDescent"
    };

    private static readonly HashSet<string> ModeledCellAttributes = new(StringComparer.Ordinal)
    {
        "r",
        "s",
        "t"
    };

    public static XlsxWorksheetRowColumnLayout Read(XDocument worksheetXml, XNamespace worksheetNs)
        => ReadSheetDataLayout(worksheetXml, worksheetNs).RowColumnLayout;

    public static XlsxWorksheetSheetDataLayout ReadSheetDataLayout(XDocument worksheetXml, XNamespace worksheetNs)
    {
        var hiddenRows = new HashSet<uint>();
        var hiddenCols = new HashSet<uint>();
        var rowOutlineLevels = new Dictionary<uint, int>();
        var colOutlineLevels = new Dictionary<uint, int>();
        var groupHiddenRows = new HashSet<uint>();
        var groupHiddenCols = new HashSet<uint>();
        var collapsedAnchorRows = new HashSet<uint>();
        var collapsedAnchorCols = new HashSet<uint>();
        var rowHeights = new Dictionary<uint, double>();
        var columnWidths = new Dictionary<uint, double>();
        var styledRows = new HashSet<uint>();
        var styledColumns = new HashSet<uint>();
        var explicitStyleOnlyCells = new List<(uint Row, uint Col, int StyleIndex)>();
        var explicitPopulatedCellStyles = new List<(uint Row, uint Col, int StyleIndex)>();
        var cachedFormulaErrors = new Dictionary<(uint Row, uint Col), ErrorValue>();
        var sharedStringValueCells = new List<(uint Row, uint Col)>();
        var styleOnlyStyleIndexes = new HashSet<string>(StringComparer.Ordinal);
        var hasDuplicateStyleOnlyCellStyleIndexes = false;
        var hasStyleOnlyCells = false;
        var populatedCellCount = 0;

        var root = worksheetXml.Root;
        if (root is not null)
        {
            var rowName = worksheetNs + "row";
            var cellName = worksheetNs + "c";
            var formulaName = worksheetNs + "f";
            var valueName = worksheetNs + "v";
            var inlineStringName = worksheetNs + "is";
            foreach (var row in root.Element(worksheetNs + "sheetData")?.Elements(rowName) ?? [])
            {
                if (uint.TryParse(row.Attribute("r")?.Value, out var rowNumber))
                    ReadRowLayout(row, rowNumber, hiddenRows, rowOutlineLevels, groupHiddenRows, collapsedAnchorRows, rowHeights, styledRows);

                foreach (var cell in row.Elements(cellName))
                {
                    hasStyleOnlyCells |= XlsxWorksheetCellLayoutReader.ReadCell(
                        cell,
                        formulaName,
                        valueName,
                        inlineStringName,
                        explicitStyleOnlyCells,
                        explicitPopulatedCellStyles,
                        cachedFormulaErrors,
                        styleOnlyStyleIndexes,
                        ref hasDuplicateStyleOnlyCellStyleIndexes,
                        ref populatedCellCount,
                        sharedStringValueCells);
                }
            }

            foreach (var cols in root.Elements(worksheetNs + "cols"))
            {
                foreach (var col in cols.Elements(worksheetNs + "col"))
                    ReadColumnLayout(col, hiddenCols, colOutlineLevels, groupHiddenCols, collapsedAnchorCols, columnWidths, styledColumns);
            }
        }

        var layout = new XlsxWorksheetSheetDataLayout(
            new XlsxWorksheetRowColumnLayout(
                hiddenRows,
                hiddenCols,
                rowOutlineLevels,
                colOutlineLevels,
                groupHiddenRows,
                groupHiddenCols,
                rowHeights,
                columnWidths,
                collapsedAnchorRows,
                collapsedAnchorCols,
                styledRows,
                styledColumns),
            new XlsxWorksheetCellLayout(
                cachedFormulaErrors,
                explicitPopulatedCellStyles,
                explicitStyleOnlyCells,
                hasStyleOnlyCells,
                hasDuplicateStyleOnlyCellStyleIndexes,
                populatedCellCount,
                sharedStringValueCells));

        var outlinePr = root?
            .Element(worksheetNs + "sheetPr")?
            .Element(worksheetNs + "outlinePr");
        ClassifyCollapsedOutlineHidden(
            layout.RowColumnLayout,
            ReadSummaryDirection(outlinePr?.Attribute("summaryBelow")?.Value),
            ReadSummaryDirection(outlinePr?.Attribute("summaryRight")?.Value));
        return layout;
    }

    public static XlsxWorksheetSheetDataLayout ReadSheetDataLayout(
        XmlReader reader,
        XNamespace worksheetNs,
        bool detectPreservableSourceSheetDataMetadata = true)
    {
        var hiddenRows = new HashSet<uint>();
        var hiddenCols = new HashSet<uint>();
        var rowOutlineLevels = new Dictionary<uint, int>();
        var colOutlineLevels = new Dictionary<uint, int>();
        var groupHiddenRows = new HashSet<uint>();
        var groupHiddenCols = new HashSet<uint>();
        var collapsedAnchorRows = new HashSet<uint>();
        var collapsedAnchorCols = new HashSet<uint>();
        var rowHeights = new Dictionary<uint, double>();
        var columnWidths = new Dictionary<uint, double>();
        var styledRows = new HashSet<uint>();
        var styledColumns = new HashSet<uint>();
        var explicitStyleOnlyCells = new List<(uint Row, uint Col, int StyleIndex)>();
        var explicitPopulatedCellStyles = new List<(uint Row, uint Col, int StyleIndex)>();
        var cachedFormulaErrors = new Dictionary<(uint Row, uint Col), ErrorValue>();
        var sharedStringValueCells = new List<(uint Row, uint Col)>();
        var styleOnlyStyleIndexes = new HashSet<string>(StringComparer.Ordinal);
        var hasDuplicateStyleOnlyCellStyleIndexes = false;
        var hasStyleOnlyCells = false;
        var populatedCellCount = 0;
        var hasPreservableSourceSheetDataMetadata = false;

        if (reader.NodeType == XmlNodeType.None)
            reader.Read();
        if (reader.NodeType != XmlNodeType.Element)
            reader.MoveToContent();
        if (reader.NodeType != XmlNodeType.Element ||
            reader.LocalName != "sheetData" ||
            !string.Equals(reader.NamespaceURI, worksheetNs.NamespaceName, StringComparison.Ordinal))
        {
            return CreateLayout();
        }

        if (detectPreservableSourceSheetDataMetadata && HasAnyNonNamespaceAttribute(reader))
            hasPreservableSourceSheetDataMetadata = true;
        if (reader.IsEmptyElement)
            return CreateLayout();

        var sheetDataDepth = reader.Depth;
        var rowDepth = -1;
        var cellDepth = -1;
        var valueDepth = -1;
        var inlineStringDepth = -1;
        var currentCellIsInlineString = false;
        string? currentReference = null;
        string? currentRawStyleIndex = null;
        var currentHasStyle = false;
        var currentStyleIndex = 0;
        var currentIsErrorType = false;
        var currentIsSharedStringType = false;
        var currentHasFormula = false;
        var currentHasValue = false;
        var currentHasInlineString = false;
        string? currentRawValue = null;

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement)
            {
                if (reader.Depth == valueDepth)
                    valueDepth = -1;

                if (reader.Depth == inlineStringDepth)
                    inlineStringDepth = -1;

                if (reader.Depth == cellDepth)
                {
                    FinalizeCurrentCell();
                    cellDepth = -1;
                    currentCellIsInlineString = false;
                }

                if (reader.Depth == rowDepth)
                    rowDepth = -1;

                if (reader.Depth == sheetDataDepth)
                    break;

                continue;
            }

            if (reader.NodeType is XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.SignificantWhitespace)
            {
                if (valueDepth >= 0 &&
                    reader.Depth == valueDepth + 1 &&
                    currentIsErrorType)
                {
                    currentRawValue += reader.Value;
                }

                continue;
            }

            if (reader.NodeType != XmlNodeType.Element)
                continue;

            if (detectPreservableSourceSheetDataMetadata &&
                !string.Equals(reader.NamespaceURI, worksheetNs.NamespaceName, StringComparison.Ordinal))
            {
                hasPreservableSourceSheetDataMetadata = true;
                continue;
            }

            if (reader.Depth == sheetDataDepth + 1)
            {
                if (reader.LocalName != "row")
                {
                    if (detectPreservableSourceSheetDataMetadata)
                        hasPreservableSourceSheetDataMetadata = true;
                    continue;
                }

                if (detectPreservableSourceSheetDataMetadata &&
                    HasNativeOnlyLocalAttributes(reader, ModeledRowAttributes))
                {
                    hasPreservableSourceSheetDataMetadata = true;
                }

                if (uint.TryParse(reader.GetAttribute("r"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var rowNumber))
                    ReadRowLayout(reader, rowNumber, hiddenRows, rowOutlineLevels, groupHiddenRows, collapsedAnchorRows, rowHeights, styledRows);

                rowDepth = reader.Depth;
                if (reader.IsEmptyElement)
                    rowDepth = -1;
                continue;
            }

            if (rowDepth >= 0 && reader.Depth == rowDepth + 1)
            {
                if (reader.LocalName == "extLst")
                {
                    if (detectPreservableSourceSheetDataMetadata)
                        hasPreservableSourceSheetDataMetadata = true;
                    continue;
                }

                if (reader.LocalName != "c")
                {
                    if (detectPreservableSourceSheetDataMetadata)
                        hasPreservableSourceSheetDataMetadata = true;
                    continue;
                }

                if (detectPreservableSourceSheetDataMetadata &&
                    HasNativeOnlyLocalAttributes(reader, ModeledCellAttributes))
                {
                    hasPreservableSourceSheetDataMetadata = true;
                }

                BeginCurrentCell(reader);
                cellDepth = reader.Depth;
                if (reader.IsEmptyElement)
                {
                    FinalizeCurrentCell();
                    cellDepth = -1;
                    currentCellIsInlineString = false;
                }

                continue;
            }

            if (cellDepth >= 0 && reader.Depth == cellDepth + 1)
            {
                switch (reader.LocalName)
                {
                    case "extLst":
                        if (detectPreservableSourceSheetDataMetadata)
                            hasPreservableSourceSheetDataMetadata = true;
                        break;
                    case "f":
                        currentHasFormula = true;
                        if (detectPreservableSourceSheetDataMetadata &&
                            HasAnyNonNamespaceAttribute(reader))
                        {
                            hasPreservableSourceSheetDataMetadata = true;
                        }
                        break;
                    case "v":
                        currentHasValue = true;
                        valueDepth = reader.Depth;
                        currentRawValue = currentIsErrorType ? string.Empty : null;
                        if (reader.IsEmptyElement)
                            valueDepth = -1;
                        break;
                    case "is":
                        currentHasInlineString = true;
                        if (detectPreservableSourceSheetDataMetadata &&
                            currentCellIsInlineString &&
                            HasAnyNonNamespaceAttribute(reader))
                        {
                            hasPreservableSourceSheetDataMetadata = true;
                        }
                        inlineStringDepth = reader.Depth;
                        if (reader.IsEmptyElement)
                            inlineStringDepth = -1;
                        break;
                    default:
                        if (detectPreservableSourceSheetDataMetadata)
                            hasPreservableSourceSheetDataMetadata = true;
                        break;
                }

                continue;
            }

            if (inlineStringDepth >= 0 &&
                reader.Depth == inlineStringDepth + 1 &&
                currentCellIsInlineString &&
                reader.LocalName is "r" or "rPh" or "phoneticPr")
            {
                if (detectPreservableSourceSheetDataMetadata)
                    hasPreservableSourceSheetDataMetadata = true;
            }
        }

        var layout = CreateLayout();
        ClassifyCollapsedOutlineHidden(layout.RowColumnLayout, summaryBelow: true, summaryRight: true);
        return layout;

        XlsxWorksheetSheetDataLayout CreateLayout() =>
            new(
                new XlsxWorksheetRowColumnLayout(
                    hiddenRows,
                    hiddenCols,
                    rowOutlineLevels,
                    colOutlineLevels,
                    groupHiddenRows,
                    groupHiddenCols,
                    rowHeights,
                    columnWidths,
                    collapsedAnchorRows,
                    collapsedAnchorCols,
                    styledRows,
                    styledColumns),
                new XlsxWorksheetCellLayout(
                    cachedFormulaErrors,
                    explicitPopulatedCellStyles,
                    explicitStyleOnlyCells,
                    hasStyleOnlyCells,
                    hasDuplicateStyleOnlyCellStyleIndexes,
                    populatedCellCount,
                    sharedStringValueCells),
                hasPreservableSourceSheetDataMetadata);

        void BeginCurrentCell(XmlReader cell)
        {
            currentReference = cell.GetAttribute("r");
            currentRawStyleIndex = cell.GetAttribute("s");
            var type = cell.GetAttribute("t");
            currentHasStyle = int.TryParse(
                currentRawStyleIndex,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out currentStyleIndex);
            currentIsErrorType = string.Equals(type, "e", StringComparison.OrdinalIgnoreCase);
            currentIsSharedStringType = string.Equals(type, "s", StringComparison.OrdinalIgnoreCase);
            currentCellIsInlineString = string.Equals(type, "inlineStr", StringComparison.OrdinalIgnoreCase);
            currentHasFormula = false;
            currentHasValue = false;
            currentHasInlineString = false;
            currentRawValue = null;
        }

        void FinalizeCurrentCell()
        {
            if (currentHasFormula || currentHasValue || currentHasInlineString)
                populatedCellCount++;

            // Track SharedString cells that have a value but no formula: ClosedXML's CellsUsed()
            // silently skips these when the SST entry is an empty string, causing a loading gap.
            if (currentIsSharedStringType && currentHasValue && !currentHasFormula &&
                !string.IsNullOrWhiteSpace(currentReference) &&
                CellAddress.TryParse(currentReference, ParseOnlySheetId, out var ssAddr))
            {
                sharedStringValueCells.Add((ssAddr.Row, ssAddr.Col));
            }

            if (!currentHasStyle && !currentIsErrorType)
                return;

            var isStyleOnly = currentHasStyle && !currentHasFormula && !currentHasValue && !currentHasInlineString;
            var hasCachedFormulaError =
                currentIsErrorType &&
                currentHasFormula &&
                !string.IsNullOrWhiteSpace(currentRawValue);
            var isExplicitlyStyledPopulatedCell = currentHasStyle && !isStyleOnly;
            if (!isStyleOnly && !hasCachedFormulaError && !isExplicitlyStyledPopulatedCell)
                return;

            if (isStyleOnly && !styleOnlyStyleIndexes.Add(currentRawStyleIndex!))
                hasDuplicateStyleOnlyCellStyleIndexes = true;

            if (string.IsNullOrWhiteSpace(currentReference) ||
                !CellAddress.TryParse(currentReference, ParseOnlySheetId, out var address))
            {
                if (isStyleOnly)
                    hasStyleOnlyCells = true;
                return;
            }

            if (isStyleOnly)
            {
                explicitStyleOnlyCells.Add((address.Row, address.Col, currentStyleIndex));
                hasStyleOnlyCells = true;
            }

            if (isExplicitlyStyledPopulatedCell)
                explicitPopulatedCellStyles.Add((address.Row, address.Col, currentStyleIndex));

            if (hasCachedFormulaError)
                cachedFormulaErrors[(address.Row, address.Col)] = MapCachedFormulaError(currentRawValue!);
        }
    }

    // R136-io-worksheet-props-col-row-default-style: a row's whole-row default style (the "s" +
    // "customFormat" pair that stamps a banner style on every cell in the row that has no explicit
    // style of its own) used to be dropped entirely (R75-io-worksheet-props-4-3, RE-DEFERRED). It is
    // now surfaced via styledRows -- just the ROW NUMBER, not the raw "s" stylesheet index -- so the
    // caller (XlsxFileAdapter) can resolve the effective style through ClosedXML's own
    // IXLRow.Style (which already implements Excel's cell>row>column precedence) instead of
    // remapping the source cellXfs index by hand. That sidesteps the stale-index/crash hazard the
    // old comment warned about: we never re-attach a raw source style index to the rebuilt
    // styles.xml, we resolve to a CellStyle value and let Workbook.RegisterStyle assign a fresh id.
    // A row with s="0" (Excel's default style) is not flagged -- registering it would be a wasted
    // round-trip through the resolver for a style that always maps to CellStyle.Default.
    private static void ReadRowLayout(
        XElement row,
        uint rowNumber,
        HashSet<uint> hiddenRows,
        Dictionary<uint, int> rowOutlineLevels,
        HashSet<uint> groupHiddenRows,
        HashSet<uint> collapsedAnchorRows,
        Dictionary<uint, double> rowHeights,
        HashSet<uint>? styledRows = null)
    {
        var isHidden = XlsxWorksheetXmlValueParser.IsTruthy(row.Attribute("hidden")?.Value);
        if (TryReadRowHeight(row.Attribute("ht")?.Value, row.Attribute("customHeight")?.Value, out var height))
            rowHeights[rowNumber] = height;

        var isCollapsed = XlsxWorksheetXmlValueParser.IsTruthy(row.Attribute("collapsed")?.Value);

        var outlineStr = row.Attribute("outlineLevel")?.Value;
        var hasOutline = int.TryParse(outlineStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var outlineLevel) && outlineLevel > 0;
        if (hasOutline)
        {
            rowOutlineLevels[rowNumber] = outlineLevel;
        }

        if (isHidden)
            hiddenRows.Add(rowNumber);

        // An inner group's anchor can itself be hidden as detail of an outer group, so collapsed
        // must be retained independently of hidden and outlineLevel.
        if (isCollapsed)
            collapsedAnchorRows.Add(rowNumber);

        if (styledRows is not null &&
            XlsxWorksheetXmlValueParser.IsTruthy(row.Attribute("customFormat")?.Value) &&
            int.TryParse(row.Attribute("s")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rowStyleIndex) &&
            rowStyleIndex != 0)
        {
            styledRows.Add(rowNumber);
        }
    }

    private static void ReadRowLayout(
        XmlReader row,
        uint rowNumber,
        HashSet<uint> hiddenRows,
        Dictionary<uint, int> rowOutlineLevels,
        HashSet<uint> groupHiddenRows,
        HashSet<uint> collapsedAnchorRows,
        Dictionary<uint, double> rowHeights,
        HashSet<uint>? styledRows = null)
    {
        var isHidden = XlsxWorksheetXmlValueParser.IsTruthy(row.GetAttribute("hidden"));
        if (TryReadRowHeight(row.GetAttribute("ht"), row.GetAttribute("customHeight"), out var height))
            rowHeights[rowNumber] = height;

        var isCollapsed = XlsxWorksheetXmlValueParser.IsTruthy(row.GetAttribute("collapsed"));

        var outlineStr = row.GetAttribute("outlineLevel");
        var hasOutline = int.TryParse(outlineStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var outlineLevel) && outlineLevel > 0;
        if (hasOutline)
        {
            rowOutlineLevels[rowNumber] = outlineLevel;
        }

        if (styledRows is not null &&
            XlsxWorksheetXmlValueParser.IsTruthy(row.GetAttribute("customFormat")) &&
            int.TryParse(row.GetAttribute("s"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var rowStyleIndex) &&
            rowStyleIndex != 0)
        {
            styledRows.Add(rowNumber);
        }

        if (isHidden)
            hiddenRows.Add(rowNumber);

        if (isCollapsed)
            collapsedAnchorRows.Add(rowNumber);
    }

    // R136-io-worksheet-props-col-row-default-style: a column's whole-column default style (a
    // <col style="..."> that fills every cell in the column with a style when the cell itself has
    // none) used to be dropped entirely (R75-io-worksheet-props-4-2, RE-DEFERRED). It is now
    // surfaced via styledColumns -- just the COLUMN NUMBER, not the raw "style" stylesheet index --
    // so the caller (XlsxFileAdapter) can resolve the effective style through ClosedXML's own
    // IXLColumn.Style (which already implements Excel's cell>row>column precedence) instead of
    // remapping the source cellXfs index by hand, sidestepping the stale-index/crash hazard the old
    // comment warned about (see the row-side twin above for the full rationale). A column with
    // style="0" (Excel's default style) is not flagged, matching the row-side skip.
    private static void ReadColumnLayout(
        XElement col,
        HashSet<uint> hiddenCols,
        Dictionary<uint, int> colOutlineLevels,
        HashSet<uint> groupHiddenCols,
        HashSet<uint> collapsedAnchorCols,
        Dictionary<uint, double> columnWidths,
        HashSet<uint>? styledColumns = null)
    {
        if (!uint.TryParse(col.Attribute("min")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var min))
            return;
        if (!uint.TryParse(col.Attribute("max")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var max))
            return;
        if (min > max)
            return;

        // Cap a malformed/crafted <col min max> span to the model's max column so a tiny file
        // can't drive a multi-billion-iteration loop (OOM / hang). Excel itself clamps <col> max
        // to the sheet's column count. Mirrors OdsFileAdapter.Read.cs's repeat-count clamp.
        if (min > CellAddress.MaxCol)
            return;
        if (max > CellAddress.MaxCol)
            max = CellAddress.MaxCol;

        var isHidden = XlsxWorksheetXmlValueParser.IsTruthy(col.Attribute("hidden")?.Value);
        var isColCollapsed = XlsxWorksheetXmlValueParser.IsTruthy(col.Attribute("collapsed")?.Value);

        var colOutlineStr = col.Attribute("outlineLevel")?.Value;
        var hasOutline = int.TryParse(colOutlineStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var colOutlineLevel) && colOutlineLevel > 0;
        if (hasOutline)
        {
            for (var colNumber = min; colNumber <= max; colNumber++)
                colOutlineLevels[colNumber] = colOutlineLevel;
        }

        if (isHidden)
        {
            for (var colNumber = min; colNumber <= max; colNumber++)
                hiddenCols.Add(colNumber);
        }

        if (isColCollapsed)
        {
            for (var colNumber = min; colNumber <= max; colNumber++)
                collapsedAnchorCols.Add(colNumber);
        }

        // Capture BEFORE the width-carrier early return below: a narrow "styling-only carrier"
        // column (Excel/ClosedXML stamp a style + an auto width on a column that merely formats
        // empty cells) is exactly the case this default-style fallback exists for, so it must not
        // be skipped just because its width isn't treated as a real custom width.
        if (styledColumns is not null &&
            int.TryParse(col.Attribute("style")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var colStyleIndex) &&
            colStyleIndex != 0)
        {
            for (var colNumber = min; colNumber <= max; colNumber++)
                styledColumns.Add(colNumber);
        }

        if (XlsxWorksheetXmlValueParser.IsTruthy(col.Attribute("customWidth")?.Value) &&
            XlsxWorksheetXmlValueParser.ParsePositiveFiniteDouble(col.Attribute("width")?.Value) is { } width &&
            width > 0)
        {
            // A styled column at a near-default width is a styling-only carrier (Excel/ClosedXML stamp a
            // style + an auto width — typically 8.43..~9.14 depending on the font — on a column that
            // merely formats empty cells), not a real custom width, so skip it. A genuinely-modelled
            // width in this band is written by the column-width writer with its ClosedXML-stamped style
            // *removed* (see XlsxWorksheetColumnWidthWriter), so it arrives here with no style attribute
            // and round-trips intact — including a narrow 1.71 / 5.71 gutter or a 8.14 / 8.71 column that
            // happens to sit in the carrier band.
            if (col.Attribute("style") is not null && width <= 9.2)
                return;

            // Keep the exact width — Excel column widths are fractional, so flooring loses fidelity.
            for (var colNumber = min; colNumber <= max; colNumber++)
                columnWidths[colNumber] = width;
        }
    }

    internal static void ClassifyCollapsedOutlineHidden(
        XlsxWorksheetRowColumnLayout layout,
        bool summaryBelow,
        bool summaryRight)
    {
        layout.HiddenRows.UnionWith(layout.GroupHiddenRows);
        layout.GroupHiddenRows.Clear();
        foreach (var anchor in layout.CollapsedAnchorRows ?? [])
        {
            ClassifyDetailRun(
                anchor,
                summaryBelow,
                CellAddress.MaxRow,
                layout.HiddenRows,
                layout.RowOutlineLevels,
                layout.GroupHiddenRows);
        }
        layout.HiddenRows.ExceptWith(layout.GroupHiddenRows);

        layout.HiddenCols.UnionWith(layout.GroupHiddenCols);
        layout.GroupHiddenCols.Clear();
        foreach (var anchor in layout.CollapsedAnchorCols ?? [])
        {
            ClassifyDetailRun(
                anchor,
                summaryRight,
                CellAddress.MaxCol,
                layout.HiddenCols,
                layout.ColOutlineLevels,
                layout.GroupHiddenCols);
        }
        layout.HiddenCols.ExceptWith(layout.GroupHiddenCols);
    }

    private static void ClassifyDetailRun(
        uint anchor,
        bool summaryAfter,
        uint maxIndex,
        IReadOnlySet<uint> hidden,
        IReadOnlyDictionary<uint, int> levels,
        HashSet<uint> groupHidden)
    {
        levels.TryGetValue(anchor, out var anchorLevel);
        if (summaryAfter)
        {
            if (anchor <= 1)
                return;

            for (var index = anchor - 1; ; index--)
            {
                if (!TryClassify(index))
                    break;
                if (index == 1)
                    break;
            }
        }
        else
        {
            if (anchor >= maxIndex)
                return;

            for (var index = anchor + 1; index <= maxIndex; index++)
            {
                if (!TryClassify(index))
                    break;
                if (index == maxIndex)
                    break;
            }
        }

        bool TryClassify(uint index)
        {
            if (!hidden.Contains(index) ||
                !levels.TryGetValue(index, out var level) ||
                level <= anchorLevel)
            {
                return false;
            }

            groupHidden.Add(index);
            return true;
        }
    }

    private static bool ReadSummaryDirection(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "0" or "false" => false,
            _ => true
        };

    private static bool TryReadRowHeight(string? rawHeight, string? rawCustomHeight, out double height)
    {
        height = 0;
        if (XlsxWorksheetXmlValueParser.ParsePositiveFiniteDouble(rawHeight) is not { } xlsxHeight || xlsxHeight <= 0)
            return false;

        if (XlsxWorksheetXmlValueParser.IsTruthy(rawCustomHeight))
        {
            height = RowHeightPointsToPixels(xlsxHeight);
            return true;
        }

        // Excel may persist cached autofit heights without customHeight. Small cached
        // rows such as 15.75 display as the next lower point row in Excel, while tall
        // wrapped rows need a different normalization to match the visible autofit.
        if (xlsxHeight <= 20.0)
        {
            var normalizedPoints = Math.Floor(xlsxHeight);
            if (normalizedPoints <= 0 || Math.Abs(normalizedPoints - xlsxHeight) < 0.001)
                return false;

            height = RowHeightPointsToPixels(normalizedPoints);
            return true;
        }

        height = Math.Round(xlsxHeight * CachedAutoFitRowHeightScale, MidpointRounding.AwayFromZero);
        return true;
    }

    private static double RowHeightPointsToPixels(double points) =>
        Math.Round(points * PointsToDips, MidpointRounding.AwayFromZero);

    private static bool HasAnyNonNamespaceAttribute(XmlReader reader)
    {
        if (!reader.HasAttributes)
            return false;

        for (var i = 0; i < reader.AttributeCount; i++)
        {
            reader.MoveToAttribute(i);
            if (reader.NamespaceURI != XNamespace.Xmlns.NamespaceName)
            {
                reader.MoveToElement();
                return true;
            }
        }

        reader.MoveToElement();
        return false;
    }

    private static bool HasNativeOnlyLocalAttributes(XmlReader reader, HashSet<string> modeledAttributeNames)
    {
        if (!reader.HasAttributes)
            return false;

        for (var i = 0; i < reader.AttributeCount; i++)
        {
            reader.MoveToAttribute(i);
            var isNativeOnly =
                reader.NamespaceURI != XNamespace.Xmlns.NamespaceName &&
                !modeledAttributeNames.Contains(reader.LocalName);
            if (isNativeOnly)
            {
                reader.MoveToElement();
                return true;
            }
        }

        reader.MoveToElement();
        return false;
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
}

internal sealed record XlsxWorksheetSheetDataLayout(
    XlsxWorksheetRowColumnLayout RowColumnLayout,
    XlsxWorksheetCellLayout CellLayout,
    bool HasPreservableSourceSheetDataMetadata = false);

internal sealed record XlsxWorksheetRowColumnLayout(
    HashSet<uint> HiddenRows,
    HashSet<uint> HiddenCols,
    Dictionary<uint, int> RowOutlineLevels,
    Dictionary<uint, int> ColOutlineLevels,
    HashSet<uint> GroupHiddenRows,
    HashSet<uint> GroupHiddenCols,
    Dictionary<uint, double> RowHeights,
    Dictionary<uint, double> ColumnWidths,
    // Trailing optional so existing positional call sites outside this file (which construct this
    // record with the original 8 arguments) keep compiling unchanged. Rows/columns that are the
    // visible, still-uncollapsed anchor of a collapsed outline group (collapsed="1", hidden absent/
    // false) -- see Sheet.CollapsedAnchorRows/CollapsedAnchorCols and R35-deferred-collapse-anchor-1.
    HashSet<uint>? CollapsedAnchorRows = null,
    HashSet<uint>? CollapsedAnchorCols = null,
    // Rows/columns that carry a whole-row/-column default style (row "s"+"customFormat", or a
    // <col style> range) with no per-cell override -- see R136-io-worksheet-props-col-row-default-style.
    // Trailing optional for the same reason as CollapsedAnchorRows/Cols above.
    HashSet<uint>? StyledRows = null,
    HashSet<uint>? StyledColumns = null);
