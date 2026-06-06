using System.Globalization;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetSheetViewNormalizer
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

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
        var changed = false;
        foreach (var sheetView in sheetViews.Elements(WorksheetNs + "sheetView"))
            changed |= NormalizeSheetViewElement(sheetView);

        return changed;
    }

    public static bool NormalizeSheetViewElement(XElement sheetView)
    {
        var changed = false;

        changed |= NormalizeAttribute(sheetView, "workbookViewId", NormalizeRequiredUnsignedInt);
        changed |= NormalizeAttribute(sheetView, "view", value => NormalizeToken(value, ValidViewModes));
        changed |= NormalizeAttribute(sheetView, "topLeftCell", NormalizeCellReference);

        foreach (var attributeName in SheetViewBooleanAttributes)
            changed |= NormalizeAttribute(sheetView, attributeName, NormalizeBoolean);
        foreach (var attributeName in SheetViewUnsignedIntAttributes)
            changed |= NormalizeAttribute(sheetView, attributeName, NormalizeUnsignedIntOrNull);

        foreach (var pane in sheetView.Elements(WorksheetNs + "pane"))
            changed |= NormalizePaneElement(pane);
        foreach (var selection in sheetView.Elements(WorksheetNs + "selection"))
            changed |= NormalizeSelectionElement(selection);

        return changed;
    }

    private static bool NormalizePaneElement(XElement pane)
    {
        var changed = false;
        changed |= NormalizeAttribute(pane, "xSplit", NormalizeDouble);
        changed |= NormalizeAttribute(pane, "ySplit", NormalizeDouble);
        changed |= NormalizeAttribute(pane, "topLeftCell", NormalizeCellReference);
        changed |= NormalizeAttribute(pane, "activePane", value => NormalizeToken(value, ValidPaneValues));
        changed |= NormalizeAttribute(pane, "state", value => NormalizeToken(value, ValidPaneStates));
        return changed;
    }

    private static bool NormalizeSelectionElement(XElement selection)
    {
        var changed = false;
        changed |= NormalizeAttribute(selection, "pane", value => NormalizeToken(value, ValidPaneValues));
        changed |= NormalizeAttribute(selection, "activeCell", NormalizeCellReference);
        changed |= NormalizeAttribute(selection, "activeCellId", NormalizeUnsignedIntOrNull);
        changed |= NormalizeAttribute(selection, "sqref", NormalizeSqref);
        return changed;
    }

    private static bool NormalizeAttribute(
        XElement element,
        string attributeName,
        Func<string?, string?> normalize)
    {
        var attribute = element.Attribute(attributeName);
        var normalized = normalize(attribute?.Value);
        if (normalized is null)
        {
            if (attribute is null)
                return false;

            attribute.Remove();
            return true;
        }

        if (attribute is not null && string.Equals(attribute.Value, normalized, StringComparison.Ordinal))
            return false;

        element.SetAttributeValue(attributeName, normalized);
        return true;
    }

    private static string? NormalizeBoolean(string? value)
    {
        var trimmed = value?.Trim();
        return trimmed switch
        {
            "0" or "1" => trimmed,
            "true" or "false" => trimmed,
            _ => null
        };
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

        return parsed.ToString("G17", CultureInfo.InvariantCulture);
    }

    private static string? NormalizeRequiredUnsignedInt(string? value) =>
        NormalizeUnsignedIntOrNull(value) ?? "0";

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

    private static string? NormalizeToken(string? value, IReadOnlySet<string> allowedValues)
    {
        var trimmed = value?.Trim();
        return trimmed is not null && allowedValues.Contains(trimmed) ? trimmed : null;
    }

    private static string? NormalizeUnsignedIntOrNull(string? value)
    {
        var trimmed = value?.Trim();
        return uint.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            ? parsed.ToString(CultureInfo.InvariantCulture)
            : null;
    }

    private static bool IsCellOrRangeReference(string token)
    {
        var parts = token.Split(':');
        if (parts.Length == 1)
            return CellAddress.TryParse(parts[0], SheetId.New(), out _);

        return parts.Length == 2 &&
               CellAddress.TryParse(parts[0], SheetId.New(), out _) &&
               CellAddress.TryParse(parts[1], SheetId.New(), out _);
    }
}
