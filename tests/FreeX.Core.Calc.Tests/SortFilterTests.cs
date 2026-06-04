using FreeX.Core.Model;
using FreeX.Core.Commands;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

public partial class SortFilterTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static (Workbook wb, Sheet sheet, ICommandContext ctx) MakeContext()
    {
        var wb = new Workbook("Test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        return (wb, sheet, ctx);
    }

    // ── Sort tests ────────────────────────────────────────────────────────────
}
