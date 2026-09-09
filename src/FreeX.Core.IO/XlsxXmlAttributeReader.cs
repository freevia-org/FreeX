using System.Globalization;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxXmlAttributeReader
{
    public static int? ReadIntAttribute(XElement element, string name) =>
        int.TryParse(element.Attribute(name)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    /// <summary>
    /// r578: IsFinite. double.TryParse has not thrown on magnitude overflow since .NET Core -- it
    /// returns true with +/-Infinity -- and NumberStyles.Float also accepts the literal
    /// "NaN"/"Infinity" spellings. All 33 call sites read a BOUNDED quantity out of an xlsx part
    /// (page margins, pivot group start/end/interval, filter comparison values, top-10 counts,
    /// calcPr/@iterateDelta), so not one of them has a use for a non-finite number; null is this
    /// method's existing "the attribute is absent or unreadable" result and every caller already
    /// handles it. Guarding the one door rather than 33 call sites mirrors r575 (XlsxChartScalar
    /// Reader.ReadOptionalDouble, 68 sites) and r571 (DialogNumericTextPolicy, 55 sites).
    /// <para>
    /// Two of the sites are twins of defects fixed in r577 on their other side: @iterateDelta is
    /// the FILE side of the convergence threshold CalculationOptionsInputParser guards when it is
    /// typed, and the pivot group bounds feed PivotTableRefreshService's numeric-range bucket
    /// maths, whose own reject form is fixed alongside this.
    /// </para>
    /// </summary>
    public static double? ReadDoubleAttribute(XElement element, string name) =>
        double.TryParse(element.Attribute(name)?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
        && double.IsFinite(value)
            ? value
            : null;

    public static bool ReadBoolAttribute(XElement? element, string name, bool defaultValue = false)
    {
        var value = element?.Attribute(name)?.Value;
        if (value is null)
            return defaultValue;

        return XlsxWorksheetXmlValueParser.IsTruthy(value);
    }

    public static bool? ReadNullableBoolAttribute(XElement? element, string name)
    {
        var value = element?.Attribute(name)?.Value;
        if (value is null)
            return null;

        return XlsxWorksheetXmlValueParser.IsTruthy(value);
    }
}
