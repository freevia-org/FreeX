using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public partial class CommentCommandTests
{
    private static readonly DateTimeOffset CreatedAtUtc = new(2026, 5, 31, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ModifiedAtUtc = new(2026, 5, 31, 9, 30, 0, TimeSpan.Zero);

    private static (Workbook wb, Sheet sheet, ICommandContext ctx) Setup()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        return (wb, sheet, new TestCommandContext(wb));
    }
}
