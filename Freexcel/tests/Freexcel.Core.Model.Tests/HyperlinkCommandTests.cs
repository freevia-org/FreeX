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

        var metadata = new HyperlinkMetadata(
            HyperlinkTargetKind.PlaceInThisDocument,
            "Open the budget tab",
            "Budget!A1");
        var command = new SetHyperlinkCommand(sheet.Id, addr, "https://example.com", "Example", metadata);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(addr).Should().Be(new TextValue("Example"));
        sheet.Hyperlinks[addr].Should().Be("https://example.com");
        sheet.HyperlinkMetadata[addr].Should().Be(metadata);

        command.Revert(ctx);

        sheet.GetValue(addr).Should().Be(new TextValue("old"));
        sheet.Hyperlinks.Should().NotContainKey(addr);
        sheet.HyperlinkMetadata.Should().NotContainKey(addr);
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
        sheet.HyperlinkMetadata[addr] = new HyperlinkMetadata(
            HyperlinkTargetKind.EmailAddress,
            "Email support",
            "support@example.com");

        var command = new ClearHyperlinksCommand(
            sheet.Id,
            new GridRange(addr, addr));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(addr).Should().Be(new TextValue("Example"));
        sheet.Hyperlinks.Should().NotContainKey(addr);
        sheet.HyperlinkMetadata.Should().NotContainKey(addr);

        command.Revert(ctx);

        sheet.Hyperlinks[addr].Should().Be("https://example.com");
        sheet.HyperlinkMetadata[addr].Should().Be(new HyperlinkMetadata(
            HyperlinkTargetKind.EmailAddress,
            "Email support",
            "support@example.com"));
    }

    [Fact]
    public void ClearHyperlinksCommand_RejectsLockedHyperlinkCellsOnProtectedSheet()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var addr = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(addr, new TextValue("Example"));
        sheet.Hyperlinks[addr] = "https://example.com";
        sheet.IsProtected = true;

        var outcome = new ClearHyperlinksCommand(sheet.Id, new GridRange(addr, addr)).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        sheet.Hyperlinks[addr].Should().Be("https://example.com");
    }

    [Fact]
    public void ClearHyperlinksCommand_AllowsUnlockedHyperlinkCellsOnProtectedSheet()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var addr = new CellAddress(sheet.Id, 1, 1);
        var unlockedStyle = wb.RegisterStyle(new CellStyle { Locked = false });
        var cell = Cell.FromValue(new TextValue("Example"));
        cell.StyleId = unlockedStyle;
        sheet.SetCell(addr, cell);
        sheet.Hyperlinks[addr] = "https://example.com";
        sheet.IsProtected = true;

        var outcome = new ClearHyperlinksCommand(sheet.Id, new GridRange(addr, addr)).Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.Hyperlinks.Should().NotContainKey(addr);
        sheet.GetCell(addr)!.StyleId.Should().Be(unlockedStyle);
    }

    [Fact]
    public void SetHyperlinkCommand_RejectsProtectedSheetWithoutInsertHyperlinksPermission()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var addr = new CellAddress(sheet.Id, 1, 1);
        sheet.IsProtected = true;

        var outcome = new SetHyperlinkCommand(sheet.Id, addr, "https://example.com", "Example").Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        sheet.Hyperlinks.Should().NotContainKey(addr);
    }

    [Fact]
    public void SetHyperlinkCommand_AllowsProtectedSheetWithInsertHyperlinksPermission()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var addr = new CellAddress(sheet.Id, 1, 1);
        sheet.IsProtected = true;
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.InsertHyperlinks);

        var outcome = new SetHyperlinkCommand(sheet.Id, addr, "https://example.com", "Example").Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.GetValue(addr).Should().Be(new TextValue("Example"));
        sheet.Hyperlinks[addr].Should().Be("https://example.com");
    }

    private sealed class SimpleCtx(Workbook wb) : ICommandContext
    {
        public Workbook Workbook { get; } = wb;
        public Sheet GetSheet(SheetId id) => Workbook.GetSheet(id)!;
    }
}
