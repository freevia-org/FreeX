using FreeX.Core.Model;
using FreeX.Core.Commands;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

public partial class SortFilterTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static (Workbook wb, Sheet sheet, ICommandContext ctx) MakeContext() =>
        TestWorkbookFixture.CreateContext("Test");

    // ── Sort tests ────────────────────────────────────────────────────────────
}
