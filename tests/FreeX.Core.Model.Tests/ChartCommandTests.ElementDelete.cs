using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class ChartCommandTests
{
    [Fact]
    public void SetChartLayoutCommand_ClearsSecondaryAxisTitle_AndUndoRestoresIt()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2));
        new AddChartCommand(sheet.Id, range, ChartType.Column, "Sales").Apply(ctx);
        sheet.Charts[0].SecondaryAxisTitle = "Growth";

        var command = new SetChartLayoutCommand(
            sheet.Id, sheet.Charts[0].Id, new ChartLayoutOptions { SecondaryAxisTitle = string.Empty });

        command.Apply(ctx).Success.Should().BeTrue();
        sheet.Charts[0].SecondaryAxisTitle.Should().BeEmpty();

        command.Revert(ctx);
        sheet.Charts[0].SecondaryAxisTitle.Should().Be("Growth");
    }

    [Fact]
    public void SetChartLayoutCommand_AppliesLegendEntryDeletion_AndUndoRestoresEntries()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2));
        new AddChartCommand(sheet.Id, range, ChartType.Column, "Sales").Apply(ctx);

        var command = new SetChartLayoutCommand(
            sheet.Id,
            sheet.Charts[0].Id,
            new ChartLayoutOptions(LegendEntries: [new ChartLegendEntryModel(1, true)]));

        command.Apply(ctx).Success.Should().BeTrue();
        sheet.Charts[0].LegendEntries.Should().ContainSingle(e => e.Index == 1 && e.IsDeleted == true);

        command.Revert(ctx);
        sheet.Charts[0].LegendEntries.Should().BeEmpty();
    }
}
