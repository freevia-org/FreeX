using System.Globalization;
using System.Xml.Linq;

namespace Freexcel.Core.IO;

internal static class XlsxXmlAttributeReader
{
    public static int? ReadIntAttribute(XElement element, string name) =>
        int.TryParse(element.Attribute(name)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    public static double? ReadDoubleAttribute(XElement element, string name) =>
        double.TryParse(element.Attribute(name)?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    public static bool ReadBoolAttribute(XElement? element, string name, bool defaultValue = false)
    {
        var value = element?.Attribute(name)?.Value;
        if (value is null)
            return defaultValue;

        return value is "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }
}
