using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetSheetViewNormalizer
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly IReadOnlySet<string> EmptyAttributes = new HashSet<string>(StringComparer.Ordinal);
    private static readonly IReadOnlySet<string> SheetViewAttributes =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "workbookViewId",
            "windowProtection",
            "showFormulas",
            "showGridLines",
            "showRowColHeaders",
            "showZeros",
            "rightToLeft",
            "tabSelected",
            "showRuler",
            "showOutlineSymbols",
            "defaultGridColor",
            "showWhiteSpace",
            "view",
            "topLeftCell",
            "colorId",
            "zoomScale",
            "zoomScaleNormal",
            "zoomScaleSheetLayoutView",
            "zoomScalePageLayoutView"
        };

    private static readonly IReadOnlySet<string> PaneAttributes =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "xSplit",
            "ySplit",
            "topLeftCell",
            "activePane",
            "state"
        };

    private static readonly IReadOnlySet<string> SelectionAttributes =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "pane",
            "activeCell",
            "activeCellId",
            "sqref"
        };

    private static readonly HashSet<string> ValidViewModes =
    [
        "normal",
        "pageBreakPreview",
        "pageLayout"
    ];

    private static readonly HashSet<string> ValidPaneValues =
    [
        "bottomRight",
        "topRight",
        "bottomLeft",
        "topLeft"
    ];

    private static readonly HashSet<string> ValidPaneStates =
    [
        "split",
        "frozen",
        "frozenSplit"
    ];

    private static readonly string[] SheetViewBooleanAttributes =
    [
        "windowProtection",
        "showFormulas",
        "showGridLines",
        "showRowColHeaders",
        "showZeros",
        "rightToLeft",
        "tabSelected",
        "showRuler",
        "showOutlineSymbols",
        "defaultGridColor",
        "showWhiteSpace"
    ];

    private static readonly string[] SheetViewUnsignedIntAttributes =
    [
        "colorId",
        "zoomScale",
        "zoomScaleNormal",
        "zoomScaleSheetLayoutView",
        "zoomScalePageLayoutView"
    ];

    public static bool NormalizeSheetViewsElement(XElement sheetViews)
    {
        var changed = XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(sheetViews, EmptyAttributes);
        foreach (var sheetView in sheetViews.Elements(WorksheetNs + "sheetView"))
            changed |= NormalizeSheetViewElement(sheetView);

        return changed;
    }

    public static bool NormalizeWorksheetRoot(XElement worksheetRoot)
    {
        if (worksheetRoot.Element(WorksheetNs + "sheetViews") is not { } sheetViews)
            return false;

        return NormalizeSheetViewsElement(sheetViews);
    }

    public static void NormalizeWorksheets(ZipArchive archive)
    {
        foreach (var worksheetEntry in archive.Entries.Where(XlsxPackagePath.IsWorksheetXmlEntry).ToList())
        {
            var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
            var root = worksheetXml.Root;
            if (root is null || !NormalizeWorksheetRoot(root))
                continue;

            XlsxPackageXmlEditor.ReplaceXml(archive, worksheetEntry.FullName, worksheetXml);
        }
    }

    public static bool NormalizeSheetViewElement(XElement sheetView)
    {
        var changed = XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(sheetView, SheetViewAttributes);

        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(sheetView, "workbookViewId", XlsxXmlNormalizationHelpers.NormalizeRequiredUnsignedInt);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(sheetView, "view", value => XlsxXmlNormalizationHelpers.NormalizeToken(value, ValidViewModes));
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(sheetView, "topLeftCell", NormalizeCellReference);

        foreach (var attributeName in SheetViewBooleanAttributes)
            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(sheetView, attributeName, XlsxXmlNormalizationHelpers.NormalizeBoolean);
        foreach (var attributeName in SheetViewUnsignedIntAttributes)
            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(sheetView, attributeName, XlsxXmlNormalizationHelpers.NormalizeUnsignedIntOrNull);

        foreach (var pane in sheetView.Elements(WorksheetNs + "pane"))
            changed |= NormalizePaneElement(pane);
        foreach (var selection in sheetView.Elements(WorksheetNs + "selection"))
            changed |= NormalizeSelectionElement(selection);

        return changed;
    }

    private static bool NormalizePaneElement(XElement pane)
    {
        var changed = XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(pane, PaneAttributes);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(pane, "xSplit", NormalizeDouble);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(pane, "ySplit", NormalizeDouble);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(pane, "topLeftCell", NormalizeCellReference);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(pane, "activePane", value => XlsxXmlNormalizationHelpers.NormalizeToken(value, ValidPaneValues));
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(pane, "state", value => XlsxXmlNormalizationHelpers.NormalizeToken(value, ValidPaneStates));
        return changed;
    }

    private static bool NormalizeSelectionElement(XElement selection)
    {
        var changed = XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(selection, SelectionAttributes);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(selection, "pane", value => XlsxXmlNormalizationHelpers.NormalizeToken(value, ValidPaneValues));
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(selection, "activeCell", NormalizeCellReference);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(selection, "activeCellId", XlsxXmlNormalizationHelpers.NormalizeUnsignedIntOrNull);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(selection, "sqref", NormalizeSqref);
        return changed;
    }

    private static string? NormalizeCellReference(string? value)
    {
        var trimmed = value?.Trim();
        return trimmed is not null && CellAddress.TryParse(trimmed, SheetId.New(), out _)
            ? trimmed
            : null;
    }

    private static string? NormalizeDouble(string? value)
    {
        var trimmed = value?.Trim();
        if (!double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ||
            !double.IsFinite(parsed))
        {
            return null;
        }

        return XlsxNumberFormatting.ToXmlString(parsed);
    }

    private static string? NormalizeSqref(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return null;

        var tokens = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return tokens.Length > 0 && tokens.All(IsCellOrRangeReference)
            ? string.Join(' ', tokens)
            : null;
    }

    private static bool IsCellOrRangeReference(string token)
    {
        var parts = token.Split(':');
        if (parts.Length == 1)
            return CellAddress.TryParse(parts[0], SheetId.New(), out _);

        if (parts.Length != 2)
            return false;

        if (CellAddress.TryParse(parts[0], SheetId.New(), out _) &&
            CellAddress.TryParse(parts[1], SheetId.New(), out _))
        {
            return true;
        }

        // Whole-column ("A:A", "C:E") and whole-row ("3:3", "3:5") selection sqrefs are valid
        // Excel selection references even though neither side parses as a full cell address.
        if (IsColumnOnlyReference(parts[0]) && IsColumnOnlyReference(parts[1]))
            return true;

        return IsRowOnlyReference(parts[0]) && IsRowOnlyReference(parts[1]);
    }

    private static bool IsColumnOnlyReference(string value)
    {
        if (value.Length is 0 or > 3)
            return false;

        foreach (var c in value)
        {
            if (c is (< 'A' or > 'Z') and (< 'a' or > 'z'))
                return false;
        }

        var column = CellAddress.ColumnNameToNumber(value);
        return column is > 0 and <= CellAddress.MaxCol;
    }

    private static bool IsRowOnlyReference(string value)
    {
        if (value.Length is 0 or > 7)
            return false;

        uint row = 0;
        foreach (var c in value)
        {
            if (c is < '0' or > '9')
                return false;

            row = row * 10 + (uint)(c - '0');
            if (row > CellAddress.MaxRow)
                return false;
        }

        return row > 0;
    }
}
