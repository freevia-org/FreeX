using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public partial class InsertDeleteColumnsTests
{
    private static (Workbook wb, Sheet sheet, ICommandContext ctx) Setup()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        return (wb, sheet, new SimpleCtx(wb));
    }

    private const int DenseShiftRows = 500;
    private const int DenseShiftColumns = 80;
    private const uint DenseShiftBeforeColumn = 2;
    private const int DenseMetadataColumns = 6_000;
    private const uint DenseMetadataStartColumn = 2;

    private static (Workbook Workbook, Sheet Sheet, ICommandContext Context) SetupDenseShiftWorkbook()
    {
        var workbook = new Workbook("dense column shift perf");
        var sheet = workbook.AddSheet("Sheet1");

        for (uint row = 1; row <= DenseShiftRows; row++)
        {
            for (uint col = 1; col <= DenseShiftColumns; col++)
                sheet.SetCell(new CellAddress(sheet.Id, row, col), new NumberValue(row * 1000 + col));
        }

        return (workbook, sheet, new SimpleCtx(workbook));
    }

    private static (Workbook Workbook, Sheet Sheet, ICommandContext Context) SetupDenseColumnMetadataWorkbook()
    {
        var workbook = new Workbook("dense column metadata shift perf");
        var sheet = workbook.AddSheet("Sheet1");

        for (uint col = 1; col <= DenseMetadataColumns; col++)
        {
            sheet.ColumnWidths[col] = 9 + col % 11;
            sheet.HiddenCols.Add(col);
            sheet.ColumnPageBreaks.Add(col);

            sheet.Comments[new CellAddress(sheet.Id, 1, col)] = $"comment {col}";
            sheet.ThreadedComments[new CellAddress(sheet.Id, 2, col)] = new ThreadedComment($"thread {col}", "FreeX");
            var hyperlinkAddress = new CellAddress(sheet.Id, 3, col);
            sheet.Hyperlinks[hyperlinkAddress] = $"https://example.com/{col}";
            sheet.HyperlinkMetadata[hyperlinkAddress] = new HyperlinkMetadata(ScreenTip: $"Open column {col}");
        }

        return (workbook, sheet, new SimpleCtx(workbook));
    }

    private static string FindWorkspaceFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate))
                return candidate;

            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not find workspace file {Path.Combine(parts)}");
    }

    private sealed class SimpleCtx(Workbook wb) : ICommandContext
    {
        public Workbook Workbook { get; } = wb;
        public Sheet GetSheet(SheetId id) => Workbook.GetSheet(id)!;
    }
}
