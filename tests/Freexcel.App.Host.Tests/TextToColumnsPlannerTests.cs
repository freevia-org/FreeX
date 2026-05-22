using FluentAssertions;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class TextToColumnsPlannerTests
{
    [Fact]
    public void BuildEdits_SplitsTextFromFirstColumnAcrossColumns()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 2, 3), new CellAddress(sheet.Id, 3, 3));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new TextValue("East, 42, Open"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new TextValue("West, 7, Closed"));

        var edits = TextToColumnsPlanner.BuildEdits(sheet, range, ',');

        edits.Select(edit => edit.Address).Should().Equal(
            new CellAddress(sheet.Id, 2, 3),
            new CellAddress(sheet.Id, 2, 4),
            new CellAddress(sheet.Id, 2, 5),
            new CellAddress(sheet.Id, 3, 3),
            new CellAddress(sheet.Id, 3, 4),
            new CellAddress(sheet.Id, 3, 5));
        edits.Select(edit => edit.NewCell.Value).Should().Equal(
            new TextValue("East"),
            new NumberValue(42),
            new TextValue("Open"),
            new TextValue("West"),
            new NumberValue(7),
            new TextValue("Closed"));
    }

    [Fact]
    public void BuildEdits_IgnoresNonTextCellsInSourceColumn()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("A;B"));

        var edits = TextToColumnsPlanner.BuildEdits(sheet, range, ';');

        edits.Should().HaveCount(2);
        edits[0].Address.Should().Be(new CellAddress(sheet.Id, 2, 1));
        edits[0].NewCell.Value.Should().Be(new TextValue("A"));
        edits[1].Address.Should().Be(new CellAddress(sheet.Id, 2, 2));
        edits[1].NewCell.Value.Should().Be(new TextValue("B"));
    }

    [Fact]
    public void BuildEdits_CanWriteSplitOutputToExplicitDestination()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 3, 1));
        var destination = new CellAddress(sheet.Id, 2, 6);
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East,42"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("West,7"));

        var edits = TextToColumnsPlanner.BuildEdits(sheet, range, destination, ',');

        edits.Select(edit => edit.Address).Should().Equal(
            new CellAddress(sheet.Id, 2, 6),
            new CellAddress(sheet.Id, 2, 7),
            new CellAddress(sheet.Id, 3, 6),
            new CellAddress(sheet.Id, 3, 7));
        edits.Select(edit => edit.Address.Col).Should().NotContain(1u);
    }

    [Fact]
    public void BuildEdits_AppliesTextAndSkipColumnFormats()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 2, 1));
        var destination = new CellAddress(sheet.Id, 2, 5);
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("00123,Skip Me,42"));

        var edits = TextToColumnsPlanner.BuildEdits(
            sheet,
            range,
            destination,
            ',',
            [
                TextToColumnsColumnFormat.Text,
                TextToColumnsColumnFormat.Skip,
                TextToColumnsColumnFormat.General
            ]);

        edits.Select(edit => edit.Address).Should().Equal(
            new CellAddress(sheet.Id, 2, 5),
            new CellAddress(sheet.Id, 2, 6));
        edits.Select(edit => edit.NewCell.Value).Should().Equal(
            new TextValue("00123"),
            new NumberValue(42));
    }

    [Fact]
    public void BuildEdits_SplitsOnAnySelectedDelimiter()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("East,42;Open"));

        var edits = TextToColumnsPlanner.BuildEdits(sheet, range, ",;");

        edits.Select(edit => edit.NewCell.Value).Should().Equal(
            new TextValue("East"),
            new NumberValue(42),
            new TextValue("Open"));
    }

    [Fact]
    public void SplitText_DefaultsToCommaWhenDelimiterListIsEmpty()
    {
        TextToColumnsPlanner.SplitText("A,B", "").Should().Equal("A", "B");
    }

    [Fact]
    public void SplitText_HonorsExcelTextQualifier()
    {
        TextToColumnsPlanner.SplitText("\"Smith, John\",42,\"He said \"\"OK\"\"\"", ",", '"', false)
            .Should()
            .Equal("Smith, John", "42", "He said \"OK\"");
    }

    [Fact]
    public void SplitText_CanTreatConsecutiveDelimitersAsOne()
    {
        TextToColumnsPlanner.SplitText("A,,B", ",", '"', true)
            .Should()
            .Equal("A", "B");

        TextToColumnsPlanner.SplitText("A,,B", ",", '"', false)
            .Should()
            .Equal("A", "", "B");
    }

    [Fact]
    public void BuildEdits_UsesTextQualifierAndConsecutiveDelimiterOptions()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("\"Smith, John\",,42"));

        var edits = TextToColumnsPlanner.BuildEdits(sheet, range, ",", '"', true);

        edits.Select(edit => edit.NewCell.Value).Should().Equal(
            new TextValue("Smith, John"),
            new NumberValue(42));
    }

    [Fact]
    public void SplitFixedWidthText_UsesSortedUniqueBreakPositions()
    {
        TextToColumnsPlanner.SplitFixedWidthText("East0042Open", [8, 4, 4])
            .Should()
            .Equal("East", "0042", "Open");
    }

    [Fact]
    public void BuildFixedWidthEdits_SplitsTextAcrossColumns()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 2, 3), new CellAddress(sheet.Id, 2, 3));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new TextValue("East0042Open"));

        var edits = TextToColumnsPlanner.BuildFixedWidthEdits(sheet, range, [4, 8]);

        edits.Select(edit => edit.NewCell.Value).Should().Equal(
            new TextValue("East"),
            new NumberValue(42),
            new TextValue("Open"));
    }
}
