using System.Globalization;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static partial class XlsxPivotTableWriter
{
    private static string FormatInvariant(double value) =>
        value.ToString("0.########", CultureInfo.InvariantCulture);

    private static XAttribute? OptionalAttribute(string name, string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : new XAttribute(name, value.Trim());

    private static XAttribute? ToOptionalIntAttribute(string name, int? value) =>
        OptionalFormattedAttribute(
            name,
            value is { } intValue ? intValue.ToString(CultureInfo.InvariantCulture) : null);

    private static XAttribute? ToOptionalBoolAttribute(string name, bool? value) =>
        OptionalFormattedAttribute(name, value is { } boolValue ? boolValue ? "1" : "0" : null);

    private static XAttribute? OptionalFormattedAttribute(string name, string? value) =>
        value is null ? null : new XAttribute(name, value);
}
