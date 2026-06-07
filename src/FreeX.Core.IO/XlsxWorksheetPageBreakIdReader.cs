using System.Globalization;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetPageBreakIdReader
{
    public static bool TryReadSupportedId(XElement breakElement, uint maxBreakId, out uint id)
    {
        id = 0;
        return uint.TryParse(
                breakElement.Attribute("id")?.Value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out id) &&
            IsSupported(id, maxBreakId);
    }

    public static bool IsSupported(uint id, uint maxBreakId) =>
        id >= 2 && id <= maxBreakId;
}
