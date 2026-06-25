using FreeX.Core.Model;

namespace FreeX.Core.IO;

/// <summary>
/// Utility to check whether a workbook contains any cell gradient fills.
/// The actual save-time preservation of gradient fill entries in styles.xml is handled
/// by <see cref="XlsxStylesheetMetadataPreserver"/> (source→target fills section copy)
/// for loaded workbooks. This class is kept for feature-plan gating.
/// </summary>
internal static class XlsxCellGradientFillWriter
{
    public static bool HasGradientFills(Workbook workbook)
    {
        for (int i = 0; i < workbook.StyleCount; i++)
        {
            if (workbook.GetStyle(new StyleId(i)).GradientFill is not null)
                return true;
        }
        return false;
    }
}
