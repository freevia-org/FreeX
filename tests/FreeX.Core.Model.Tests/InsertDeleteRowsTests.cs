using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public partial class InsertDeleteRowsTests
{
    private static (Workbook wb, Sheet sheet, ICommandContext ctx) Setup()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        return (wb, sheet, new TestCommandContext(wb));
    }

    private const int DenseShiftRows = 500;
    private const int DenseShiftColumns = 80;
    private const uint DenseShiftBeforeRow = 2;
    private const int DenseMetadataRows = 6_000;
    private const uint DenseMetadataStartRow = 2;

    private static (Workbook Workbook, Sheet Sheet, ICommandContext Context) SetupDenseShiftWorkbook()
    {
        var workbook = new Workbook("dense row shift perf");
        var sheet = workbook.AddSheet("Sheet1");

        for (uint row = 1; row <= DenseShiftRows; row++)
        {
            for (uint col = 1; col <= DenseShiftColumns; col++)
                sheet.SetCell(new CellAddress(sheet.Id, row, col), new NumberValue(row * 1000 + col));
        }

        return (workbook, sheet, new TestCommandContext(workbook));
    }

    private static (Workbook Workbook, Sheet Sheet, ICommandContext Context) SetupDenseRowMetadataWorkbook()
    {
        var workbook = new Workbook("dense row metadata shift perf");
        var sheet = workbook.AddSheet("Sheet1");

        for (uint row = 1; row <= DenseMetadataRows; row++)
        {
            sheet.RowHeights[row] = 18 + row % 7;
            sheet.HiddenRows.Add(row);
            sheet.FilterHiddenRows.Add(row);
            sheet.RowPageBreaks.Add(row);

            sheet.Comments[new CellAddress(sheet.Id, row, 1)] = $"comment {row}";
            sheet.ThreadedComments[new CellAddress(sheet.Id, row, 2)] = new ThreadedComment($"thread {row}", "FreeX");
            var hyperlinkAddress = new CellAddress(sheet.Id, row, 3);
            sheet.Hyperlinks[hyperlinkAddress] = $"https://example.com/{row}";
            sheet.HyperlinkMetadata[hyperlinkAddress] = new HyperlinkMetadata(ScreenTip: $"Open row {row}");
        }

        return (workbook, sheet, new TestCommandContext(workbook));
    }

    private static string FindWorkspaceFile(params string[] parts)
        => WorkspaceFileLocator.Find(parts);

}
