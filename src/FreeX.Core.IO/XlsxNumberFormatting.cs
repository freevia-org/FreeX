using System.Globalization;

namespace FreeX.Core.IO;

/// <summary>
/// Shared helpers for serialising numeric values in XLSX XML.
/// </summary>
internal static class XlsxNumberFormatting
{
    /// <summary>
    /// Formats a <see cref="double"/> for use as an XML attribute value.
    /// Uses the "G17" round-trip format with <see cref="CultureInfo.InvariantCulture"/>,
    /// matching Excel's representation exactly.
    /// </summary>
    public static string ToXmlString(double value) =>
        value.ToString("G17", CultureInfo.InvariantCulture);
}
