using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// r239: GroupedApplyStyleCommand, the third and last of the trio r236 named. Two undo snapshots
/// rather than five -- cells with their style-only entry and its provenance tag, and the rich-text
/// runs the command rewrites when the style diff touches run fonts -- and both are consulted.
/// <para>
/// Applying a style a range already carries is the ordinary gesture: the gallery highlights the
/// current style, and pressing Bold on already-bold text is the same move.
/// </para>
/// </summary>
public sealed class R239_GroupedApplyStyleNoOpTests
{
    private static (Workbook Workbook, Sheet A, Sheet B, TestCommandContext Ctx) Fixture()
    {
        var workbook = new Workbook("test");
        var a = workbook.AddSheet("A");
        var b = workbook.AddSheet("B");
        return (workbook, a, b, new TestCommandContext(workbook));
    }

    private static GridRange Range(Sheet sheet) =>
        new(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1));

    [Fact]
    public void ApplyingBoldTwice_ReportsNoOpTheSecondTime()
    {
        var (_, a, b, ctx) = Fixture();
        a.SetCell(new CellAddress(a.Id, 1, 1), new TextValue("x"));
        b.SetCell(new CellAddress(b.Id, 1, 1), new TextValue("y"));

        new GroupedApplyStyleCommand([a.Id, b.Id], Range(a), new StyleDiff(Bold: true)).Apply(ctx)
            .IsNoOp.Should().BeFalse("the first application is a real edit");

        new GroupedApplyStyleCommand([a.Id, b.Id], Range(a), new StyleDiff(Bold: true)).Apply(ctx)
            .IsNoOp.Should().BeTrue("both sheets already carry the style");
    }

    [Fact]
    public void ApplyingADifferentStyle_DoesNotReportNoOp()
    {
        var (_, a, b, ctx) = Fixture();
        a.SetCell(new CellAddress(a.Id, 1, 1), new TextValue("x"));

        new GroupedApplyStyleCommand([a.Id, b.Id], Range(a), new StyleDiff(Bold: true)).Apply(ctx);

        new GroupedApplyStyleCommand([a.Id, b.Id], Range(a), new StyleDiff(Italic: true)).Apply(ctx)
            .IsNoOp.Should().BeFalse();
    }

    [Fact]
    public void AStyleAlreadyOnOneSheetButNotTheOther_IsARealEdit()
    {
        // The grouped part of the command matters here: sheet A already carries the style and sheet
        // B does not, so the batch is a real edit even though half of it changes nothing. Same
        // TrueForAll-not-Any argument as r234's batch case, across sheets instead of cells.
        var (_, a, b, ctx) = Fixture();
        a.SetCell(new CellAddress(a.Id, 1, 1), new TextValue("x"));
        b.SetCell(new CellAddress(b.Id, 1, 1), new TextValue("y"));

        new GroupedApplyStyleCommand([a.Id], Range(a), new StyleDiff(Bold: true)).Apply(ctx);

        new GroupedApplyStyleCommand([a.Id, b.Id], Range(a), new StyleDiff(Bold: true)).Apply(ctx)
            .IsNoOp.Should().BeFalse("sheet B has not been styled yet");
    }
}
