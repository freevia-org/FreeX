using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

public static class FormatPainterCommandFactory
{
    public static IWorkbookCommand Create(
        Workbook workbook,
        Sheet sourceSheet,
        CellAddress sourceAddress,
        GridRange targetRange)
    {
        var styleId = sourceSheet.GetCell(sourceAddress)?.StyleId
            ?? sourceSheet.GetStyleOnly(sourceAddress.Row, sourceAddress.Col)
            ?? StyleId.Default;
        var sourceStyle = workbook.GetStyle(styleId);
        return new ApplyStyleCommand(targetRange.Start.Sheet, targetRange, StyleDiff.FromStyle(sourceStyle));
    }

    public static IWorkbookCommand Create(
        Workbook workbook,
        StyleId sourceStyleId,
        GridRange targetRange)
    {
        var sourceStyle = workbook.GetStyle(sourceStyleId);
        return new ApplyStyleCommand(targetRange.Start.Sheet, targetRange, StyleDiff.FromStyle(sourceStyle));
    }
}
