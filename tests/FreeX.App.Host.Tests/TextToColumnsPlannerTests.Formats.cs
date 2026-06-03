using System.Globalization;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class TextToColumnsPlannerTests
{
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
    public void BuildEdits_UsesAdvancedNumberOptionsForGeneralColumns()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 2, 1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("1.234,50;42-"));

        var edits = TextToColumnsPlanner.BuildEdits(
            sheet,
            range,
            new CellAddress(sheet.Id, 2, 3),
            ";",
            advancedOptions: new TextToColumnsAdvancedOptions(",", ".", TrailingMinusNumbers: true));

        edits.Select(edit => edit.NewCell.Value).Should().Equal(
            new NumberValue(1234.50),
            new NumberValue(-42));
    }

    [Fact]
    public void BuildEdits_UsesCurrentCultureForGeneralNumbersWithInvariantFallback()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
        try
        {
            var sheet = new Sheet(SheetId.New(), "Sheet1");
            var range = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 2, 1));
            sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("1,25;1.25;NaN;Infinity"));

            var edits = TextToColumnsPlanner.BuildEdits(sheet, range, new CellAddress(sheet.Id, 2, 3), ";");

            edits.Select(edit => edit.NewCell.Value).Should().Equal(
                new NumberValue(1.25),
                new NumberValue(1.25),
                new TextValue("NaN"),
                new TextValue("Infinity"));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void BuildEdits_UsesSelectedDateColumnFormat()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 2, 1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("31/12/2025,2026-01-15"));

        var edits = TextToColumnsPlanner.BuildEdits(
            sheet,
            range,
            new CellAddress(sheet.Id, 2, 3),
            ",",
            [
                TextToColumnsColumnFormat.DateDMY,
                TextToColumnsColumnFormat.DateYMD
            ]);

        edits.Select(edit => edit.NewCell.Value).Should().Equal(
            new DateTimeValue(new DateTime(2025, 12, 31).ToOADate()),
            new DateTimeValue(new DateTime(2026, 1, 15).ToOADate()));
    }
}
