using Freexcel.Core.Commands;
using Freexcel.Core.Model;
using FluentAssertions;

namespace Freexcel.Core.Model.Tests;

public class ApplyStyleCommandTests
{
    private static (Workbook wb, Sheet sheet, ICommandContext ctx) Setup()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        return (wb, sheet, ctx);
    }

    [Fact]
    public void ApplyBold_SetsBoldOnTargetCell()
    {
        var (wb, sheet, ctx) = Setup();
        var addr = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(addr, new NumberValue(1));

        var range = new GridRange(addr, addr);
        var cmd = new ApplyStyleCommand(sheet.Id, range, new StyleDiff(Bold: true));
        cmd.Apply(ctx);

        var style = wb.GetStyle(sheet.GetCell(addr)!.StyleId);
        style.Bold.Should().BeTrue();
    }

    [Fact]
    public void ApplyBold_DoesNotChangeFontColor()
    {
        var (wb, sheet, ctx) = Setup();
        var addr = new CellAddress(sheet.Id, 1, 1);
        var baseStyle = new CellStyle { FontColor = new CellColor(255, 0, 0) };
        var cell = Cell.FromValue(new NumberValue(1));
        cell.StyleId = wb.RegisterStyle(baseStyle);
        sheet.SetCell(addr, cell);

        var range = new GridRange(addr, addr);
        var cmd = new ApplyStyleCommand(sheet.Id, range, new StyleDiff(Bold: true));
        cmd.Apply(ctx);

        var style = wb.GetStyle(sheet.GetCell(addr)!.StyleId);
        style.Bold.Should().BeTrue();
        style.FontColor.Should().Be(new CellColor(255, 0, 0));
    }

    [Fact]
    public void ApplyToRange_AllCellsUpdated()
    {
        var (wb, sheet, ctx) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b2 = new CellAddress(sheet.Id, 2, 2);
        sheet.SetCell(a1, new NumberValue(1));
        sheet.SetCell(b2, new NumberValue(2));

        var range = new GridRange(a1, b2);
        var cmd = new ApplyStyleCommand(sheet.Id, range, new StyleDiff(Italic: true));
        cmd.Apply(ctx);

        wb.GetStyle(sheet.GetCell(a1)!.StyleId).Italic.Should().BeTrue();
        wb.GetStyle(sheet.GetCell(b2)!.StyleId).Italic.Should().BeTrue();
    }

    [Fact]
    public void Revert_RestoresOriginalStyles()
    {
        var (wb, sheet, ctx) = Setup();
        var addr = new CellAddress(sheet.Id, 1, 1);
        var original = new CellStyle { Bold = true };
        var cell = Cell.FromValue(new NumberValue(1));
        cell.StyleId = wb.RegisterStyle(original);
        sheet.SetCell(addr, cell);
        var originalStyleId = cell.StyleId;

        var range = new GridRange(addr, addr);
        var cmd = new ApplyStyleCommand(sheet.Id, range, new StyleDiff(Italic: true));
        cmd.Apply(ctx);
        cmd.Revert(ctx);

        sheet.GetCell(addr)!.StyleId.Should().Be(originalStyleId);
    }

    [Fact]
    public void Apply_CreatesNewCellIfMissing()
    {
        var (wb, sheet, ctx) = Setup();
        var addr = new CellAddress(sheet.Id, 5, 5);

        var range = new GridRange(addr, addr);
        var cmd = new ApplyStyleCommand(sheet.Id, range, new StyleDiff(Bold: true));
        cmd.Apply(ctx);

        var cell = sheet.GetCell(addr);
        cell.Should().NotBeNull();
        wb.GetStyle(cell!.StyleId).Bold.Should().BeTrue();
    }

    private sealed class SimpleCtx(Workbook wb) : ICommandContext
    {
        public Workbook Workbook { get; } = wb;
        public Sheet GetSheet(SheetId id) => Workbook.GetSheet(id)!;
    }
}
