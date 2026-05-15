using FluentAssertions;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;
using Xunit;

namespace Freexcel.Core.Model.Tests;

public sealed class HyperlinkCommandTests
{
    [Fact]
    public void SetHyperlinkCommand_SetsDisplayTextAndHyperlinkAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var addr = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(addr, new TextValue("old"));

        var command = new SetHyperlinkCommand(sheet.Id, addr, "https://example.com", "Example");

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(addr).Should().Be(new TextValue("Example"));
        sheet.Hyperlinks[addr].Should().Be("https://example.com");

        command.Revert(ctx);

        sheet.GetValue(addr).Should().Be(new TextValue("old"));
        sheet.Hyperlinks.Should().NotContainKey(addr);
    }

    [Fact]
    public void ClearHyperlinksCommand_RemovesHyperlinksButKeepsDisplayText()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var addr = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(addr, new TextValue("Example"));
        sheet.Hyperlinks[addr] = "https://example.com";

        var command = new ClearHyperlinksCommand(
            sheet.Id,
            new GridRange(addr, addr));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(addr).Should().Be(new TextValue("Example"));
        sheet.Hyperlinks.Should().NotContainKey(addr);

        command.Revert(ctx);

        sheet.Hyperlinks[addr].Should().Be("https://example.com");
    }

    private sealed class SimpleCtx(Workbook wb) : ICommandContext
    {
        public Workbook Workbook { get; } = wb;
        public Sheet GetSheet(SheetId id) => Workbook.GetSheet(id)!;
    }
}
