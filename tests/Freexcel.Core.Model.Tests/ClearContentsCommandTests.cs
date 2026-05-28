using FluentAssertions;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.Core.Model.Tests;

public sealed class ClearContentsCommandTests
{
    [Fact]
    public void ClearContents_ClearsValuesAndFormulasButPreservesStyle()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 1, 1);
        var style = workbook.RegisterStyle(new CellStyle { Bold = true });
        sheet.SetCell(address, new Cell
        {
            FormulaText = "B1+1",
            Value = new NumberValue(5),
            StyleId = style
        });

        var command = new ClearContentsCommand(sheet.Id, new GridRange(address, address));

        command.Apply(new SimpleCommandContext(workbook)).Success.Should().BeTrue();

        var cleared = sheet.GetCell(address);
        cleared.Should().NotBeNull();
        var clearedCell = cleared!;
        clearedCell.HasFormula.Should().BeFalse();
        clearedCell.Value.Should().Be(BlankValue.Instance);
        clearedCell.StyleId.Should().Be(style);
    }

    [Fact]
    public void ClearContents_UndoRestoresPreviousCells()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(address, Cell.FromValue(new TextValue("old")));
        var context = new SimpleCommandContext(workbook);
        var command = new ClearContentsCommand(sheet.Id, new GridRange(address, address));

        command.Apply(context).Success.Should().BeTrue();
        command.Revert(context);

        sheet.GetCell(address)!.Value.Should().Be(new TextValue("old"));
    }

    [Fact]
    public void ClearContents_RemovesHyperlinksAndUndoRestoresThem()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 1, 1);
        var style = workbook.RegisterStyle(new CellStyle
        {
            Underline = true,
            FontColor = workbook.Theme.ResolveColor(WorkbookThemeColorSlot.Hyperlink)
        });
        var cell = Cell.FromValue(new TextValue("Example"));
        cell.StyleId = style;
        sheet.SetCell(address, cell);
        sheet.Hyperlinks[address] = "https://example.com";
        sheet.HyperlinkMetadata[address] = new HyperlinkMetadata(
            HyperlinkTargetKind.ExistingFileOrWebPage,
            "Example site",
            "https://example.com");
        var context = new SimpleCommandContext(workbook);
        var command = new ClearContentsCommand(sheet.Id, new GridRange(address, address));

        command.Apply(context).Success.Should().BeTrue();

        sheet.GetCell(address)!.Value.Should().Be(BlankValue.Instance);
        sheet.GetCell(address)!.StyleId.Should().Be(style);
        sheet.Hyperlinks.Should().NotContainKey(address);
        sheet.HyperlinkMetadata.Should().NotContainKey(address);

        command.Revert(context);

        sheet.GetValue(address).Should().Be(new TextValue("Example"));
        sheet.Hyperlinks[address].Should().Be("https://example.com");
        sheet.HyperlinkMetadata[address].Should().Be(new HyperlinkMetadata(
            HyperlinkTargetKind.ExistingFileOrWebPage,
            "Example site",
            "https://example.com"));
    }

    [Fact]
    public void ClearContents_PreservesStyleOnlyFormattingAndUndoRestoresStyleOnlyCell()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 1, 1);
        var style = workbook.RegisterStyle(new CellStyle { Bold = true });
        sheet.SetStyleOnly(address.Row, address.Col, style);
        var context = new SimpleCommandContext(workbook);
        var command = new ClearContentsCommand(sheet.Id, new GridRange(address, address));

        command.Apply(context).Success.Should().BeTrue();

        sheet.GetCell(address)!.Value.Should().Be(BlankValue.Instance);
        sheet.GetCell(address)!.StyleId.Should().Be(style);
        sheet.GetStyleOnly(address.Row, address.Col).Should().BeNull();

        command.Revert(context);

        sheet.GetCell(address).Should().BeNull();
        sheet.GetStyleOnly(address.Row, address.Col).Should().Be(style);
    }

    [Fact]
    public void ClearContents_RejectsLockedCellsOnProtectedSheet()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(address, Cell.FromValue(new TextValue("keep")));
        sheet.IsProtected = true;

        var outcome = new ClearContentsCommand(sheet.Id, new GridRange(address, address))
            .Apply(new SimpleCommandContext(workbook));

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        sheet.GetCell(address)!.Value.Should().Be(new TextValue("keep"));
    }

    [Fact]
    public void ClearContents_AllowsUnlockedCellsOnProtectedSheet()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 1, 1);
        var unlockedStyle = workbook.RegisterStyle(new CellStyle { Locked = false });
        var cell = Cell.FromValue(new TextValue("clear me"));
        cell.StyleId = unlockedStyle;
        sheet.SetCell(address, cell);
        sheet.IsProtected = true;

        var outcome = new ClearContentsCommand(sheet.Id, new GridRange(address, address))
            .Apply(new SimpleCommandContext(workbook));

        outcome.Success.Should().BeTrue();
        sheet.GetCell(address)!.Value.Should().Be(BlankValue.Instance);
        sheet.GetCell(address)!.StyleId.Should().Be(unlockedStyle);
    }

    private sealed class SimpleCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook => workbook;
        public Sheet GetSheet(SheetId sheetId) => workbook.GetSheet(sheetId)!;
    }
}
