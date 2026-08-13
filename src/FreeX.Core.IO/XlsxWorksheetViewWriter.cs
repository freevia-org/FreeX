using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetViewWriter
{
    public static bool HasPersistableViewState(Sheet sheet) =>
        !sheet.ShowGridlines ||
        !sheet.ShowHeadings ||
        !sheet.ShowRulers ||
        XlsxWorksheetValueSanitizer.ValidEnumOrDefault(sheet.ViewMode, WorksheetViewMode.Normal) != WorksheetViewMode.Normal ||
        sheet.ZoomPercent != 100 ||
        sheet.ShowFormulas ||
        !sheet.ShowZeros ||
        sheet.IsRightToLeft ||
        sheet.ViewTopRow.HasValue ||
        sheet.ViewLeftCol.HasValue ||
        sheet.ActiveRow.HasValue ||
        sheet.ActiveCol.HasValue ||
        (sheet.FrozenRows == 0 && sheet.FrozenCols == 0 &&
         (sheet.SplitRow.HasValue || sheet.SplitColumn.HasValue));

    public static void Save(Stream xlsxStream, Workbook workbook)
    {
        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Update, leaveOpen: true);
        Save(archive, workbook, XlsxWorkbookWorksheetPathMap.TryCreate(archive));
    }

    public static void Save(Stream xlsxStream, Workbook workbook, XlsxWorkbookWorksheetPathMap? worksheetPathMap)
    {
        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Update, leaveOpen: true);
        Save(archive, workbook, worksheetPathMap);
    }

    private static void Save(ZipArchive archive, Workbook workbook, XlsxWorkbookWorksheetPathMap? worksheetPathMap)
    {
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        var relsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels");
        if (workbookEntry is null || relsEntry is null || worksheetPathMap is null)
            return;

        var viewSheets = workbook.Sheets
            .Where(HasPersistableViewState)
            .ToDictionary(sheet => sheet.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var (name, worksheetPath) in worksheetPathMap.SheetPathsByName)
        {
            if (!viewSheets.TryGetValue(name, out var sheet))
                continue;

            UpdateSheetView(archive, worksheetPath, sheet);
        }
    }

    public static string? ToXlsxWorksheetViewMode(WorksheetViewMode viewMode) =>
        viewMode switch
        {
            WorksheetViewMode.PageBreakPreview => "pageBreakPreview",
            WorksheetViewMode.PageLayout => "pageLayout",
            _ => null
        };

    private static void UpdateSheetView(ZipArchive archive, string worksheetPath, Sheet sheet)
    {
        var worksheetEntry = archive.GetEntry(worksheetPath);
        if (worksheetEntry is null)
            return;

        var worksheetXml = LoadXml(worksheetEntry);
        if (UpdateSheetView(worksheetXml, sheet))
            XlsxPackageXmlEditor.ReplaceXml(archive, worksheetPath, worksheetXml);
    }

    internal static bool UpdateSheetView(XDocument worksheetXml, Sheet sheet)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var root = worksheetXml.Root;
        if (root is null)
            return false;

        var changed = false;
        var sheetViews = root.Element(worksheetNs + "sheetViews");
        if (sheetViews is null)
        {
            sheetViews = new XElement(worksheetNs + "sheetViews");
            root.AddFirst(sheetViews);
            changed = true;
        }

        XElement? sheetView = null;
        foreach (var candidateView in sheetViews.Elements(worksheetNs + "sheetView"))
        {
            if (IsPrimarySheetView(candidateView))
            {
                sheetView = candidateView;
                break;
            }
        }

        if (sheetView is null)
        {
            sheetView = new XElement(worksheetNs + "sheetView", new XAttribute("workbookViewId", "0"));
            sheetViews.AddFirst(sheetView);
            changed = true;
        }

        changed |= XlsxXmlNormalizationHelpers.SetOrRemoveAttributeIfChanged(sheetView, "view", ToXlsxWorksheetViewMode(
            XlsxWorksheetValueSanitizer.ValidEnumOrDefault(sheet.ViewMode, WorksheetViewMode.Normal)));
        changed |= XlsxXmlNormalizationHelpers.SetOrRemoveAttributeIfChanged(sheetView, "showGridLines", sheet.ShowGridlines ? null : "0");
        changed |= XlsxXmlNormalizationHelpers.SetOrRemoveAttributeIfChanged(sheetView, "showRowColHeaders", sheet.ShowHeadings ? null : "0");
        changed |= XlsxXmlNormalizationHelpers.SetOrRemoveAttributeIfChanged(sheetView, "showRuler", sheet.ShowRulers ? null : "0");
        changed |= XlsxXmlNormalizationHelpers.SetOrRemoveAttributeIfChanged(sheetView, "zoomScale", sheet.ZoomPercent == 100 ? null : sheet.ZoomPercent.ToString(CultureInfo.InvariantCulture));
        changed |= XlsxXmlNormalizationHelpers.SetOrRemoveAttributeIfChanged(sheetView, "showFormulas", sheet.ShowFormulas ? "1" : null);
        changed |= XlsxXmlNormalizationHelpers.SetOrRemoveAttributeIfChanged(sheetView, "showZeros", sheet.ShowZeros ? null : "0");
        changed |= XlsxXmlNormalizationHelpers.SetOrRemoveAttributeIfChanged(sheetView, "rightToLeft", sheet.IsRightToLeft ? "1" : null);
        changed |= XlsxXmlNormalizationHelpers.SetOrRemoveAttributeIfChanged(sheetView, "topLeftCell", ToOptionalA1(sheet.ViewTopRow, sheet.ViewLeftCol));

        // Captures the active pane computed below when this save is the FIRST one to introduce a
        // brand-new split (no pre-existing <pane>, no freeze): the split-pane-creation block
        // further down creates the <pane> element itself and needs this value to tag it with the
        // correct activePane, since there is no existing pane to read one from.
        string? activePaneNameForNewSplitPane = null;

        if (ToOptionalA1(sheet.ActiveRow, sheet.ActiveCol) is { } activeCell)
        {
            // A frozen/split sheetView can carry one <selection> per pane (topLeft/topRight/
            // bottomLeft/bottomRight), each keyed by its own @pane attribute (missing @pane means
            // "topLeft"). Only the pane holding the true cursor -- named by pane/@activePane
            // (defaulting to "topLeft" per OOXML when no pane element is present) -- should be
            // updated from the model; the other panes' <selection> elements must be carried
            // through untouched, or a full-rebuild save silently destroys their cursor positions.
            var paneElement = sheetView.Element(worksheetNs + "pane");
            string activePaneName;
            if (paneElement is not null)
            {
                // Recompute which quadrant the cursor is actually in from the model's active cell
                // and the freeze/split boundaries, rather than blindly trusting whatever
                // activePane the pane element already carries -- otherwise moving the cursor
                // across the freeze/split boundary (e.g. clicking into the frozen rows) writes a
                // self-contradictory pane/selection pair: a stale activePane (say "bottomLeft")
                // paired with a selection whose activeCell can only ever be rendered in a
                // different pane ("topLeft").
                activePaneName = ComputeActivePaneName(sheet, sheet.ActiveRow!.Value, sheet.ActiveCol!.Value);
                changed |= XlsxXmlNormalizationHelpers.SetOrRemoveAttributeIfChanged(
                    paneElement,
                    "activePane",
                    string.Equals(activePaneName, "topLeft", StringComparison.Ordinal) ? null : activePaneName);
            }
            else if (sheet.FrozenRows == 0 && sheet.FrozenCols == 0 &&
                     (sheet.SplitRow.HasValue || sheet.SplitColumn.HasValue))
            {
                // No pre-existing <pane>, but this save is introducing a brand-new split (the
                // split-pane-creation block below runs AFTER this one and hasn't created the
                // <pane> element yet). Compute the active pane from the split geometry being
                // introduced rather than hardcoding "topLeft", or an active cell that is
                // geometrically in a different quadrant (e.g. bottom-right) gets mistagged on the
                // very first save that creates the split.
                activePaneName = ComputeActivePaneName(sheet, sheet.ActiveRow!.Value, sheet.ActiveCol!.Value);
                activePaneNameForNewSplitPane = activePaneName;
            }
            else
            {
                activePaneName = "topLeft";
            }

            var selections = sheetView.Elements(worksheetNs + "selection").ToList();
            XElement? activeSelection = null;
            foreach (var selection in selections)
            {
                var selectionPaneName = selection.Attribute("pane")?.Value;
                if (string.IsNullOrWhiteSpace(selectionPaneName))
                    selectionPaneName = "topLeft";

                if (string.Equals(selectionPaneName, activePaneName, StringComparison.Ordinal))
                {
                    activeSelection = selection;
                    break;
                }
            }

            if (activeSelection is null)
            {
                activeSelection = string.Equals(activePaneName, "topLeft", StringComparison.Ordinal)
                    ? new XElement(worksheetNs + "selection")
                    : new XElement(worksheetNs + "selection", new XAttribute("pane", activePaneName));
                sheetView.Add(activeSelection);
                changed = true;
            }

            if (!string.Equals(activeSelection.Attribute("activeCell")?.Value, activeCell, StringComparison.Ordinal))
            {
                // Stale selection (names a different cell than the model's current active cell):
                // the model wins -- collapse sqref to the single active cell too. The collapsed
                // sqref is now a single area, so any activeCellId (an index into a multi-area sqref
                // list per ECMA-376 CT_Selection) referencing a since-discarded area is no longer
                // valid and must be cleared, or it points past the new single-area sqref and forces
                // Excel to repair the file on open.
                changed |= XlsxXmlNormalizationHelpers.SetOrRemoveAttributeIfChanged(activeSelection, "activeCell", activeCell);
                changed |= XlsxXmlNormalizationHelpers.SetOrRemoveAttributeIfChanged(activeSelection, "sqref", activeCell);
                changed |= XlsxXmlNormalizationHelpers.SetOrRemoveAttributeIfChanged(activeSelection, "activeCellId", null);
            }
            // else: the native selection already names the model's active cell -- preserve its
            // sqref (and activeCellId) verbatim, including a multi-cell/multi-area range the model
            // does not itself track, so an unrelated view-only save (e.g. zoom) never narrows a
            // genuine preserved selection down to a single cell.
        }

        if (sheet.FrozenRows == 0 && sheet.FrozenCols == 0 &&
            (sheet.SplitRow.HasValue || sheet.SplitColumn.HasValue))
        {
            // OOXML defines xSplit/ySplit under state="split" as twentieths-of-a-point pane-bar
            // pixel positions, NOT row/column counts (unlike state="frozen"/"frozenSplit", where
            // they genuinely are literal counts). Sheet.SplitRow/SplitColumn model the split as a
            // row/column index (the first row/column below/right of the divider), so it must be
            // converted to the cumulative pixel position of that boundary -- summing the actual
            // (or default) row heights/column widths above it -- before being written as xSplit/
            // ySplit, or Excel renders the divider at the raw index value's twips position
            // (effectively no split for any typical split index).
            var existingPanes = sheetView.Elements(worksheetNs + "pane").ToList();
            // Preserve the existing pane's activePane attribute (which cell quadrant/pane the
            // user's cursor is in) -- it is independent of the split geometry itself, and a
            // rebuilt <pane> element that drops it silently resets the active pane to the OOXML
            // default "topLeft" on any save that merely touches an unrelated view attribute. When
            // there is no existing pane at all, this is the first save introducing the split, so
            // fall back to the active pane computed above from the split geometry being
            // introduced (rather than defaulting to "topLeft" regardless of where the active cell
            // actually is).
            var existingActivePane = existingPanes.Count > 0
                ? existingPanes[0].Attribute("activePane")?.Value
                : activePaneNameForNewSplitPane;
            if (string.Equals(existingActivePane, "topLeft", StringComparison.Ordinal))
                existingActivePane = null;
            var pane = new XElement(
                worksheetNs + "pane",
                sheet.SplitColumn is { } splitColumn ? new XAttribute("xSplit", SplitColumnToTwips(sheet, splitColumn)) : null,
                sheet.SplitRow is { } splitRow ? new XAttribute("ySplit", SplitRowToTwips(sheet, splitRow)) : null,
                new XAttribute("state", "split"),
                !string.IsNullOrWhiteSpace(existingActivePane) ? new XAttribute("activePane", existingActivePane) : null);
            if (existingPanes.Count != 1 ||
                !XNode.DeepEquals(existingPanes[0], pane))
            {
                existingPanes.Remove();
                sheetView.AddFirst(pane);
                changed = true;
            }
        }

        return changed;
    }

    // Determines which pane quadrant (topLeft/topRight/bottomLeft/bottomRight) a given active
    // cell falls into, given the sheet's freeze or split boundary. Matches Excel's convention:
    // frozen boundaries (FrozenRows/FrozenCols) are literal row/column counts that are themselves
    // still part of the top/left pane, while a genuine split's SplitRow/SplitColumn model the
    // first row/column BELOW/RIGHT of the divider (see SplitRowToTwips/SplitColumnToTwips above),
    // so its boundary (the last row/column still in the top/left pane) is one less.
    private static string ComputeActivePaneName(Sheet sheet, uint row, uint col) =>
        ComputeActivePaneName(sheet.FrozenRows, sheet.FrozenCols, sheet.SplitRow, sheet.SplitColumn, row, col);

    // Shared with XlsxCustomViewMapper.ToCustomSheetViewXml, which must tag a Custom View's
    // <pane>/<selection> pair with the same activePane a live sheetView save would compute for
    // the identical freeze/split geometry and active cell (see R98_CustomViewActivePaneTests) --
    // duplicating this logic there would silently drift the two writers apart.
    internal static string ComputeActivePaneName(uint frozenRows, uint frozenCols, uint? splitRow, uint? splitColumn, uint row, uint col)
    {
        uint boundaryRow;
        uint boundaryCol;
        if (frozenRows > 0 || frozenCols > 0)
        {
            boundaryRow = frozenRows;
            boundaryCol = frozenCols;
        }
        else
        {
            boundaryRow = splitRow is > 0 ? splitRow.Value - 1 : 0;
            boundaryCol = splitColumn is > 0 ? splitColumn.Value - 1 : 0;
        }

        var isBottom = boundaryRow > 0 && row > boundaryRow;
        var isRight = boundaryCol > 0 && col > boundaryCol;

        return (isBottom, isRight) switch
        {
            (false, false) => "topLeft",
            (false, true) => "topRight",
            (true, false) => "bottomLeft",
            (true, true) => "bottomRight",
        };
    }

    private static string? ToOptionalA1(uint? row, uint? col)
    {
        return row is > 0 and <= CellAddress.MaxRow &&
               col is > 0 and <= CellAddress.MaxCol
            ? $"{CellAddress.NumberToColumnName(col.Value)}{row.Value}"
            : null;
    }

    // 20 twentieths-of-a-point per point * 72/96 points per pixel (96 DPI, matching the
    // pixels<->points conversion XlsxFileAdapter already uses for row heights) = 15.
    private const double TwipsPerPixel = 15.0;

    private static string SplitRowToTwips(Sheet sheet, uint splitRow)
    {
        double heightPixels = 0;
        for (var row = 1u; row < splitRow; row++)
        {
            if (sheet.IsRowEffectivelyHidden(row))
                continue;

            heightPixels += sheet.RowHeights.TryGetValue(row, out var height) ? height : sheet.DefaultRowHeight;
        }

        return FormatTwips(heightPixels);
    }

    private static string SplitColumnToTwips(Sheet sheet, uint splitColumn)
    {
        double widthPixels = 0;
        for (var col = 1u; col < splitColumn; col++)
        {
            if (sheet.IsColEffectivelyHidden(col))
                continue;

            var characterWidth = sheet.ColumnWidths.TryGetValue(col, out var width) ? width : sheet.DefaultColumnWidth;
            widthPixels += ExcelColumnWidthConverter.ColumnWidthToPixels(characterWidth);
        }

        return FormatTwips(widthPixels);
    }

    private static string FormatTwips(double pixels) =>
        Math.Max(0, Math.Round(pixels * TwipsPerPixel, MidpointRounding.AwayFromZero))
            .ToString(CultureInfo.InvariantCulture);

    private static XDocument LoadXml(ZipArchiveEntry entry)
    {
        return XlsxPackageXmlEditor.LoadXml(entry);
    }

    private static bool IsPrimarySheetView(XElement element) =>
        string.Equals(element.Attribute("workbookViewId")?.Value ?? "0", "0", StringComparison.Ordinal);
}
