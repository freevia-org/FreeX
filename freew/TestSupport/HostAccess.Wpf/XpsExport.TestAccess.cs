using System.IO;
using System.Windows.Documents;

namespace FreeW.App.Host;

internal static partial class XpsExport
{
    internal static byte[] RenderToBytesWithSimulatedFontSubsetterFailureForTests(DocumentPaginator paginator)
    {
        return RenderToBytesCore(
            paginator,
            _ => throw new FileFormatException("Simulated WPF font subsetter failure."));
    }
}
