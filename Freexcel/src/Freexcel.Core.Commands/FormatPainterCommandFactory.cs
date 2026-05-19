using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

public static class FormatPainterCommandFactory
{
    public static IWorkbookCommand Create(
        Workbook workbook,
        Sheet sourceSheet,
        CellAddress sourceAddress,
        GridRange targetRange)
        => Create(workbook, sourceSheet, new GridRange(sourceAddress, sourceAddress), targetRange);

    public static IWorkbookCommand Create(
        Workbook workbook,
        Sheet sourceSheet,
        GridRange sourceRange,
        GridRange targetRange)
    {
        if (sourceRange.RowCount == 1 && sourceRange.ColCount == 1)
        {
            var styleId = GetSourceStyleId(sourceSheet, sourceRange.Start);
            var sourceStyle = workbook.GetStyle(styleId);
            return new ApplyStyleCommand(targetRange.Start.Sheet, targetRange, StyleDiff.FromStyle(sourceStyle));
        }

        var commands = new List<IWorkbookCommand>();
        foreach (var targetAddress in targetRange.AllCells())
        {
            var sourceAddress = new CellAddress(
                sourceSheet.Id,
                sourceRange.Start.Row + ((targetAddress.Row - targetRange.Start.Row) % sourceRange.RowCount),
                sourceRange.Start.Col + ((targetAddress.Col - targetRange.Start.Col) % sourceRange.ColCount));
            var sourceStyle = workbook.GetStyle(GetSourceStyleId(sourceSheet, sourceAddress));
            commands.Add(new ApplyStyleCommand(
                targetRange.Start.Sheet,
                new GridRange(targetAddress, targetAddress),
                StyleDiff.FromStyle(sourceStyle)));
        }

        return new CompositeWorkbookCommand("Format Painter", commands);
    }

    public static IWorkbookCommand Create(
        Workbook workbook,
        StyleId sourceStyleId,
        GridRange targetRange)
    {
        var sourceStyle = workbook.GetStyle(sourceStyleId);
        return new ApplyStyleCommand(targetRange.Start.Sheet, targetRange, StyleDiff.FromStyle(sourceStyle));
    }

    private static StyleId GetSourceStyleId(Sheet sourceSheet, CellAddress sourceAddress) =>
        sourceSheet.GetCell(sourceAddress)?.StyleId
        ?? sourceSheet.GetStyleOnly(sourceAddress.Row, sourceAddress.Col)
        ?? StyleId.Default;
}
