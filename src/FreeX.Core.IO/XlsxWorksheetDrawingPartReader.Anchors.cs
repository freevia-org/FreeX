using System.Globalization;
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

        if (toRow <= fromRow || toCol <= fromCol)
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

        var width = EmusToPixels(ext.Attribute("cx")?.Value);
        var height = EmusToPixels(ext.Attribute("cy")?.Value);
        if (width <= 0 || height <= 0)
            return null;

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

        var left = EmusToPixels(pos.Attribute("x")?.Value);
        var top = EmusToPixels(pos.Attribute("y")?.Value);
        var width = EmusToPixels(ext.Attribute("cx")?.Value);
        var height = EmusToPixels(ext.Attribute("cy")?.Value);
        if (width <= 0 || height <= 0)
            return null;

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
        rowOffset = EmusToPixels(marker.Element(spreadsheetDrawingNs + "rowOff")?.Value);
        columnOffset = EmusToPixels(marker.Element(spreadsheetDrawingNs + "colOff")?.Value);
        return uint.TryParse(marker.Element(spreadsheetDrawingNs + "row")?.Value, out rowZeroBased) &&
               uint.TryParse(marker.Element(spreadsheetDrawingNs + "col")?.Value, out columnZeroBased);
    }

    private static double EmusToPixels(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var emus)
            ? emus / 9525.0
            : 0;
}
