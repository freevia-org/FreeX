using FreeX.Core.Calc;
using FreeX.Core.Model;

namespace FreeX.Core.Calc.Tests;

public partial class ConditionalFormatTests
{
    private static (Workbook workbook, Sheet sheet) MakeWorkbook()
    {
        var wb = new Workbook("test");
        var sh = wb.AddSheet("Sheet1");
        return (wb, sh);
    }

    private static ViewportModel GetViewport(Workbook wb, Sheet sheet)
    {
        var svc = new ViewportService();
        return svc.GetViewport(wb, sheet.Id, new ViewportRequest(1, 1, 500, 500));
    }

    private static DisplayCell GetCell(ViewportModel vp, uint row, uint col) =>
        vp.Cells.Single(c => c.Row == row && c.Col == col);

    private static string ReadViewportConditionalFormatEvaluatorSources()
    {
        var primaryFile = WorkspaceFileLocator.Find("src", "FreeX.Core.Calc", "ViewportConditionalFormatEvaluator.cs");
        var directory = Path.GetDirectoryName(primaryFile)!;
        var files = Directory.GetFiles(directory, "ViewportConditionalFormatEvaluator*.cs")
            .OrderBy(static file => Path.GetFileName(file), StringComparer.Ordinal);

        return string.Join(Environment.NewLine, files.Select(File.ReadAllText));
    }
}
