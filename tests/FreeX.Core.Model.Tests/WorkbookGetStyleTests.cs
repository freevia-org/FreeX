using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Tests that verify <see cref="Workbook.GetStyle"/> returns defensive clones of registered styles.
/// </summary>
public class WorkbookGetStyleTests
{
    [Fact]
    public void GetStyle_ReturnsDefensiveCloneOfRegisteredStyle()
    {
        var wb = new Workbook("test");
        var style = new CellStyle { Bold = true, FontSize = 14 };
        var id = wb.RegisterStyle(style);

        var retrieved = wb.GetStyle(id);
        retrieved.Bold = false;
        retrieved.FontSize = 9;

        var registered = wb.GetStyle(id);
        registered.Should().NotBeSameAs(retrieved);
        registered.Bold.Should().BeTrue();
        registered.FontSize.Should().Be(14);
    }

    [Fact]
    public void GetStyle_OutOfRangeId_ReturnsDefensiveCloneOfDefault()
    {
        var wb = new Workbook("test");

        var defaultStyle = wb.GetStyle(StyleId.Default);
        var outOfRange = wb.GetStyle(new StyleId(9999));

        outOfRange.Should().NotBeSameAs(defaultStyle);
        outOfRange.Should().Be(defaultStyle);
        outOfRange.Bold = true;
        wb.GetStyle(StyleId.Default).Bold.Should().BeFalse();
    }

    [Fact]
    public void ApplyStyleCommand_DoesNotMutateRegisteredStyle()
    {
        // Arrange: register a base style with red font color.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);

        var baseStyle = new CellStyle { FontColor = new CellColor(255, 0, 0) };
        var cell = Cell.FromValue(new NumberValue(42));
        cell.StyleId = wb.RegisterStyle(baseStyle);
        var addr = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(addr, cell);

        // Remember the registered style instance before the command runs.
        var registeredBefore = wb.GetStyle(cell.StyleId);

        // Act: apply bold via ApplyStyleCommand (which calls GetStyle then StyleDiff.ApplyTo).
        var cmd = new ApplyStyleCommand(sheet.Id, new GridRange(addr, addr), new StyleDiff(Bold: true));
        cmd.Apply(ctx);

        // The style instance that was registered earlier must be unchanged.
        registeredBefore.Bold.Should().BeFalse("ApplyStyleCommand must not mutate the registered base style");
        registeredBefore.FontColor.Should().Be(new CellColor(255, 0, 0));

        // The cell should now have a new style with bold set.
        var cellStyleId = sheet.GetCell(addr)!.StyleId;
        var newStyle = wb.GetStyle(cellStyleId);
        newStyle.Bold.Should().BeTrue();
        newStyle.FontColor.Should().Be(new CellColor(255, 0, 0), "font color must be preserved from the base style");
    }

    private sealed class SimpleCtx(Workbook wb) : ICommandContext
    {
        public Workbook Workbook { get; } = wb;
        public Sheet GetSheet(SheetId id) => Workbook.GetSheet(id)!;
    }
}
