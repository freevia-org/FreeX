using System.Globalization;
using System.Xml.Linq;

namespace Free.Shared.Drawing;

/// <summary>
/// Builders for the cached data blocks of a DrawingML chart: <c>c:numCache</c> / <c>c:numLit</c>
/// (CT_NumData) and <c>c:strCache</c> / <c>c:strLit</c> (CT_StrData). FreeX (xlsx), FreeW (docx) and FreeP
/// (pptx) each carried a private copy of the same "ptCount, then one <c>c:pt idx</c> per present point"
/// loop. What is cached (which points exist, which text a model value becomes, the format code) stays each
/// app's decision and arrives as the already-formatted <c>(index, text)</c> pairs; this class owns only the
/// element shape and the number spelling.
///
/// <para>Numbers are written with <see cref="FormatNumber"/>: culture-invariant and the shortest string
/// that round-trips the double (what Excel and PowerPoint write into a <c>c:numCache</c>, e.g.
/// <c>0.30000000000000004</c>). FreeX used to cut caches at 15 significant digits (<c>"G15"</c>), which
/// silently changed any cached value that needs 16 or 17.</para>
/// </summary>
public static class DrawingMlChartCache
{
    /// <summary>Culture-invariant, round-trippable (shortest exact) spelling of a double for a chart cache.</summary>
    public static string FormatNumber(double value) =>
        // Plain double.ToString is the shortest round-trippable form on .NET Core 3.0+ ("G" and "R" are
        // not used so the spelling cannot drift if those specifiers' definitions are ever tightened).
        value.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// A <c>c:numCache</c> or <c>c:numLit</c> (<paramref name="elementName"/>): <c>c:formatCode</c>,
    /// <c>c:ptCount</c>, then one <c>c:pt idx="i"/c:v</c> per supplied point.
    /// </summary>
    public static XElement CreateNumericData(
        XNamespace chartNamespace,
        string elementName,
        string formatCode,
        int pointCount,
        IEnumerable<KeyValuePair<int, string>> points)
    {
        ArgumentNullException.ThrowIfNull(points);

        var data = new XElement(chartNamespace + elementName,
            new XElement(chartNamespace + "formatCode", formatCode),
            new XElement(chartNamespace + "ptCount", new XAttribute("val", pointCount)));
        AddPoints(data, chartNamespace, points);
        return data;
    }

    /// <summary>
    /// A <c>c:strCache</c> or <c>c:strLit</c> (<paramref name="elementName"/>): <c>c:ptCount</c>, then one
    /// <c>c:pt idx="i"/c:v</c> per supplied point.
    /// </summary>
    public static XElement CreateStringData(
        XNamespace chartNamespace,
        string elementName,
        int pointCount,
        IEnumerable<KeyValuePair<int, string>> points)
    {
        ArgumentNullException.ThrowIfNull(points);

        var data = new XElement(chartNamespace + elementName,
            new XElement(chartNamespace + "ptCount", new XAttribute("val", pointCount)));
        AddPoints(data, chartNamespace, points);
        return data;
    }

    private static void AddPoints(XElement data, XNamespace chartNamespace, IEnumerable<KeyValuePair<int, string>> points)
    {
        foreach (var point in points)
        {
            data.Add(new XElement(chartNamespace + "pt",
                new XAttribute("idx", point.Key),
                new XElement(chartNamespace + "v", point.Value)));
        }
    }
}
