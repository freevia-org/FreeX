using System.Globalization;
using System.Xml.Linq;

namespace Freexcel.Core.IO;

internal static partial class XlsxPivotTableWriter
{
    private static string FormatInvariant(double value) =>
        value.ToString("0.########", CultureInfo.InvariantCulture);

    private static XAttribute? OptionalAttribute(string name, string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : new XAttribute(name, value.Trim());

    private static XAttribute? ToOptionalIntAttribute(string name, int? value) =>
        value is { } intValue ? new XAttribute(name, intValue.ToString(CultureInfo.InvariantCulture)) : null;

    private static XAttribute? ToOptionalBoolAttribute(string name, bool? value) =>
        value is { } boolValue ? new XAttribute(name, boolValue ? "1" : "0") : null;
}
