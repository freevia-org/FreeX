using System.Globalization;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetPageBreaksMetadataReader
{
    public static WorksheetPageBreaksMetadataModel? Read(XElement? pageBreaks, uint maxBreakId)
    {
        if (pageBreaks is null)
            return null;

        var model = new WorksheetPageBreaksMetadataModel();
        XlsxWorksheetNativeMetadataHelpers.ReadNativeAttributes(pageBreaks, model.NativeAttributes, ["count"]);

        foreach (var breakElement in pageBreaks.Elements())
        {
            if (!string.Equals(breakElement.Name.LocalName, "brk", StringComparison.Ordinal))
                continue;

            if (!TryReadPageBreakId(breakElement, maxBreakId, out var id))
                continue;

            var attributes = new Dictionary<string, string>(StringComparer.Ordinal);
            XlsxWorksheetNativeMetadataHelpers.ReadNativeAttributes(breakElement, attributes, ["id"]);

            if (attributes.Count > 0)
                model.BreakNativeAttributes[id] = attributes;
        }

        return model.NativeAttributes.Count == 0 && model.BreakNativeAttributes.Count == 0
            ? null
            : model;
    }

    private static bool TryReadPageBreakId(XElement breakElement, uint maxBreakId, out uint id)
    {
        id = 0;
        return uint.TryParse(breakElement.Attribute("id")?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out id) &&
            id >= 2 &&
            id <= maxBreakId;
    }
}
