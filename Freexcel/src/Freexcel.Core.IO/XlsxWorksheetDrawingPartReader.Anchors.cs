using System.Globalization;
using System.Xml.Linq;

namespace Freexcel.Core.IO;

internal static partial class XlsxWorksheetDrawingPartReader
{
    private static XlsxDrawingAnchor? ReadNearestAnchor(XElement element)
    {
        XNamespace spreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
        return element
            .Ancestors(spreadsheetDrawingNs + "twoCellAnchor")
            .Select(TryReadTwoCellAnchor)
            .FirstOrDefault(candidate => candidate is not null)
            ?? element
                .Ancestors(spreadsheetDrawingNs + "oneCellAnchor")
                .Select(TryReadOneCellAnchor)
                .FirstOrDefault(candidate => candidate is not null)
            ?? element
                .Ancestors(spreadsheetDrawingNs + "absoluteAnchor")
                .Select(TryReadAbsoluteAnchor)
                .FirstOrDefault(candidate => candidate is not null);
    }

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
