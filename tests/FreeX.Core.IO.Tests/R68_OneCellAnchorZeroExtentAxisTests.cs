using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R68-io-drawing-anchor-6-2: a <c>oneCellAnchor</c>/<c>absoluteAnchor</c> with a schema-valid ZERO-width or
/// zero-height <c>&lt;xdr:ext&gt;</c> used to drop the WHOLE anchor -- position AND the still-meaningful
/// non-zero axis -- because <c>TryReadOneCellAnchor</c>/<c>TryReadAbsoluteAnchor</c> rejected on
/// <c>width &lt;= 0 || height &lt;= 0</c>. A <c>twoCellAnchor</c> already tolerated a flat (zero-extent-one-
/// axis) span (see the from/to marker validity check in <c>TryReadTwoCellAnchor</c>), so a degenerate single
/// axis should only clamp that axis to zero, not discard the whole anchor and reload the object at A1 with a
/// default size.
/// </summary>
public sealed class R68_OneCellAnchorZeroExtentAxisTests
{
    private static XNamespace SpreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
    private static XNamespace DrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";

    private static XDocument BuildOneCellAnchorTextBoxDrawing(string cx, string cy) =>
        new(new XElement(SpreadsheetDrawingNs + "wsDr",
            new XAttribute(XNamespace.Xmlns + "xdr", SpreadsheetDrawingNs),
            new XAttribute(XNamespace.Xmlns + "a", DrawingNs),
            new XElement(SpreadsheetDrawingNs + "oneCellAnchor",
                new XElement(SpreadsheetDrawingNs + "from",
                    new XElement(SpreadsheetDrawingNs + "col", "2"),
                    new XElement(SpreadsheetDrawingNs + "colOff", "0"),
                    new XElement(SpreadsheetDrawingNs + "row", "3"),
                    new XElement(SpreadsheetDrawingNs + "rowOff", "0")),
                new XElement(SpreadsheetDrawingNs + "ext",
                    new XAttribute("cx", cx),
                    new XAttribute("cy", cy)),
                new XElement(SpreadsheetDrawingNs + "sp",
                    new XElement(SpreadsheetDrawingNs + "nvSpPr",
                        new XElement(SpreadsheetDrawingNs + "cNvPr", new XAttribute("id", "2"), new XAttribute("name", "TextBox 1")),
                        new XElement(SpreadsheetDrawingNs + "cNvSpPr", new XAttribute("txBox", "1"))),
                    new XElement(SpreadsheetDrawingNs + "spPr",
                        new XElement(DrawingNs + "prstGeom", new XAttribute("prst", "rect"))),
                    new XElement(SpreadsheetDrawingNs + "txBody",
                        new XElement(DrawingNs + "p",
                            new XElement(DrawingNs + "r",
                                new XElement(DrawingNs + "t", "Hi"))))),
                new XElement(SpreadsheetDrawingNs + "clientData"))));

    [Fact]
    public void ReadShapeParts_OneCellAnchor_ZeroWidthExt_KeepsAnchorAtRealFromCellWithHeightPreserved()
    {
        // cx=0 (degenerate width axis), cy=457200 EMU (= 48 px) real height.
        var drawingXml = BuildOneCellAnchorTextBoxDrawing(cx: "0", cy: "457200");

        var (textBoxes, _) = XlsxWorksheetDrawingPartReader.ReadShapeParts(drawingXml);

        textBoxes.Should().HaveCount(1);
        var anchor = textBoxes[0].Anchor;
        anchor.Should().NotBeNull("a zero-extent single axis must not drop the whole anchor (position lost, reload at A1)");
        anchor!.FromColumnZeroBased.Should().Be(2, "the real from-cell column must be preserved, not reset to A1");
        anchor.FromRowZeroBased.Should().Be(3, "the real from-cell row must be preserved, not reset to A1");
        anchor.Height.Should().Be(48, "the non-degenerate height axis (457200 EMU = 48px) must be preserved");
        anchor.Width.Should().Be(0, "the degenerate width axis clamps to zero rather than discarding the anchor");
    }

    [Fact]
    public void ReadShapeParts_OneCellAnchor_ZeroHeightExt_KeepsAnchorAtRealFromCellWithWidthPreserved()
    {
        // Mirror case: cy=0 (degenerate height axis), cx=914400 EMU (= 96 px) real width.
        var drawingXml = BuildOneCellAnchorTextBoxDrawing(cx: "914400", cy: "0");

        var (textBoxes, _) = XlsxWorksheetDrawingPartReader.ReadShapeParts(drawingXml);

        textBoxes.Should().HaveCount(1);
        var anchor = textBoxes[0].Anchor;
        anchor.Should().NotBeNull();
        anchor!.FromColumnZeroBased.Should().Be(2);
        anchor.FromRowZeroBased.Should().Be(3);
        anchor.Width.Should().Be(96, "the non-degenerate width axis (914400 EMU = 96px) must be preserved");
        anchor.Height.Should().Be(0, "the degenerate height axis clamps to zero rather than discarding the anchor");
    }

