using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Calc.Tests;

internal static class TestWorkbookFixture
{
    public static (Workbook workbook, Sheet sheet) CreateWorkbook(string name = "test")
    {
        var workbook = new Workbook(name);
        var sheet = workbook.AddSheet("Sheet1");
        return (workbook, sheet);
    }

    public static (Workbook workbook, Sheet sheet, ICommandContext context) CreateContext(string name = "test")
    {
        var (workbook, sheet) = CreateWorkbook(name);
        return (workbook, sheet, new TestCommandContext(workbook));
    }
}
