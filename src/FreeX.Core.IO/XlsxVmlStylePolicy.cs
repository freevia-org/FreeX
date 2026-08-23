using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxVmlStylePolicy
{
    public static bool SetVisibility(XElement shape, bool isVisible)
    {
        var newValue = isVisible ? "visible" : "hidden";
        var styleValue = shape.Attribute("style")?.Value ?? string.Empty;
        var properties = styleValue.Length == 0
            ? []
            : styleValue.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var found = false;
        var rebuilt = new List<string>(properties.Length + 1);
        foreach (var property in properties)
        {
            var colonIndex = property.IndexOf(':');
            if (colonIndex >= 0 &&
                string.Equals(property[..colonIndex].Trim(), "visibility", StringComparison.OrdinalIgnoreCase))
            {
                rebuilt.Add($"visibility:{newValue}");
                found = true;
            }
            else
            {
                rebuilt.Add(property);
            }
        }

        if (!found)
            rebuilt.Add($"visibility:{newValue}");

        var normalized = string.Join(";", rebuilt);
        if (string.Equals(styleValue, normalized, StringComparison.Ordinal))
            return false;

        shape.SetAttributeValue("style", normalized);
        return true;
    }
}
