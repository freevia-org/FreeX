using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Model.Tests;

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
    public void SetHyperlinkCommand_AppliesVisibleHyperlinkStyle()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var addr = new CellAddress(sheet.Id, 1, 1);

        new SetHyperlinkCommand(sheet.Id, addr, "https://example.com", "Example").Apply(ctx)
            .Success
            .Should()
            .BeTrue();

        var style = wb.GetStyle(sheet.GetCell(addr)!.StyleId);
        style.Underline.Should().BeTrue();
        style.FontColor.Should().Be(wb.Theme.ResolveColor(WorkbookThemeColorSlot.Hyperlink));
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
    public void RemoveHyperlinksCommand_RemovesHyperlinkAndResetsHyperlinkStyle()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var addr = new CellAddress(sheet.Id, 1, 1);
        var hyperlinkStyle = wb.RegisterStyle(new CellStyle
        {
            Bold = true,
            Underline = true,
            DoubleUnderline = true,
            FontColor = wb.Theme.ResolveColor(WorkbookThemeColorSlot.Hyperlink)
        });
        var cell = Cell.FromValue(new TextValue("Example"));
        cell.StyleId = hyperlinkStyle;
        sheet.SetCell(addr, cell);
        sheet.Hyperlinks[addr] = "https://example.com";
        sheet.HyperlinkMetadata[addr] = new HyperlinkMetadata(
            HyperlinkTargetKind.ExistingFileOrWebPage,
            "Example",
            "https://example.com");

        var command = new RemoveHyperlinksCommand(sheet.Id, new GridRange(addr, addr));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(addr).Should().Be(new TextValue("Example"));
        sheet.Hyperlinks.Should().NotContainKey(addr);
        sheet.HyperlinkMetadata.Should().NotContainKey(addr);
        var style = wb.GetStyle(sheet.GetCell(addr)!.StyleId);
        style.Bold.Should().BeTrue();
        style.Underline.Should().BeFalse();
        style.DoubleUnderline.Should().BeFalse();
        style.FontColor.Should().Be(CellColor.Black);

        command.Revert(ctx);

        sheet.Hyperlinks[addr].Should().Be("https://example.com");
        sheet.HyperlinkMetadata[addr].Should().Be(new HyperlinkMetadata(
            HyperlinkTargetKind.ExistingFileOrWebPage,
            "Example",
            "https://example.com"));
        wb.GetStyle(sheet.GetCell(addr)!.StyleId).Underline.Should().BeTrue();
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
