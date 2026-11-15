using System;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxCustomViewMapper
{
    public static IReadOnlyList<XlsxWorksheetCustomViewState> ReadWorksheetViews(
        XDocument worksheetXml,
        XNamespace worksheetNs)
    {
        var customViews = new List<XlsxWorksheetCustomViewState>();
        foreach (var customSheetView in worksheetXml.Root?
                     .Element(worksheetNs + "customSheetViews")?
                     .Elements(worksheetNs + "customSheetView") ?? [])
        {
            var id = customSheetView.Attribute("guid")?.Value;
            if (string.IsNullOrWhiteSpace(id))
                continue;

            var pane = customSheetView.Element(worksheetNs + "pane");
            var paneState = pane?.Attribute("state")?.Value;
            // CT_Pane's xSplit/ySplit are the row/column index under state="frozen"/"frozenSplit";
            // WorksheetCustomViewState.SplitRow/SplitColumn (the non-frozen "split" case) mirror
            // Sheet.SplitRow/SplitColumn, which are themselves row/column indexes (see Sheet.cs) --
            // this mapper reads/writes both cases as the literal index, matching FileAdapterSmoke's
            // and NativeJsonAdapter's established modeling of split panes.
            var rawYSplit = XlsxWorksheetXmlValueParser.ParsePaneSplit(pane?.Attribute("ySplit")?.Value);
            var rawXSplit = XlsxWorksheetXmlValueParser.ParsePaneSplit(pane?.Attribute("xSplit")?.Value);
            var frozenRows = paneState is "frozen" or "frozenSplit" ? rawYSplit ?? 0 : 0;
            var frozenCols = paneState is "frozen" or "frozenSplit" ? rawXSplit ?? 0 : 0;
            var splitRow = frozenRows == 0 && frozenCols == 0 ? rawYSplit : null;
            var splitColumn = frozenRows == 0 && frozenCols == 0 ? rawXSplit : null;
            var topLeftCell = customSheetView.Attribute("topLeftCell")?.Value;
            var activeCell = ReadActiveSelectionCellReference(customSheetView, pane, worksheetNs);

            var pageMargins = customSheetView.Element(worksheetNs + "pageMargins");
            var pageSetup = customSheetView.Element(worksheetNs + "pageSetup");
            var printOptions = customSheetView.Element(worksheetNs + "printOptions");
            var autoFilter = XlsxWorksheetAutoFilterXmlMapper.Read(customSheetView.Element(worksheetNs + "autoFilter"));
            var paperSizeCode = ParsePaperSizeCode(pageSetup);
            var fitToPage = XlsxXmlAttributeReader.ReadNullableBoolAttribute(customSheetView, "fitToPage");

            customViews.Add(new XlsxWorksheetCustomViewState(
                id,
                new WorksheetCustomViewState(
                    string.Empty,
                    XlsxWorksheetXmlValueParser.ParseWorksheetViewMode(customSheetView.Attribute("view")?.Value),
                    frozenRows,
                    frozenCols,
                    splitRow,
                    splitColumn,
                    ShowGridlines: !XlsxWorksheetXmlValueParser.IsFalse(customSheetView.Attribute("showGridLines")?.Value),
                    ShowHeadings: !XlsxWorksheetXmlValueParser.IsFalse(customSheetView.Attribute("showRowCol")?.Value),
                    ShowRulers: !XlsxWorksheetXmlValueParser.IsFalse(customSheetView.Attribute("showRuler")?.Value),
                    ZoomPercent: XlsxWorksheetValueSanitizer.ValidZoomPercentOrDefault(XlsxXmlAttributeReader.ReadIntAttribute(customSheetView, "scale") ?? 100),
                    ShowFormulas: XlsxWorksheetXmlValueParser.IsTruthy(customSheetView.Attribute("showFormulas")?.Value),
                    ActiveRow: ParseCellRow(activeCell),
                    ActiveCol: ParseCellColumn(activeCell),
                    ViewTopRow: ParseCellRow(topLeftCell),
                    ViewLeftCol: ParseCellColumn(topLeftCell),
                    // N13: hidden-rows/cols state has no dedicated slot in CT_CustomSheetView
                    // (ECMA-376 §18.3.1.90) — Excel itself does not snapshot per-view hidden-row/
                    // column lists there (only the live sheetData row/col hidden attributes) — so
                    // that is left uncaptured here rather than guessing at a non-standard
                    // extension. FitToPage IS a real customSheetView attribute (CT_CustomSheetView.
                    // FitToPage, distinct from the worksheet-level sheetPr/pageSetUpPr/@fitToPage
                    // flag) and AutoFilter/print settings also have schema support (nested
                    // autoFilter/pageMargins/pageSetup/printOptions elements), so all of those
                    // round-trip below.
                    FitToPage: fitToPage,
                    AutoFilter: autoFilter,
                    PageOrientation: ParseOrientation(pageSetup?.Attribute("orientation")?.Value),
                    PaperSize: paperSizeCode is { } code && PaperSizeCodes.TryGetEnum(code, out var paperSize) ? paperSize : null,
                    PaperSizeCode: paperSizeCode,
                    PageMargins: ParsePageMargins(pageMargins),
                    HeaderMargin: ParseNullableDouble(pageMargins, "header"),
                    FooterMargin: ParseNullableDouble(pageMargins, "footer"),
                    PrintGridlines: printOptions is null ? null : XlsxXmlAttributeReader.ReadBoolAttribute(printOptions, "gridLines"),
                    PrintHeadings: printOptions is null ? null : XlsxXmlAttributeReader.ReadBoolAttribute(printOptions, "headings"),
                    ScaleToFit: ParseScaleToFit(pageSetup, fitToPage))
                {
                    PrintSettings = ReadPrintSettings(customSheetView, worksheetNs, fitToPage)
                }));
        }

        return customViews;
    }

    // Excel stores custom-view print areas and titles as hidden sheet-scoped workbook names.
    // Only recognize the exact GUID-shaped wvu form, leaving ordinary user names untouched.
    internal static bool IsCustomViewPrintName(string? name, out string guid, out bool titles)
    {
        guid = string.Empty;
        titles = false;
        if (name is null || !name.StartsWith("Z_", StringComparison.OrdinalIgnoreCase) || name.Length < 39)
            return false;
        var suffix = name[38..];
        titles = string.Equals(suffix, "_.wvu.PrintTitles", StringComparison.OrdinalIgnoreCase);
        if (!titles && !string.Equals(suffix, "_.wvu.PrintArea", StringComparison.OrdinalIgnoreCase)) return false;
        if (!Guid.TryParseExact(name.Substring(2, 36).Replace('_', '-'), "D", out var parsed)) return false;
        guid = $"{{{parsed:D}}}".ToUpperInvariant();
        return true;
    }

    internal static void ReadCustomViewPrintNames(Stream packageStream, Workbook workbook)
    {
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry("xl/workbook.xml");
        if (entry is null) return;
        var xml = XlsxPackageXmlEditor.LoadXml(entry);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        foreach (var name in xml.Root?.Element(ns + "definedNames")?.Elements(ns + "definedName") ?? [])
        {
            if (!IsCustomViewPrintName(name.Attribute("name")?.Value, out var guid, out var titles) ||
                XlsxXmlAttributeReader.ReadIntAttribute(name, "localSheetId") is not { } sheetIndex ||
                sheetIndex < 0 || sheetIndex >= workbook.Sheets.Count) continue;
            var sheet = workbook.Sheets[sheetIndex];
            var viewIndex = workbook.CustomViews.FindIndex(view =>
                string.Equals(NormalizeId(view.Id), guid, StringComparison.OrdinalIgnoreCase));
            if (viewIndex < 0) continue;
            var view = workbook.CustomViews[viewIndex];
            var states = view.Sheets.ToList();
            var stateIndex = states.FindIndex(state =>
                string.Equals(state.SheetName, sheet.Name, StringComparison.OrdinalIgnoreCase));
            if (stateIndex < 0) continue;
            var state = states[stateIndex];
            var refs = SplitDefinedNameReferences(name.Value);
            if (titles)
            {
                var print = state.PrintSettings ?? new WorksheetCustomViewPrintSettings();
                WorksheetRepeatRange? rows = null, columns = null;
                var opaque = false;
                foreach (var reference in refs)
                {
                    if (!TryReferenceBody(reference, sheet.Name, out var body)) { opaque = true; break; }
                    var parts = body.Replace("$", "", StringComparison.Ordinal).Split(':');
                    if (parts.Length != 2) { opaque = true; break; }
                    if (uint.TryParse(parts[0], out var r1) && uint.TryParse(parts[1], out var r2) &&
                        r1 >= 1 && r1 <= CellAddress.MaxRow && r2 >= r1 && r2 <= CellAddress.MaxRow)
                    {
                        if (rows is not null) { opaque = true; break; }
                        rows = new WorksheetRepeatRange(r1, r2);
                    }
                    else
                    {
                        var c1 = CellAddress.ColumnNameToNumber(parts[0]);
                        var c2 = CellAddress.ColumnNameToNumber(parts[1]);
                        if (c1 >= 1 && c1 <= CellAddress.MaxCol && c2 >= c1 && c2 <= CellAddress.MaxCol)
                        {
                            if (columns is not null) { opaque = true; break; }
                            columns = new WorksheetRepeatRange(c1, c2);
                        }
                        else { opaque = true; break; }
                    }
                }
                states[stateIndex] = state with
                {
                    PrintSettings = print with
                    {
                        PrintTitleRows = opaque ? null : rows,
                        PrintTitleColumns = opaque ? null : columns
                    },
                    OpaquePrintTitlesExpression = opaque ? name.Value : null
                };
            }
            else
            {
                var areas = new List<GridRange>();
                var forms = new List<WorksheetCustomViewPrintAreaForm>();
                var opaque = false;
                foreach (var reference in refs)
                {
                    if (!TryReferenceBody(reference, sheet.Name, out var body) ||
                        !TryParsePrintArea(body, sheet.Id, out var area, out var kind))
                    {
                        opaque = true;
                        break;
                    }
                    areas.Add(area);
                    forms.Add(new WorksheetCustomViewPrintAreaForm(area, kind));
                }
                states[stateIndex] = state with
                {
                    PrintAreas = opaque ? null : areas,
                    PrintAreaReferenceForms = opaque ? null : forms,
                    OpaquePrintAreaExpression = opaque ? name.Value : null
                };
            }
            workbook.CustomViews[viewIndex] = view with { Sheets = states };
        }
    }

    private static IEnumerable<string> SplitDefinedNameReferences(string text)
    {
        var start = 0;
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\'')
            {
                if (quoted && i + 1 < text.Length && text[i + 1] == '\'') { i++; continue; }
                quoted = !quoted;
            }
            else if (text[i] == ',' && !quoted) { yield return text[start..i].Trim(); start = i + 1; }
        }
        yield return text[start..].Trim();
    }

    private static bool TryReferenceBody(string reference, string sheetName, out string body)
    {
        body = string.Empty;
        var separator = reference.LastIndexOf('!');
        if (separator < 0) return false;
        var qualifier = reference[..separator].Trim();
        if (qualifier.Length >= 2 && qualifier[0] == '\'' && qualifier[^1] == '\'')
            qualifier = qualifier[1..^1].Replace("''", "'", StringComparison.Ordinal);
        if (!string.Equals(qualifier, sheetName, StringComparison.OrdinalIgnoreCase)) return false;
        body = reference[(separator + 1)..].Trim();
        return body.Length > 0;
    }

    private static bool TryParsePrintArea(string body, SheetId sheet, out GridRange range,
        out WorksheetCustomViewPrintAreaKind kind)
    {
        range = default;
        kind = WorksheetCustomViewPrintAreaKind.CellRange;
        var plain = body.Replace("$", "", StringComparison.Ordinal).Trim();
        var parts = plain.Split(':');
        if (parts.Length == 2)
        {
            var firstColumn = CellAddress.ColumnNameToNumber(parts[0]);
            var lastColumn = CellAddress.ColumnNameToNumber(parts[1]);
            if (firstColumn is >= 1 and <= CellAddress.MaxCol && lastColumn is >= 1 and <= CellAddress.MaxCol)
            {
                range = new GridRange(new CellAddress(sheet, 1, firstColumn),
                    new CellAddress(sheet, CellAddress.MaxRow, lastColumn));
                kind = WorksheetCustomViewPrintAreaKind.FullColumn;
                return true;
            }
            if (uint.TryParse(parts[0], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var firstRow) &&
                uint.TryParse(parts[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var lastRow) &&
                firstRow is >= 1 and <= CellAddress.MaxRow && lastRow is >= 1 and <= CellAddress.MaxRow)
            {
                range = new GridRange(new CellAddress(sheet, firstRow, 1),
                    new CellAddress(sheet, lastRow, CellAddress.MaxCol));
                kind = WorksheetCustomViewPrintAreaKind.FullRow;
                return true;
            }
        }
        try { range = GridRange.ParseCellOrRange(plain, sheet); return true; }
        catch (FormatException) { return false; }
    }

    private static void WriteCustomViewPrintNames(XDocument xml, XNamespace ns, Workbook workbook,
        IReadOnlyList<(string Id, List<WorksheetCustomViewState> States)> views)
    {
        var root = xml.Root;
        if (root is null) return;
        var names = root.Element(ns + "definedNames");
        if (names is not null)
            foreach (var existing in names.Elements(ns + "definedName").ToList())
                if (IsCustomViewPrintName(existing.Attribute("name")?.Value, out _, out _))
                    existing.Remove();

        var generated = new List<XElement>();
        foreach (var (id, states) in views)
        {
            if (!Guid.TryParse(id.Trim('{', '}'), out var guid)) continue;
            var prefix = "Z_" + guid.ToString("D").ToUpperInvariant().Replace('-', '_') + "_.wvu.";
            foreach (var state in states)
            {
                var sheet = workbook.GetSheet(state.SheetName);
                if (sheet is null) continue;
                var sheetIndex = workbook.IndexOfSheet(sheet.Id);
                var qualifier = SheetNameFormatter.QuoteIfNeeded(sheet.Name) + "!";
                if (state.OpaquePrintAreaExpression is { Length: > 0 } rawArea && state.PrintAreas is null)
                {
                    generated.Add(new XElement(ns + "definedName", new XAttribute("name", prefix + "PrintArea"),
                        new XAttribute("localSheetId", sheetIndex), new XAttribute("hidden", "1"), rawArea));
                }
                else if (state.PrintAreas is { Count: > 0 } areas)
                {
                    static string Abs(CellAddress cell) =>
                        "$" + CellAddress.NumberToColumnName(cell.Col) + "$" + cell.Row;
                    var refs = areas.Select((area, index) =>
                    {
                        var form = state.PrintAreaReferenceForms is { } forms && index < forms.Count
                            ? forms[index] : null;
                        if (form is not null && form.Range == area)
                        {
                            if (form.Kind == WorksheetCustomViewPrintAreaKind.FullColumn &&
                                area.Start.Row == 1 && area.End.Row == CellAddress.MaxRow)
                                return qualifier + "$" + CellAddress.NumberToColumnName(area.Start.Col) +
                                    ":$" + CellAddress.NumberToColumnName(area.End.Col);
                            if (form.Kind == WorksheetCustomViewPrintAreaKind.FullRow &&
                                area.Start.Col == 1 && area.End.Col == CellAddress.MaxCol)
                                return qualifier + "$" + area.Start.Row + ":$" + area.End.Row;
                        }
                        return qualifier + Abs(area.Start) + ":" + Abs(area.End);
                    });
                    generated.Add(new XElement(ns + "definedName", new XAttribute("name", prefix + "PrintArea"),
                        new XAttribute("localSheetId", sheetIndex), new XAttribute("hidden", "1"), string.Join(",", refs)));
                }
                if (state.OpaquePrintTitlesExpression is { Length: > 0 } rawTitles &&
                    state.PrintSettings?.PrintTitleRows is null && state.PrintSettings?.PrintTitleColumns is null)
                {
                    generated.Add(new XElement(ns + "definedName", new XAttribute("name", prefix + "PrintTitles"),
                        new XAttribute("localSheetId", sheetIndex), new XAttribute("hidden", "1"), rawTitles));
                }
                else if (state.PrintSettings is { } print)
                {
                    var refs = new List<string>();
                    if (print.PrintTitleRows is { } rows)
                        refs.Add(qualifier + "$" + rows.Start + ":$" + rows.End);
                    if (print.PrintTitleColumns is { } cols)
                        refs.Add(qualifier + "$" + CellAddress.NumberToColumnName(cols.Start) +
                            ":$" + CellAddress.NumberToColumnName(cols.End));
                    if (refs.Count > 0)
                        generated.Add(new XElement(ns + "definedName", new XAttribute("name", prefix + "PrintTitles"),
                            new XAttribute("localSheetId", sheetIndex), new XAttribute("hidden", "1"), string.Join(",", refs)));
                }
            }
        }
        if (generated.Count == 0) return;
        if (names is null)
        {
            names = new XElement(ns + "definedNames");
            var next = root.Element(ns + "customWorkbookViews");
            if (next is not null) next.AddBeforeSelf(names); else root.Add(names);
        }
        names.Add(generated);
    }

    private static string? ReadActiveSelectionCellReference(XElement customSheetView, XElement? pane, XNamespace worksheetNs)
    {
        // Mirrors XlsxFileAdapter.SheetXmlLayout.ReadActiveSelectionCell: when the view is frozen/
        // split into panes, Excel writes one <selection> per pane and marks the pane holding the
        // true cursor via pane/@activePane (defaulting to "topLeft" when no pane element is
        // present). A <selection> with no @pane attribute implicitly belongs to "topLeft". Picking
        // the first <selection> in document order (rather than the one matching the active pane)
        // silently reports the wrong active cell whenever the user's cursor was left in any pane
        // other than the first one Excel happened to write.
        var activePaneName = pane?.Attribute("activePane")?.Value;
        if (string.IsNullOrWhiteSpace(activePaneName))
            activePaneName = "topLeft";

        string? fallbackReference = null;
        foreach (var selection in customSheetView.Elements(worksheetNs + "selection"))
        {
            var reference = selection.Attribute("activeCell")?.Value ?? selection.Attribute("sqref")?.Value;
            if (string.IsNullOrWhiteSpace(reference))
                continue;

            fallbackReference ??= reference;

            var selectionPaneName = selection.Attribute("pane")?.Value;
            if (string.IsNullOrWhiteSpace(selectionPaneName))
                selectionPaneName = "topLeft";

            if (string.Equals(selectionPaneName, activePaneName, StringComparison.Ordinal))
                return reference;
        }

        return fallbackReference;
    }

    private static WorksheetPageOrientation? ParseOrientation(string? value) => value switch
    {
        "landscape" => WorksheetPageOrientation.Landscape,
        "portrait" => WorksheetPageOrientation.Portrait,
        _ => null
    };

    private static int? ParsePaperSizeCode(XElement? pageSetup) =>
        pageSetup is null ? null : XlsxXmlAttributeReader.ReadIntAttribute(pageSetup, "paperSize");

    private static double? ParseNullableDouble(XElement? element, string attributeName) =>
        element is null ? null : XlsxXmlAttributeReader.ReadDoubleAttribute(element, attributeName);

    private static WorksheetPageMargins? ParsePageMargins(XElement? pageMargins)
    {
        if (pageMargins is null)
            return null;

        var left = XlsxXmlAttributeReader.ReadDoubleAttribute(pageMargins, "left");
        var right = XlsxXmlAttributeReader.ReadDoubleAttribute(pageMargins, "right");
        var top = XlsxXmlAttributeReader.ReadDoubleAttribute(pageMargins, "top");
        var bottom = XlsxXmlAttributeReader.ReadDoubleAttribute(pageMargins, "bottom");
        if (left is null || right is null || top is null || bottom is null)
            return null;

        return XlsxWorksheetValueSanitizer.ValidPageMarginsOrDefault(
            new WorksheetPageMargins(left.Value, right.Value, top.Value, bottom.Value),
            WorksheetPageMargins.Narrow);
    }

    // R115: CT_CustomSheetView's nested <pageSetup> can carry scale/fitToWidth/fitToHeight
    // attributes together even though only one mode is ever actually active -- Excel is known to
    // leave the inactive mode's attribute(s) behind as stale leftovers (the same "sibling attribute
    // goes stale" quirk this codebase already documents for firstPageNumber/useFirstPageNumber; see
    // XlsxFileAdapter.cs). The sibling customSheetView/@fitToPage attribute (read into `fitToPage`
    // above) is the SOLE discriminator for which mode is actually live, mirroring exactly how
    // XlsxFileAdapter.cs resolves the identical ambiguity for the main worksheet via ClosedXML's
    // PagesWide/PagesTall (which itself defers to sheetPr/pageSetUpPr/@fitToPage). Resolving the
    // ambiguity here -- rather than passing all three raw attributes through unconditionally -- keeps
    // the resulting WorksheetScaleToFit from ever encoding "both modes active" the way the main
    // sheet's ScaleToFit never does, so every downstream consumer (ToPageSetupXml below,
    // XlsxWorksheetPageSetupMetadataWriter.DetermineEffectiveFitToPage, and
    // CustomViewCommands.ApplyState copying this straight onto Sheet.ScaleToFit when a view is
    // shown) sees an already-unambiguous value instead of having to re-derive the same priority
    // decision -- and possibly disagreeing with each other, as they did before this fix.
    private static WorksheetScaleToFit? ParseScaleToFit(XElement? pageSetup, bool? fitToPage)
    {
        if (pageSetup is null)
            return null;

        var scale = XlsxXmlAttributeReader.ReadIntAttribute(pageSetup, "scale");
        var fitToWidth = XlsxXmlAttributeReader.ReadIntAttribute(pageSetup, "fitToWidth");
        var fitToHeight = XlsxXmlAttributeReader.ReadIntAttribute(pageSetup, "fitToHeight");
        if (scale is null && fitToWidth is null && fitToHeight is null)
            return null;

        var resolved = fitToPage is true
            ? new WorksheetScaleToFit(null, fitToWidth, fitToHeight)
            : new WorksheetScaleToFit(scale, null, null);

        return XlsxWorksheetValueSanitizer.ValidScaleToFitOrDefault(resolved, WorksheetScaleToFit.Default);
    }

    public static void Save(Stream packageStream, Workbook workbook)
    {
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true);
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        if (workbookEntry is null)
            return;

        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        var workbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
        var workbookRels = XlsxRelationshipReader.LoadTargets(
            archive,
            "xl/_rels/workbook.xml.rels",
            "xl/workbook.xml",
            packageRelNs);
        var sheetPaths = XlsxWorkbookSheetPathReader.GetWorkbookSheetPaths(workbookXml, workbookRels, workbookNs, relNs)
            .ToDictionary(pair => pair.SheetName, pair => pair.WorksheetPath, StringComparer.OrdinalIgnoreCase);
        var sheetsElement = workbookXml.Root?.Element(workbookNs + "sheets");
        var workbookView = workbookXml.Root?.Element(workbookNs + "bookViews")?.Element(workbookNs + "workbookView");
        // Excel refuses to open customWorkbookView without window dimensions even though
        // the OOXML schema marks them optional. Reuse the workbook window when available.
        var windowWidth = workbookView is null ? null : XlsxXmlAttributeReader.ReadIntAttribute(workbookView, "windowWidth");
        var windowHeight = workbookView is null ? null : XlsxXmlAttributeReader.ReadIntAttribute(workbookView, "windowHeight");

        var customViews = workbook.CustomViews
            .Select((view, index) => new
            {
                View = view,
                Id = NormalizeId(view.Id) ?? CreateDeterministicId(view.Name, index),
                States = view.Sheets
                    .GroupBy(state => state.SheetName, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.Last())
                    .ToList()
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.View.Name) && item.States.Count > 0)
            .ToList();
        if (customViews.Count == 0)
            return;

        workbookXml.Root?.Element(workbookNs + "customWorkbookViews")?.Remove();
        InsertWorkbookCustomViewsInOrder(workbookXml.Root, workbookNs, new XElement(
            workbookNs + "customWorkbookViews",
            customViews.Select(item => new XElement(
                workbookNs + "customWorkbookView",
                new XAttribute("name", item.View.Name),
                new XAttribute("guid", item.Id),
                new XAttribute("windowWidth", windowWidth is > 0 ? windowWidth.Value : 20000),
                new XAttribute("windowHeight", windowHeight is > 0 ? windowHeight.Value : 10000),
                new XAttribute("activeSheetId", GetActiveSheetId(sheetsElement, workbookNs, workbook, item.View)),
                item.View.IncludePrintSettings ? new XAttribute("includePrintSettings", "1") : new XAttribute("includePrintSettings", "0"),
                item.View.IncludeHiddenRowsColumnsAndFilterSettings ? new XAttribute("includeHiddenRowCol", "1") : new XAttribute("includeHiddenRowCol", "0")))));
        // R90-io-sheet-view-custom-views-5-3: autoUpdate/mergeInterval/personalView are
        // intentionally NOT written above. Their CT_CustomWorkbookView schema defaults
        // (false/0/false) already match omission, and leaving them absent lets
        // XlsxWorkbookMetadataPreserver.MergeCustomWorkbookViews (MergeMissingAttributes)
        // restore the source file's true values for this view's guid -- hardcoding "0"
        // previously masked those attributes as "already present" and silently discarded
        // the source's real autoUpdate/mergeInterval/personalView on every save.
        WriteCustomViewPrintNames(workbookXml, workbookNs, workbook,
            customViews.Select(item => (item.Id, item.View.IncludePrintSettings ? item.States : new List<WorksheetCustomViewState>())).ToList());
        XlsxPackageXmlEditor.ReplaceXml(archive, "xl/workbook.xml", workbookXml);

        var customViewsBySheet = new Dictionary<string, List<XElement>>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in customViews)
        {
            foreach (var state in item.States)
            {
                if (!customViewsBySheet.TryGetValue(state.SheetName, out var elements))
                {
                    elements = [];
                    customViewsBySheet[state.SheetName] = elements;
                }

                elements.Add(ToCustomSheetViewXml(workbookNs, item.Id, state));
            }
        }

        foreach (var (sheetName, customSheetViews) in customViewsBySheet)
        {
            if (!sheetPaths.TryGetValue(sheetName, out var worksheetPath))
                continue;

            var worksheetEntry = archive.GetEntry(worksheetPath);
            if (worksheetEntry is null)
                continue;

            var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
            var root = worksheetXml.Root;
            if (root is null)
                continue;

            root.Element(workbookNs + "customSheetViews")?.Remove();
            XlsxWorksheetElementOrder.Insert(root, new XElement(
                workbookNs + "customSheetViews",
                customSheetViews));
            XlsxPackageXmlEditor.ReplaceXml(archive, worksheetPath, worksheetXml);
        }
    }

    public static HashSet<string> GetModeledIds(Workbook workbook)
    {
        return workbook.CustomViews
            .Select((view, index) => NormalizeId(view.Id) ?? CreateDeterministicId(view.Name, index))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public static string? NormalizeId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;

        var trimmed = id.Trim();
        if (Guid.TryParse(trimmed.Trim('{', '}'), out var guid))
            return $"{{{guid:D}}}".ToUpperInvariant();

        return trimmed;
    }

    private static string CreateDeterministicId(string name, int index)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes($"FreeX.CustomView:{index}:{name}"));
        return $"{{{new Guid(bytes):D}}}".ToUpperInvariant();
    }

    private static XElement ToCustomSheetViewXml(XNamespace workbookNs, string id, WorksheetCustomViewState state)
    {
        var frozenRows = XlsxWorksheetXmlValueParser.ValidFrozenRowsOrZero(state.FrozenRows);
        var frozenCols = XlsxWorksheetXmlValueParser.ValidFrozenColumnsOrZero(state.FrozenCols);
        var hasFrozenPanes = frozenRows > 0 || frozenCols > 0;
        var splitRow = hasFrozenPanes ? null : state.SplitRow;
        var splitColumn = hasFrozenPanes ? null : state.SplitColumn;
        var autoFilterXml = ToAutoFilterXml(workbookNs, state.AutoFilter);

        var customSheetView = new XElement(
            workbookNs + "customSheetView",
            new XAttribute("guid", id),
            ToCellReference(state.ViewTopRow, state.ViewLeftCol) is { } topLeftCell
                ? new XAttribute("topLeftCell", topLeftCell)
                : null,
            XlsxWorksheetViewWriter.ToXlsxWorksheetViewMode(XlsxWorksheetValueSanitizer.ValidEnumOrDefault(state.ViewMode, WorksheetViewMode.Normal)) is { } view
                ? new XAttribute("view", view)
                : null,
            state.ShowGridlines ? null : new XAttribute("showGridLines", "0"),
            state.ShowHeadings ? null : new XAttribute("showRowCol", "0"),
            state.ShowRulers ? null : new XAttribute("showRuler", "0"),
            state.ZoomPercent == 100 ? null : new XAttribute("scale", XlsxWorksheetValueSanitizer.ValidZoomPercentOrDefault(state.ZoomPercent)),
            state.ShowFormulas ? new XAttribute("showFormulas", "1") : null,
            DetermineEffectiveFitToPage(state) is true ? new XAttribute("fitToPage", "1") : null,
            state.PrintAreas is { Count: > 0 } ||
                state.PrintAreas is null && !string.IsNullOrEmpty(state.OpaquePrintAreaExpression)
                    ? new XAttribute("printArea", "1") : null,
            // ShowAutoFilter ("Show AutoFilter Drop Down Controls") is distinct from the <autoFilter>
            // child element's own ref/criteria: without it, Excel restores the view without the
            // filter dropdown arrows even though the underlying autoFilter element round-trips.
            autoFilterXml is not null ? new XAttribute("showAutoFilter", "1") : null,
            new XAttribute("state", "visible"));

        var hasPanes = hasFrozenPanes || splitRow.HasValue || splitColumn.HasValue;
        var activeCell = ToCellReference(state.ActiveRow, state.ActiveCol);

        // Matches XlsxWorksheetViewWriter.UpdateSheetView's treatment of the primary sheetView:
        // when the view is frozen/split and the captured active cell falls outside the topLeft
        // quadrant, Excel always tags the <pane> with @activePane and the matching <selection>
        // with @pane naming that same quadrant (a <selection> with no @pane, or a <pane> with no
        // @activePane, both default to "topLeft" per ECMA-376 §18.3.1.66/§18.3.1.90). Computing
        // this from the same ComputeActivePaneName the primary writer uses keeps a Custom View
        // capture of a scrolled-past-the-freeze-line cursor from being recorded as though it were
        // sitting in the topLeft pane. Only computed when activeCell itself is valid (matches
        // ToCellReference's own row/col range check), so an out-of-range ActiveRow/ActiveCol never
        // tags the <pane> with an activePane whose corresponding <selection> was never written.
        string? activePaneName = hasPanes && activeCell is not null
            ? XlsxWorksheetViewWriter.ComputeActivePaneName(frozenRows, frozenCols, splitRow, splitColumn, state.ActiveRow!.Value, state.ActiveCol!.Value)
            : null;

        if (hasPanes)
        {
            // Mirrors ReadWorksheetViews above: both state="frozen" and state="split" are written
            // as the literal row/column index here (SplitRow/SplitColumn already model the index,
            // matching Sheet.SplitRow/SplitColumn), not a pane-bar pixel offset.
            customSheetView.Add(new XElement(
                workbookNs + "pane",
                !hasFrozenPanes && splitColumn is { } splitColumnValue ? new XAttribute("xSplit", splitColumnValue) : null,
                !hasFrozenPanes && splitRow is { } splitRowValue ? new XAttribute("ySplit", splitRowValue) : null,
                frozenCols > 0 ? new XAttribute("xSplit", frozenCols) : null,
                frozenRows > 0 ? new XAttribute("ySplit", frozenRows) : null,
                new XAttribute("state", hasFrozenPanes ? "frozen" : "split"),
                activePaneName is not null && !string.Equals(activePaneName, "topLeft", StringComparison.Ordinal)
                    ? new XAttribute("activePane", activePaneName)
                    : null));
        }

        if (activeCell is not null)
        {
            customSheetView.Add(new XElement(
                workbookNs + "selection",
                activePaneName is not null && !string.Equals(activePaneName, "topLeft", StringComparison.Ordinal)
                    ? new XAttribute("pane", activePaneName)
                    : null,
                new XAttribute("activeCell", activeCell),
                new XAttribute("sqref", activeCell)));
        }

        // N13: print settings and AutoFilter have dedicated slots in CT_CustomSheetView
        // (ECMA-376 §18.3.1.90, sequence: pane?, selection*, pageMargins?, printOptions?,
        // pageSetup?, headerFooter?, autoFilter?, extLst?) — hidden-rows/cols/FilterHiddenRows
        // do not (Excel keeps those on the live sheetData row/col elements), so that field is
        // intentionally not written here; see the matching read-side comment above.
        if (state.PrintSettings is { } printSettings)
        {
            if (ToPageBreaksXml(workbookNs, "rowBreaks", printSettings.RowPageBreaks, printSettings.RowPageBreaksMetadata, CellAddress.MaxRow, CellAddress.MaxCol - 1) is { } rowBreaks)
                customSheetView.Add(rowBreaks);
            if (ToPageBreaksXml(workbookNs, "colBreaks", printSettings.ColumnPageBreaks, printSettings.ColumnPageBreaksMetadata, CellAddress.MaxCol, CellAddress.MaxRow - 1) is { } colBreaks)
                customSheetView.Add(colBreaks);
        }
        if (ToPageMarginsXml(workbookNs, state) is { } pageMarginsXml)
        {
            ApplyPrintMetadata(pageMarginsXml, state.PrintSettings?.PageMarginsMetadata, "pageMargins",
                ["left", "right", "top", "bottom", "header", "footer"], []);
            XlsxWorksheetPageLayoutNormalizer.NormalizePageMargins(pageMarginsXml);
            customSheetView.Add(pageMarginsXml);
        }
        if (ToPrintOptionsXml(workbookNs, state) is { } printOptionsXml)
        {
            ApplyPrintMetadata(printOptionsXml, state.PrintSettings?.PrintOptionsMetadata, "printOptions",
                ["gridLines", "headings", "horizontalCentered", "verticalCentered"], []);
            XlsxWorksheetPageLayoutNormalizer.NormalizePrintOptions(printOptionsXml);
            customSheetView.Add(printOptionsXml);
        }
        if (ToPageSetupXml(workbookNs, state) is { } pageSetupXml)
        {
            ApplyPrintMetadata(pageSetupXml, state.PrintSettings?.PageSetupMetadata, "pageSetup",
                ["paperSize", "scale", "firstPageNumber", "fitToWidth", "fitToHeight", "pageOrder",
                 "orientation", "usePrinterDefaults", "blackAndWhite", "draft", "cellComments",
                 "useFirstPageNumber", "errors", "horizontalDpi", "verticalDpi", "copies"], []);
            XlsxWorksheetPageLayoutNormalizer.NormalizePageSetup(pageSetupXml);
            customSheetView.Add(pageSetupXml);
        }
        if (state.PrintSettings is { } headerFooterSettings)
        {
            var headerFooter = ToHeaderFooterXml(workbookNs, headerFooterSettings);
            ApplyPrintMetadata(headerFooter, headerFooterSettings.HeaderFooterMetadata, "headerFooter",
                ["differentOddEven", "differentFirst", "scaleWithDoc", "alignWithMargins"],
                ["oddHeader", "oddFooter", "evenHeader", "evenFooter", "firstHeader", "firstFooter"]);
            XlsxWorksheetPageLayoutNormalizer.NormalizeHeaderFooter(headerFooter);
            customSheetView.Add(headerFooter);
        }
        if (autoFilterXml is not null)
            customSheetView.Add(autoFilterXml);

        return customSheetView;
    }

    /// <summary>
    /// Resolves the customSheetView/@fitToPage attribute from the captured
    /// <see cref="WorksheetCustomViewState.ScaleToFit"/> rather than trusting the raw
    /// <see cref="WorksheetCustomViewState.FitToPage"/> field, mirroring
    /// XlsxWorksheetPageSetupMetadataWriter.DetermineEffectiveFitToPage's identical treatment of
    /// Sheet.FitToPage for the main worksheet. WorksheetCustomViewState.FitToPage can be populated
    /// two ways: (1) round-tripped from a loaded file's customSheetView/@fitToPage, in which case it
    /// was the very attribute ParseScaleToFit above already used to resolve ScaleToFit -- so the two
    /// already agree; or (2) copied from Sheet.FitToPage when the user does View &gt; Custom Views &gt;
    /// Add (see CustomViewCommands/CustomViewStatePlanner.CaptureSheetState) -- and Sheet.FitToPage is
    /// documented (see XlsxWorksheetPageSetupMetadataWriter) as a load-time flag the Page Setup
    /// dialog's scale/fit-to-page toggle never updates, so it can be stale relative to the sheet's
    /// actual current Sheet.ScaleToFit. Deriving from ScaleToFit's own populated field first -- and
    /// only falling back to the raw flag when ScaleToFit itself was never captured -- keeps this
    /// element's fitToPage attribute from ever disagreeing with its own sibling &lt;pageSetup&gt;
    /// (written by ToPageSetupXml from that same ScaleToFit) the way it could before this fix.
    /// </summary>
    private static bool? DetermineEffectiveFitToPage(WorksheetCustomViewState state)
    {
        var scaleToFit = state.ScaleToFit;
        if (scaleToFit is null)
            return state.FitToPage;
        if (scaleToFit.Value.ScalePercent is not null)
            return false;
        if (scaleToFit.Value.FitToPagesWide is not null || scaleToFit.Value.FitToPagesTall is not null)
            return true;
        return state.FitToPage;
    }

    private static XElement? ToPageMarginsXml(XNamespace workbookNs, WorksheetCustomViewState state)
    {
        var margins = state.PageMargins;
        var header = state.HeaderMargin;
        var footer = state.FooterMargin;
        if (margins is null && header is null && footer is null)
            return null;

        var resolvedMargins = XlsxWorksheetValueSanitizer.ValidPageMarginsOrDefault(
            margins ?? WorksheetPageMargins.Narrow, WorksheetPageMargins.Narrow);
        return new XElement(
            workbookNs + "pageMargins",
            new XAttribute("left", resolvedMargins.Left),
            new XAttribute("right", resolvedMargins.Right),
            new XAttribute("top", resolvedMargins.Top),
            new XAttribute("bottom", resolvedMargins.Bottom),
            new XAttribute("header", header ?? 0.3),
            new XAttribute("footer", footer ?? 0.3));
    }

    private static XElement? ToPrintOptionsXml(XNamespace workbookNs, WorksheetCustomViewState state)
    {
        if (state.PrintGridlines is null && state.PrintHeadings is null && state.PrintSettings is null)
            return null;

        return new XElement(
            workbookNs + "printOptions",
            state.PrintGridlines is { } gridlines ? new XAttribute("gridLines", gridlines ? "1" : "0") : null,
            state.PrintHeadings is { } headings ? new XAttribute("headings", headings ? "1" : "0") : null,
            state.PrintSettings?.CenterHorizontallyOnPage is true ? new XAttribute("horizontalCentered", "1") : null,
            state.PrintSettings?.CenterVerticallyOnPage is true ? new XAttribute("verticalCentered", "1") : null);
    }

    private static XElement? ToPageSetupXml(XNamespace workbookNs, WorksheetCustomViewState state)
    {
        if (state.PageOrientation is null && state.PaperSizeCode is null && state.PaperSize is null && state.ScaleToFit is null && state.PrintSettings is null)
            return null;

        var paperSizeCode = state.PaperSizeCode ?? (state.PaperSize is { } paperSize ? PaperSizeCodes.GetCode(paperSize) : (int?)null);
        var scaleToFit = XlsxWorksheetValueSanitizer.ValidScaleToFitOrDefault(
            state.ScaleToFit ?? WorksheetScaleToFit.Default, WorksheetScaleToFit.Default);

        return new XElement(
            workbookNs + "pageSetup",
            paperSizeCode is { } code ? new XAttribute("paperSize", code) : null,
            state.PageOrientation is { } orientation
                ? new XAttribute("orientation", orientation == WorksheetPageOrientation.Landscape ? "landscape" : "portrait")
                : null,
            scaleToFit.FitToPagesWide is null && scaleToFit.FitToPagesTall is null
                ? new XAttribute("scale", scaleToFit.ScalePercent ?? 100)
                : null,
            scaleToFit.FitToPagesWide is { } fitToPagesWide ? new XAttribute("fitToWidth", fitToPagesWide) : null,
            scaleToFit.FitToPagesTall is { } fitToPagesTall ? new XAttribute("fitToHeight", fitToPagesTall) : null,
            state.PrintSettings is { } settings ? new XAttribute("pageOrder", settings.PageOrder == WorksheetPageOrder.OverThenDown ? "overThenDown" : "downThenOver") : null,
            state.PrintSettings?.FirstPageNumber is > 0 and var firstPage ? new XAttribute("firstPageNumber", firstPage) : null,
            state.PrintSettings?.FirstPageNumber is > 0 ? new XAttribute("useFirstPageNumber", "1") : null,
            state.PrintSettings?.UsePrinterDefaults is { } useDefaults ? new XAttribute("usePrinterDefaults", useDefaults ? "1" : "0") : null,
            state.PrintSettings?.PrintCopies is > 0 and var copies ? new XAttribute("copies", copies) : null,
            state.PrintSettings?.PrintBlackAndWhite is true ? new XAttribute("blackAndWhite", "1") : null,
            state.PrintSettings?.PrintDraftQuality is true ? new XAttribute("draft", "1") : null,
            state.PrintSettings?.PrintQualityDpi is > 0 and var dpi ? new XAttribute("horizontalDpi", dpi) : null,
            state.PrintSettings?.PrintQualityVerticalDpi is > 0 and var verticalDpi ? new XAttribute("verticalDpi", verticalDpi) : null,
            state.PrintSettings is { } options ? new XAttribute("errors", options.PrintErrorValue switch { WorksheetPrintErrorValue.Blank => "blank", WorksheetPrintErrorValue.Dash => "dash", WorksheetPrintErrorValue.NotAvailable => "NA", _ => "displayed" }) : null,
            state.PrintSettings is { } comments ? new XAttribute("cellComments", comments.PrintComments switch { WorksheetPrintComments.AtEnd => "atEnd", WorksheetPrintComments.AsDisplayed => "asDisplayed", _ => "none" }) : null);
    }

    private static WorksheetCustomViewPrintSettings? ReadPrintSettings(XElement view, XNamespace ns, bool? fitToPage)
    {
        var headerFooter = view.Element(ns + "headerFooter");
        var pageSetup = view.Element(ns + "pageSetup");
        var printOptions = view.Element(ns + "printOptions");
        var rowBreaks = view.Element(ns + "rowBreaks");
        var colBreaks = view.Element(ns + "colBreaks");
        if (headerFooter is null && pageSetup is null && printOptions is null && rowBreaks is null && colBreaks is null &&
            view.Element(ns + "pageMargins") is null && fitToPage is null)
            return null;

        int? Positive(string name) => pageSetup is null ? null :
            XlsxXmlAttributeReader.ReadIntAttribute(pageSetup, name) is > 0 and var value ? value : null;
        var headerMetadata = XlsxWorksheetLayoutMetadataReader.ReadWorksheetHeaderFooterMetadata(headerFooter);
        var marginsMetadata = XlsxWorksheetLayoutMetadataReader.ReadWorksheetPageMarginsMetadata(view.Element(ns + "pageMargins"));
        var optionsMetadata = XlsxWorksheetLayoutMetadataReader.ReadWorksheetPrintOptionsMetadata(printOptions);
        var setupMetadata = XlsxWorksheetLayoutMetadataReader.ReadWorksheetPageSetupMetadata(pageSetup);
        return new WorksheetCustomViewPrintSettings
        {
            PageHeader = ReadHeaderFooter(headerFooter?.Element(ns + "oddHeader")?.Value),
            PageFooter = ReadHeaderFooter(headerFooter?.Element(ns + "oddFooter")?.Value),
            FirstPageHeader = ReadHeaderFooter(headerFooter?.Element(ns + "firstHeader")?.Value),
            FirstPageFooter = ReadHeaderFooter(headerFooter?.Element(ns + "firstFooter")?.Value),
            EvenPageHeader = ReadHeaderFooter(headerFooter?.Element(ns + "evenHeader")?.Value),
            EvenPageFooter = ReadHeaderFooter(headerFooter?.Element(ns + "evenFooter")?.Value),
            DifferentFirstPageHeaderFooter = XlsxXmlAttributeReader.ReadBoolAttribute(headerFooter, "differentFirst"),
            DifferentOddEvenHeaderFooter = XlsxXmlAttributeReader.ReadBoolAttribute(headerFooter, "differentOddEven"),
            HeaderFooterScaleWithDocument = XlsxXmlAttributeReader.ReadBoolAttribute(headerFooter, "scaleWithDoc", true),
            HeaderFooterAlignWithMargins = XlsxXmlAttributeReader.ReadBoolAttribute(headerFooter, "alignWithMargins", true),
            CenterHorizontallyOnPage = XlsxXmlAttributeReader.ReadBoolAttribute(printOptions, "horizontalCentered"),
            CenterVerticallyOnPage = XlsxXmlAttributeReader.ReadBoolAttribute(printOptions, "verticalCentered"),
            PageOrder = pageSetup?.Attribute("pageOrder")?.Value == "overThenDown" ? WorksheetPageOrder.OverThenDown : WorksheetPageOrder.DownThenOver,
            FirstPageNumber = XlsxXmlAttributeReader.ReadBoolAttribute(pageSetup, "useFirstPageNumber") ? Positive("firstPageNumber") : null,
            UsePrinterDefaults = XlsxXmlAttributeReader.ReadNullableBoolAttribute(pageSetup, "usePrinterDefaults"),
            PrintCopies = Positive("copies"),
            PrintBlackAndWhite = XlsxXmlAttributeReader.ReadBoolAttribute(pageSetup, "blackAndWhite"),
            PrintDraftQuality = XlsxXmlAttributeReader.ReadBoolAttribute(pageSetup, "draft"),
            PrintQualityDpi = Positive("horizontalDpi"),
            PrintQualityVerticalDpi = Positive("verticalDpi"),
            PrintErrorValue = pageSetup?.Attribute("errors")?.Value switch { "blank" => WorksheetPrintErrorValue.Blank, "dash" => WorksheetPrintErrorValue.Dash, "NA" => WorksheetPrintErrorValue.NotAvailable, _ => WorksheetPrintErrorValue.Displayed },
            PrintComments = pageSetup?.Attribute("cellComments")?.Value switch { "atEnd" => WorksheetPrintComments.AtEnd, "asDisplayed" => WorksheetPrintComments.AsDisplayed, _ => WorksheetPrintComments.None },
            RowPageBreaks = ReadPageBreaks(rowBreaks, ns, CellAddress.MaxRow),
            ColumnPageBreaks = ReadPageBreaks(colBreaks, ns, CellAddress.MaxCol),
            RowPageBreaksMetadata = XlsxWorksheetPageBreaksMetadataReader.Read(rowBreaks, CellAddress.MaxRow),
            ColumnPageBreaksMetadata = XlsxWorksheetPageBreaksMetadataReader.Read(colBreaks, CellAddress.MaxCol),
            HeaderFooterMetadata = headerMetadata,
            HeaderFooterMetadataCaptured = headerMetadata is not null,
            PageMarginsMetadata = marginsMetadata,
            PageMarginsMetadataCaptured = marginsMetadata is not null,
            PrintOptionsMetadata = optionsMetadata,
            PrintOptionsMetadataCaptured = optionsMetadata is not null,
            PageSetupMetadata = setupMetadata,
            PageSetupMetadataCaptured = setupMetadata is not null,
            FitToPage = fitToPage
        };
    }

    private static IReadOnlyList<uint> ReadPageBreaks(XElement? parent, XNamespace ns, uint max) =>
        parent?.Elements(ns + "brk")
            .Where(item => XlsxXmlAttributeReader.ReadBoolAttribute(item, "man"))
            .Select(item => XlsxWorksheetPageBreakIdReader.TryReadSupportedId(item, max, out var id) ? id : 0)
            .Where(id => id != 0).Distinct().OrderBy(id => id).ToArray() ?? [];

    private static XElement? ToPageBreaksXml(XNamespace ns, string name, IReadOnlyList<uint> breaks,
        WorksheetPageBreaksMetadataModel? metadata, uint max, uint spanMax)
    {
        var valid = breaks.Where(id => XlsxWorksheetPageBreakIdReader.IsSupported(id, max)).Distinct().OrderBy(id => id).ToArray();
        if (valid.Length == 0 && metadata is null) return null;
        var element = new XElement(ns + name,
            new XAttribute("count", valid.Length), new XAttribute("manualBreakCount", valid.Length));
        XlsxWorksheetNativeMetadataHelpers.ApplyNativeAttributes(element, metadata?.NativeAttributes, ["count", "manualBreakCount"]);
        foreach (var id in valid)
        {
            var child = new XElement(ns + "brk", new XAttribute("id", id),
                new XAttribute("max", spanMax), new XAttribute("man", "1"));
            if (metadata?.BreakNativeAttributes.TryGetValue(id, out var attrs) is true)
                XlsxWorksheetNativeMetadataHelpers.ApplyNativeAttributes(child, attrs, ["id", "man"]);
            element.Add(child);
        }
        XlsxWorksheetPageBreakNormalizer.NormalizeElement(element);
        return element;
    }

    private static WorksheetHeaderFooter ReadHeaderFooter(string? value)
    {
        var sections = new[] { new StringBuilder(), new StringBuilder(), new StringBuilder() };
        var current = sections[1];
        var inFontName = false;
        value ??= string.Empty;
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '&' && i + 1 < value.Length)
            {
                var next = value[i + 1];
                if (next == '&') { current.Append("&&"); i++; continue; }
                if (next == '"') { inFontName = !inFontName; current.Append("&\""); i++; continue; }
                if (!inFontName && next is 'L' or 'C' or 'R')
                {
                    current = sections[next == 'L' ? 0 : next == 'C' ? 1 : 2];
                    i++;
                    continue;
                }
            }
            else if (inFontName && value[i] == '"') inFontName = false;
            current.Append(value[i]);
        }
        return new WorksheetHeaderFooter(
            XlsxWorksheetPageSetupMapper.FromHeaderFooterText(sections[0].ToString()),
            XlsxWorksheetPageSetupMapper.FromHeaderFooterText(sections[1].ToString()),
            XlsxWorksheetPageSetupMapper.FromHeaderFooterText(sections[2].ToString()));
    }

    private static XElement ToHeaderFooterXml(XNamespace ns, WorksheetCustomViewPrintSettings settings)
    {
        static XElement? Element(XNamespace ns, string name, WorksheetHeaderFooter value)
        {
            var text = new StringBuilder();
            if (!string.IsNullOrEmpty(value.Left)) text.Append("&L").Append(XlsxWorksheetPageSetupMapper.ToHeaderFooterText(value.Left));
            if (!string.IsNullOrEmpty(value.Center)) text.Append("&C").Append(XlsxWorksheetPageSetupMapper.ToHeaderFooterText(value.Center));
            if (!string.IsNullOrEmpty(value.Right)) text.Append("&R").Append(XlsxWorksheetPageSetupMapper.ToHeaderFooterText(value.Right));
            return text.Length == 0 ? null : new XElement(ns + name, XmlTextSanitizer.Sanitize(text.ToString()));
        }
        return new XElement(ns + "headerFooter",
            settings.DifferentOddEvenHeaderFooter ? new XAttribute("differentOddEven", "1") : null,
            settings.DifferentFirstPageHeaderFooter ? new XAttribute("differentFirst", "1") : null,
            !settings.HeaderFooterScaleWithDocument ? new XAttribute("scaleWithDoc", "0") : null,
            !settings.HeaderFooterAlignWithMargins ? new XAttribute("alignWithMargins", "0") : null,
            Element(ns, "oddHeader", settings.PageHeader), Element(ns, "oddFooter", settings.PageFooter),
            Element(ns, "evenHeader", settings.EvenPageHeader), Element(ns, "evenFooter", settings.EvenPageFooter),
            Element(ns, "firstHeader", settings.FirstPageHeader), Element(ns, "firstFooter", settings.FirstPageFooter));
    }

    private static void ApplyPrintMetadata(XElement element, NativeXmlPreserveBag? bag, string key,
        IReadOnlyCollection<string> modeledAttributes, IReadOnlyCollection<string> modeledChildren)
    {
        var (attributes, children) = XmlNativeBagSerializer.Deserialize(bag?.Get(key));
        XlsxWorksheetNativeMetadataHelpers.ApplyNativeAttributes(element, attributes, modeledAttributes);
        foreach (var child in children)
            XlsxWorksheetNativeMetadataHelpers.TryAddNativeChildElement(element, child, modeledChildren);
    }

    private static XElement? ToAutoFilterXml(XNamespace workbookNs, WorksheetAutoFilterModel? autoFilter)
    {
        if (autoFilter is null)
            return null;

        // Prefer replaying the exact captured XML (round-trips native attributes/child elements
        // this mapper doesn't model) and fall back to a minimal reference-only element so a
        // programmatically-created AutoFilter (no NativeXml yet) still survives the round-trip.
        if (!string.IsNullOrWhiteSpace(autoFilter.NativeXml))
        {
            try
            {
                var native = XElement.Parse(autoFilter.NativeXml);
                if (native.Name == workbookNs + "autoFilter")
                    return native;
            }
            catch (System.Xml.XmlException)
            {
                // Fall through to the minimal element below.
            }
        }

        return string.IsNullOrWhiteSpace(autoFilter.Reference)
            ? null
            : new XElement(workbookNs + "autoFilter", new XAttribute("ref", autoFilter.Reference));
    }

    /// <summary>
    /// Resolves the 0-based ActiveSheetIndex to the ACTUAL <c>sheetId</c> of the corresponding
    /// <c>&lt;sheet&gt;</c> element in the live <c>&lt;sheets&gt;</c> being written, per
    /// CT_CustomWorkbookView (customWorkbookView/@activeSheetId references a sheetId, not a
    /// position). Excel never reuses/renumbers sheetId across deletes/reorders, so a workbook whose
    /// sheetId values have drifted from position (extremely common) would otherwise get a wrong or
    /// unresolvable activeSheetId. Falls back to the 1-based position only when the index can't be
    /// resolved against &lt;sheets&gt; (e.g. missing/malformed sheetId attributes).
    /// </summary>
    private static int GetActiveSheetId(XElement? sheetsElement, XNamespace workbookNs, Workbook workbook, WorkbookCustomView view)
    {
        var index = view.ActiveSheetIndex ?? workbook.ActiveSheetIndex ?? 0;
        var sheetElements = sheetsElement?.Elements(workbookNs + "sheet").ToList();
        if (sheetElements is not null && index >= 0 && index < sheetElements.Count &&
            XlsxXmlAttributeReader.ReadIntAttribute(sheetElements[index], "sheetId") is { } sheetId and > 0)
        {
            return sheetId;
        }

        var maxSheetId = Math.Max(1, workbook.Sheets.Count);
        return Math.Clamp(index + 1, 1, maxSheetId);
    }

    private static string? ToCellReference(uint? row, uint? column)
    {
        if (row is not (>= 1 and <= CellAddress.MaxRow) ||
            column is not (>= 1 and <= CellAddress.MaxCol))
        {
            return null;
        }

        return new CellAddress(default, row.Value, column.Value).ToA1();
    }

    private static uint? ParseCellRow(string? reference) =>
        TryParseCellReference(reference, out var address) ? address.Row : null;

    private static uint? ParseCellColumn(string? reference) =>
        TryParseCellReference(reference, out var address) ? address.Col : null;

    private static bool TryParseCellReference(string? reference, out CellAddress address)
    {
        if (!string.IsNullOrWhiteSpace(reference))
        {
            var firstReference = reference
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(firstReference) &&
                CellAddress.TryParse(firstReference, default, out address))
            {
                return true;
            }
        }

        address = default;
        return false;
    }

    private static void InsertWorkbookCustomViewsInOrder(
        XElement? workbookRoot,
        XNamespace workbookNs,
        XElement customWorkbookViews)
    {
        if (workbookRoot is null)
            return;

        string[] laterWorkbookElements =
        [
            "pivotCaches",
            "smartTagPr",
            "smartTagTypes",
            "webPublishing",
            "fileRecoveryPr",
            "webPublishObjects",
            "extLst"
        ];

        var insertionPoint = FindFirstLaterElement(workbookRoot, workbookNs, laterWorkbookElements);
        if (insertionPoint is null)
            workbookRoot.Add(customWorkbookViews);
        else
            insertionPoint.AddBeforeSelf(customWorkbookViews);
    }

    private static XElement? FindFirstLaterElement(
        XElement root,
        XNamespace workbookNs,
        string[] laterElementNames)
    {
        foreach (var element in root.Elements())
        {
            if (element.Name.Namespace != workbookNs)
                continue;

            foreach (var laterElementName in laterElementNames)
            {
                if (string.Equals(element.Name.LocalName, laterElementName, StringComparison.Ordinal))
                    return element;
            }
        }

        return null;
    }
}

internal sealed record XlsxWorksheetCustomViewState(string Id, WorksheetCustomViewState State);
