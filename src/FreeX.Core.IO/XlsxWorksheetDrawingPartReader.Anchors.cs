using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static partial class XlsxWorksheetDrawingPartReader
{
    private static XlsxDrawingAnchor? ReadNearestAnchor(XElement element)
    {
        XNamespace spreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
        var anchor = FindNearestAnchorElement(element, spreadsheetDrawingNs);
        return anchor is null ? null : TryReadAnchor(anchor, spreadsheetDrawingNs);
    }

    private static int ReadNearestAnchorOrderIndex(XElement element)
    {
        XNamespace spreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
        var anchor = FindNearestAnchorElement(element, spreadsheetDrawingNs);
        return anchor is null ? -1 : ReadAnchorOrderIndex(anchor, spreadsheetDrawingNs);
    }

    private static XElement? FindNearestAnchorElement(XElement element, XNamespace spreadsheetDrawingNs)
    {
        foreach (var candidate in element.Ancestors())
        {
            if (IsSpreadsheetDrawingAnchor(candidate, spreadsheetDrawingNs))
                return candidate;
        }

        return null;
    }

    private static int ReadAnchorOrderIndex(XElement anchor, XNamespace spreadsheetDrawingNs)
    {
        if (anchor.Parent is null)
            return -1;

        var index = 0;
        foreach (var sibling in anchor.Parent.Elements())
        {
            if (!IsSpreadsheetDrawingAnchor(sibling, spreadsheetDrawingNs))
                continue;

            if (ReferenceEquals(sibling, anchor))
                return index;

            index++;
        }

        return -1;
    }

    private static XlsxDrawingAnchor? TryReadAnchor(XElement anchor, XNamespace spreadsheetDrawingNs)
    {
        if (anchor.Name == spreadsheetDrawingNs + "twoCellAnchor")
            return TryReadTwoCellAnchor(anchor);
        if (anchor.Name == spreadsheetDrawingNs + "oneCellAnchor")
            return TryReadOneCellAnchor(anchor);
        return anchor.Name == spreadsheetDrawingNs + "absoluteAnchor"
            ? TryReadAbsoluteAnchor(anchor)
            : null;
    }

    private static bool IsSpreadsheetDrawingAnchor(XElement element, XNamespace spreadsheetDrawingNs) =>
        element.Name == spreadsheetDrawingNs + "twoCellAnchor" ||
        element.Name == spreadsheetDrawingNs + "oneCellAnchor" ||
        element.Name == spreadsheetDrawingNs + "absoluteAnchor";

    private static XlsxDrawingAnchor? TryReadTwoCellAnchor(XElement anchor)
    {
        XNamespace spreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
        var from = anchor.Element(spreadsheetDrawingNs + "from");
        var to = anchor.Element(spreadsheetDrawingNs + "to");
        if (from is null || to is null)
            return null;

        if (!TryReadAnchorCoordinate(from, spreadsheetDrawingNs, out var fromRow, out var fromCol, out var fromRowOffset, out var fromColOffset) ||
            !TryReadAnchorCoordinate(to, spreadsheetDrawingNs, out var toRow, out var toCol, out var toRowOffset, out var toColOffset))
        {
            return null;
        }

        // A two-cell anchor is valid as long as the to-marker is not before the from-marker in at
        // least one axis.  Reject only when the anchor is inverted (or degenerate) in BOTH the
        // column direction AND the row direction simultaneously.  A flat horizontal line has cy=0 so
        // its from/to share the same row and rowOffset — that's fine as long as toCol > fromCol.
        // Conversely, a vertical line is fine when toRow > fromRow even if the columns are equal.
        var colInvalid = toCol < fromCol || (toCol == fromCol && toColOffset <= fromColOffset);
        var rowInvalid = toRow < fromRow || (toRow == fromRow && toRowOffset <= fromRowOffset);
        if (colInvalid && rowInvalid)
            return null;

        return new XlsxDrawingAnchor(
            ChartDrawingAnchorKind.TwoCell,
            fromRow,
            fromCol,
            fromRowOffset,
            fromColOffset,
            AbsoluteLeft: null,
            AbsoluteTop: null,
            toRow,
            toCol,
            toRowOffset,
            toColOffset,
            Width: null,
            Height: null);
    }

    private static XlsxDrawingAnchor? TryReadOneCellAnchor(XElement anchor)
    {
        XNamespace spreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
        var from = anchor.Element(spreadsheetDrawingNs + "from");
        var ext = anchor.Element(spreadsheetDrawingNs + "ext");
        if (from is null || ext is null)
            return null;

        if (!TryReadAnchorCoordinate(from, spreadsheetDrawingNs, out var fromRow, out var fromCol, out var fromRowOffset, out var fromColOffset))
            return null;

        var width = DrawingMlCoordinateUnits.EmuToPixels(ext.Attribute("cx")?.Value);
        var height = DrawingMlCoordinateUnits.EmuToPixels(ext.Attribute("cy")?.Value);
        // A schema-valid ext CAN be zero on one axis alone (e.g. a flat/degenerate shape flush against a
        // cell edge); only reject the anchor outright when BOTH axes are non-positive, since then there is
        // no usable size or position information left at all. Rejecting on a single zero axis used to drop
        // the whole anchor -- position AND the still-meaningful other-axis size -- reloading the object at
        // A1 with a default size instead of its real from-cell position and real dimension.
        if (width <= 0 && height <= 0)
            return null;
        width = Math.Max(0, width);
        height = Math.Max(0, height);

        return new XlsxDrawingAnchor(
            ChartDrawingAnchorKind.OneCell,
            fromRow,
            fromCol,
            fromRowOffset,
            fromColOffset,
            AbsoluteLeft: null,
            AbsoluteTop: null,
            ToRowZeroBased: null,
            ToColumnZeroBased: null,
            ToRowOffset: null,
            ToColumnOffset: null,
            width,
            height);
    }

    private static XlsxDrawingAnchor? TryReadAbsoluteAnchor(XElement anchor)
    {
        XNamespace spreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
        var pos = anchor.Element(spreadsheetDrawingNs + "pos");
        var ext = anchor.Element(spreadsheetDrawingNs + "ext");
        if (pos is null || ext is null)
            return null;

        var left = DrawingMlCoordinateUnits.EmuToPixels(pos.Attribute("x")?.Value);
        var top = DrawingMlCoordinateUnits.EmuToPixels(pos.Attribute("y")?.Value);
        var width = DrawingMlCoordinateUnits.EmuToPixels(ext.Attribute("cx")?.Value);
        var height = DrawingMlCoordinateUnits.EmuToPixels(ext.Attribute("cy")?.Value);
        // See the matching comment in TryReadOneCellAnchor: only reject when BOTH axes are non-positive,
        // otherwise the still-meaningful position (pos) and the non-degenerate axis are lost too.
        if (width <= 0 && height <= 0)
            return null;
        width = Math.Max(0, width);
        height = Math.Max(0, height);

        return new XlsxDrawingAnchor(
            ChartDrawingAnchorKind.Absolute,
            FromRowZeroBased: 0,
            FromColumnZeroBased: 0,
            FromRowOffset: 0,
            FromColumnOffset: 0,
            left,
            top,
            ToRowZeroBased: null,
            ToColumnZeroBased: null,
            ToRowOffset: null,
            ToColumnOffset: null,
            width,
            height);
    }

    private static bool TryReadAnchorCoordinate(
        XElement marker,
        XNamespace spreadsheetDrawingNs,
        out uint rowZeroBased,
        out uint columnZeroBased,
        out double rowOffset,
        out double columnOffset)
    {
        rowZeroBased = 0;
        columnZeroBased = 0;
        rowOffset = DrawingMlCoordinateUnits.EmuToPixels(marker.Element(spreadsheetDrawingNs + "rowOff")?.Value);
        columnOffset = DrawingMlCoordinateUnits.EmuToPixels(marker.Element(spreadsheetDrawingNs + "colOff")?.Value);
        return uint.TryParse(marker.Element(spreadsheetDrawingNs + "row")?.Value, out rowZeroBased) &&
               uint.TryParse(marker.Element(spreadsheetDrawingNs + "col")?.Value, out columnZeroBased);
    }
}
