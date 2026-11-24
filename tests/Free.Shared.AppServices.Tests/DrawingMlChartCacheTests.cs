using System.Globalization;
using System.Xml.Linq;
using Free.Shared.Drawing;

namespace Free.Shared.AppServices.Tests;

/// <summary>
/// Coverage for the shared <c>c:numCache</c>/<c>c:strCache</c> builders that FreeX, FreeW and FreeP now
/// call instead of three private copies. The expected XML is exactly what FreeW and FreeP produced before
/// (FreeX produced the same shape, differing only in number spelling: see
/// <see cref="NumberSpellingRoundTripsEveryDouble"/>).
/// </summary>
public sealed class DrawingMlChartCacheTests
{
    private static readonly XNamespace C = "http://schemas.openxmlformats.org/drawingml/2006/chart";

    private static XElement Strip(XElement element)
    {
        var clone = new XElement(element);
        foreach (var e in clone.DescendantsAndSelf())
            e.Attributes().Where(a => a.IsNamespaceDeclaration).Remove();
        return clone;
    }

    private static void AssertXml(XElement actual, string expected) =>
        XNode.DeepEquals(Strip(actual), Strip(XElement.Parse(expected))).Should().BeTrue("actual: " + actual);

    [Fact]
    public void NumericCacheMatchesTheFreeWAndFreePShape()
    {
        var cache = DrawingMlChartCache.CreateNumericData(
            C,
            "numCache",
            "General",
            3,
            [new(0, "1.5"), new(2, "-4")]);

        AssertXml(cache,
            "<c:numCache xmlns:c=\"http://schemas.openxmlformats.org/drawingml/2006/chart\">"
            + "<c:formatCode>General</c:formatCode><c:ptCount val=\"3\" />"
            + "<c:pt idx=\"0\"><c:v>1.5</c:v></c:pt><c:pt idx=\"2\"><c:v>-4</c:v></c:pt>"
            + "</c:numCache>");
    }

    [Fact]
    public void NumericLiteralUsesTheSameChildrenUnderTheLiteralName()
    {
        var literal = DrawingMlChartCache.CreateNumericData(C, "numLit", "0.00", 1, [new(0, "2")]);

        AssertXml(literal,
            "<c:numLit xmlns:c=\"http://schemas.openxmlformats.org/drawingml/2006/chart\">"
            + "<c:formatCode>0.00</c:formatCode><c:ptCount val=\"1\" /><c:pt idx=\"0\"><c:v>2</c:v></c:pt></c:numLit>");
    }

    [Fact]
    public void StringCacheMatchesTheFreeWAndFreePShape()
    {
        var cache = DrawingMlChartCache.CreateStringData(C, "strCache", 2, [new(0, "North"), new(1, "South")]);

        AssertXml(cache,
            "<c:strCache xmlns:c=\"http://schemas.openxmlformats.org/drawingml/2006/chart\">"
            + "<c:ptCount val=\"2\" />"
            + "<c:pt idx=\"0\"><c:v>North</c:v></c:pt><c:pt idx=\"1\"><c:v>South</c:v></c:pt>"
            + "</c:strCache>");
    }

    [Fact]
    public void EmptyPointListStillDeclaresTheCount()
    {
        var cache = DrawingMlChartCache.CreateStringData(C, "strLit", 0, []);

        AssertXml(cache,
            "<c:strLit xmlns:c=\"http://schemas.openxmlformats.org/drawingml/2006/chart\"><c:ptCount val=\"0\" /></c:strLit>");
    }

    [Theory]
    [InlineData(0.0, "0")]
    [InlineData(1.5, "1.5")]
    [InlineData(-12345.678, "-12345.678")]
    [InlineData(1e-5, "1E-05")]
    [InlineData(1e21, "1E+21")]
    public void NumberSpellingIsInvariantAndShortest(double value, string expected)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            DrawingMlChartCache.FormatNumber(value).Should().Be(expected);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData(0.1 + 0.2)]
    [InlineData(1.0 / 3.0)]
    [InlineData(45000.708333333336)]
    [InlineData(123456789012345.67)]
    [InlineData(double.Epsilon)]
    [InlineData(double.MaxValue)]
    public void NumberSpellingRoundTripsEveryDouble(double value)
    {
        // Excel and PowerPoint write full-precision numbers into c:numCache. The previous FreeX "G15"
        // spelling turned 0.1 + 0.2 into "0.3" and lost the low digits of a time-of-day serial date.
        var text = DrawingMlChartCache.FormatNumber(value);

        double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture).Should().Be(value);
    }
}