    [Fact]
    public void ReadShapeParts_OneCellAnchor_BothAxesZero_StillDropsTheAnchor_NoRegression()
    {
        // Sibling no-regression case: when BOTH axes are non-positive there is no usable size information
        // left at all, so the anchor is still rejected exactly as before.
        var drawingXml = BuildOneCellAnchorTextBoxDrawing(cx: "0", cy: "0");

        var (textBoxes, _) = XlsxWorksheetDrawingPartReader.ReadShapeParts(drawingXml);

        textBoxes.Should().HaveCount(1);
        textBoxes[0].Anchor.Should().BeNull("an anchor with no usable extent on either axis has nothing worth keeping");
    }

    [Fact]
    public void ReadShapeParts_OneCellAnchor_OrdinaryNonZeroExt_StillReadsBothAxes_NoRegression()
    {
        // Sibling no-regression case: an ordinary oneCellAnchor with both axes positive (the overwhelming
        // common case) must keep reading both dimensions unaffected by the single-axis clamp change.
        var drawingXml = BuildOneCellAnchorTextBoxDrawing(cx: "914400", cy: "457200");

        var (textBoxes, _) = XlsxWorksheetDrawingPartReader.ReadShapeParts(drawingXml);

        textBoxes.Should().HaveCount(1);
        var anchor = textBoxes[0].Anchor;
        anchor.Should().NotBeNull();
        anchor!.Width.Should().Be(96);
        anchor.Height.Should().Be(48);
    }

    private static XDocument BuildAbsoluteAnchorTextBoxDrawing(string cx, string cy) =>
        new(new XElement(SpreadsheetDrawingNs + "wsDr",
            new XAttribute(XNamespace.Xmlns + "xdr", SpreadsheetDrawingNs),
            new XAttribute(XNamespace.Xmlns + "a", DrawingNs),
            new XElement(SpreadsheetDrawingNs + "absoluteAnchor",
                new XElement(SpreadsheetDrawingNs + "pos", new XAttribute("x", "914400"), new XAttribute("y", "457200")),
                new XElement(SpreadsheetDrawingNs + "ext",
                    new XAttribute("cx", cx),
                    new XAttribute("cy", cy)),
                new XElement(SpreadsheetDrawingNs + "sp",
                    new XElement(SpreadsheetDrawingNs + "nvSpPr",
                        new XElement(SpreadsheetDrawingNs + "cNvPr", new XAttribute("id", "2"), new XAttribute("name", "TextBox 1")),
                        new XElement(SpreadsheetDrawingNs + "cNvSpPr", new XAttribute("txBox", "1"))),
                    new XElement(SpreadsheetDrawingNs + "spPr",
                        new XElement(DrawingNs + "prstGeom", new XAttribute("prst", "rect"))),
                    new XElement(SpreadsheetDrawingNs + "txBody",
                        new XElement(DrawingNs + "p",
                            new XElement(DrawingNs + "r",
                                new XElement(DrawingNs + "t", "Hi"))))),
                new XElement(SpreadsheetDrawingNs + "clientData"))));

    [Fact]
    public void ReadShapeParts_AbsoluteAnchor_ZeroWidthExt_KeepsAnchorAtRealPositionWithHeightPreserved()
    {
        // Mirrors the oneCellAnchor case: an absoluteAnchor with a degenerate width axis alone must keep
        // its real pixel position (pos) and the non-degenerate height, not be dropped entirely.
        var drawingXml = BuildAbsoluteAnchorTextBoxDrawing(cx: "0", cy: "457200");

        var (textBoxes, _) = XlsxWorksheetDrawingPartReader.ReadShapeParts(drawingXml);

        textBoxes.Should().HaveCount(1);
        var anchor = textBoxes[0].Anchor;
        anchor.Should().NotBeNull("a zero-extent single axis must not drop the whole absoluteAnchor (position lost)");
        anchor!.AbsoluteLeft.Should().Be(96, "the real absolute pixel position (914400 EMU = 96px) must be preserved");
        anchor.AbsoluteTop.Should().Be(48);
        anchor.Height.Should().Be(48, "the non-degenerate height axis must be preserved");
        anchor.Width.Should().Be(0, "the degenerate width axis clamps to zero rather than discarding the anchor");
    }
}
