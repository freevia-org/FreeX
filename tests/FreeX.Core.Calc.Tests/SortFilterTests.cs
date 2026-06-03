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
        var ctx = new SimpleCommandContext(wb);
        return (wb, sheet, ctx);
    }

    // ICommandContext backed by a Workbook — same pattern used by CommandBus
    private sealed class SimpleCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook => workbook;
        public Sheet GetSheet(SheetId id) => workbook.GetSheet(id)!;
    }

    // ── Sort tests ────────────────────────────────────────────────────────────
}
