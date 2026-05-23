using FluentAssertions;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.Core.Model.Tests;

public sealed class FilterProtectionCommandTests
{
    [Fact]
    public void FilterCommand_RejectsProtectedSheetWithoutUseAutoFilterPermission()
    {
        var (_, sheet, ctx, range) = SetupFilterRange();
        sheet.IsProtected = true;

        var outcome = new FilterCommand(sheet.Id, range, 0, ["West"]).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        sheet.FilterHiddenRows.Should().BeEmpty();
    }

    [Fact]
    public void FilterCommand_AllowsProtectedSheetWithUseAutoFilterPermission()
    {
        var (_, sheet, ctx, range) = SetupFilterRange();
        sheet.IsProtected = true;
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.UseAutoFilter);

        var outcome = new FilterCommand(sheet.Id, range, 0, ["West"]).Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.FilterHiddenRows.Should().Contain(3);
        sheet.FilterHiddenRows.Should().NotContain(2);
    }

    [Fact]
    public void FilterConditionCommand_AllowsProtectedSheetWithUseAutoFilterPermission()
    {
        var (_, sheet, ctx, range) = SetupFilterRange();
        sheet.IsProtected = true;
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.UseAutoFilter);

        var outcome = new FilterConditionCommand(sheet.Id, range, 1, new NumberGreaterThanFilterCriterion(7)).Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.FilterHiddenRows.Should().Contain(2);
        sheet.FilterHiddenRows.Should().NotContain(3);
    }

    [Fact]
    public void ColorFilterCommands_AllowProtectedSheetWithUseAutoFilterPermission()
    {
        var (workbook, sheet, ctx, range) = SetupFilterRange();
        var red = new CellColor(255, 0, 0);
        var blue = new CellColor(0, 0, 255);
        var redFill = workbook.RegisterStyle(new CellStyle { FillColor = red });
        var blueFont = workbook.RegisterStyle(new CellStyle { FontColor = blue });
        sheet.GetCell(2, 1)!.StyleId = redFill;
        sheet.GetCell(3, 1)!.StyleId = blueFont;
        sheet.IsProtected = true;
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.UseAutoFilter);

        new CellFillColorFilterCommand(sheet.Id, range, 0, red).Apply(ctx).Success.Should().BeTrue();
        sheet.FilterHiddenRows.Should().Contain(3);

        new CellNoFillColorFilterCommand(sheet.Id, range, 0).Apply(ctx).Success.Should().BeTrue();
        sheet.FilterHiddenRows.Should().Contain(2);

        new CellFontColorFilterCommand(sheet.Id, range, 0, blue).Apply(ctx).Success.Should().BeTrue();
        sheet.FilterHiddenRows.Should().Contain(2);
        sheet.FilterHiddenRows.Should().NotContain(3);
    }

    [Fact]
    public void SummaryFilterCommands_AllowProtectedSheetWithUseAutoFilterPermission()
    {
        var (_, sheet, ctx, range) = SetupFilterRange();
        sheet.IsProtected = true;
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.UseAutoFilter);

        new AverageFilterCommand(sheet.Id, range, 1, above: true).Apply(ctx).Success.Should().BeTrue();
        sheet.FilterHiddenRows.Should().Contain(2);

        new TopBottomFilterCommand(sheet.Id, range, 1, count: 1, top: true).Apply(ctx).Success.Should().BeTrue();
        sheet.FilterHiddenRows.Should().Contain(2);
        sheet.FilterHiddenRows.Should().NotContain(3);
    }

    [Fact]
    public void ApplyStructuredTableFiltersCommand_AllowsProtectedSheetWithUseAutoFilterPermission()
    {
        var (_, sheet, ctx, range) = SetupFilterRange();
        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = range,
            HasAutoFilter = true
        };
        table.Columns.Add(new StructuredTableColumnModel(1, "Region"));
        table.Columns.Add(new StructuredTableColumnModel(2, "Amount"));
        table.FilterColumns.Add(new StructuredTableFilterColumnModel(0, ["West"]));
        sheet.StructuredTables.Add(table);
        sheet.IsProtected = true;
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.UseAutoFilter);

        var outcome = new ApplyStructuredTableFiltersCommand(sheet.Id, table.Id).Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.FilterHiddenRows.Should().Contain(3);
        sheet.FilterHiddenRows.Should().NotContain(2);
    }

    private static (Workbook Workbook, Sheet Sheet, ICommandContext Context, GridRange Range) SetupFilterRange()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(5));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(10));
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2));
        return (workbook, sheet, new SimpleCtx(workbook), range);
    }

    private sealed class SimpleCtx(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;
        public Sheet GetSheet(SheetId sheetId) => Workbook.GetSheet(sheetId)!;
    }
}
