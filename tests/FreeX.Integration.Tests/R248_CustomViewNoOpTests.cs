using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// r248: ApplyCustomViewCommand -- applying the custom view the workbook is already showing.
/// <para>
/// The comparison goes through WorksheetCustomViewStateComparer rather than the record's own
/// <c>==</c>, because that record carries list members and records compare those by REFERENCE while
/// every capture builds fresh lists. That is the FIFTH instance of this trap in the program, which
/// is why this one got a coverage contract rather than another hand-written comparison.
/// </para>
/// </summary>
public sealed class R248_CustomViewNoOpTests
{
    private static (Workbook Workbook, Sheet Sheet, TestCommandContext Ctx) Fixture()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        return (workbook, sheet, new TestCommandContext(workbook));
    }

    [Fact]
    public void ApplyingTheViewTheWorkbookIsAlreadyShowing_ReportsNoOp()
    {
        var (_, sheet, ctx) = Fixture();
        new SaveCustomViewCommand("Plain").Apply(ctx).Success.Should().BeTrue();

        new ApplyCustomViewCommand("Plain").Apply(ctx)
            .IsNoOp.Should().BeTrue("nothing has changed since the view was saved");
    }

    [Fact]
    public void ApplyingAViewAfterTheStateMoved_DoesNotReportNoOp()
    {
        var (_, sheet, ctx) = Fixture();
        new SaveCustomViewCommand("Plain").Apply(ctx);
        sheet.ZoomPercent = 200;

        var outcome = new ApplyCustomViewCommand("Plain").Apply(ctx);

        outcome.IsNoOp.Should().BeFalse();
        sheet.ZoomPercent.Should().Be(100);
    }

    [Fact]
    public void ApplyingTwice_ReportsNoOpTheSecondTime()
    {
        var (_, sheet, ctx) = Fixture();
        new SaveCustomViewCommand("Plain").Apply(ctx);
        sheet.ZoomPercent = 200;

        new ApplyCustomViewCommand("Plain").Apply(ctx).IsNoOp.Should().BeFalse();
        new ApplyCustomViewCommand("Plain").Apply(ctx).IsNoOp.Should().BeTrue();
    }
}
