using FreeX.Core.Model;
using FreeX.Core.Commands;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

public partial class SortFilterTests
{
    [Fact]
    public void Filter_HidesNonMatchingRows()
    {
        var (wb, sheet, ctx) = MakeContext();
        var sid = sheet.Id;

        // A1=Header, A2=Apple, A3=Banana, A4=Apple
        sheet.SetCell(new CellAddress(sid, 1, 1), new TextValue("Header"));
        sheet.SetCell(new CellAddress(sid, 2, 1), new TextValue("Apple"));
        sheet.SetCell(new CellAddress(sid, 3, 1), new TextValue("Banana"));
        sheet.SetCell(new CellAddress(sid, 4, 1), new TextValue("Apple"));

        var range = new GridRange(
            new CellAddress(sid, 1, 1),
            new CellAddress(sid, 4, 1));

        var cmd = new FilterCommand(sid, range, filterColOffset: 0, allowedValues: ["Apple"]);
        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeTrue();
        // Row 3 (Banana) should be hidden; rows 1, 2, 4 (Header, Apple, Apple) should be visible
        sheet.FilterHiddenRows.Should().Contain(3u);
        sheet.FilterHiddenRows.Should().NotContain(1u);
        sheet.FilterHiddenRows.Should().NotContain(2u);
        sheet.FilterHiddenRows.Should().NotContain(4u);
    }

    [Fact]
    public void Filter_Clear_UnhidesAllRows()
    {
        var (wb, sheet, ctx) = MakeContext();
        var sid = sheet.Id;

        sheet.SetCell(new CellAddress(sid, 1, 1), new TextValue("Header"));
        sheet.SetCell(new CellAddress(sid, 2, 1), new TextValue("Apple"));
        sheet.SetCell(new CellAddress(sid, 3, 1), new TextValue("Banana"));

        var range = new GridRange(
            new CellAddress(sid, 1, 1),
            new CellAddress(sid, 3, 1));

        // First apply a filter
        var filterCmd = new FilterCommand(sid, range, filterColOffset: 0, allowedValues: ["Apple"]);
        filterCmd.Apply(ctx);
        sheet.FilterHiddenRows.Should().NotBeEmpty();

        // Then clear it
        var clearCmd = new FilterCommand(sid, range, filterColOffset: 0, allowedValues: []);
        clearCmd.Apply(ctx);

        sheet.FilterHiddenRows.Should().BeEmpty();
    }

    [Fact]
    public void CellFillColorFilterCommand_HidesRowsWithoutMatchingFillColorAndUndoRestores()
    {
        var (wb, sheet, ctx) = MakeContext();
        var sid = sheet.Id;
        var green = new CellColor(0, 176, 80);
        var yellow = new CellColor(255, 192, 0);
        var greenCellStyle = CellStyle.Default.Clone();
        greenCellStyle.FillColor = green;
        var yellowCellStyle = CellStyle.Default.Clone();
        yellowCellStyle.FillColor = yellow;
        var greenStyle = wb.RegisterStyle(greenCellStyle);
        var yellowStyle = wb.RegisterStyle(yellowCellStyle);

        sheet.SetCell(new CellAddress(sid, 1, 1), new TextValue("Status"));
        sheet.SetCell(new CellAddress(sid, 2, 1), new TextValue("Ready"));
        sheet.SetCell(new CellAddress(sid, 3, 1), new TextValue("Blocked"));
        sheet.SetCell(new CellAddress(sid, 4, 1), new TextValue("Open"));
        sheet.GetCell(2, 1)!.StyleId = greenStyle;
        sheet.GetCell(3, 1)!.StyleId = yellowStyle;
        sheet.HiddenRows.Add(99);

        var range = new GridRange(
            new CellAddress(sid, 1, 1),
            new CellAddress(sid, 4, 1));
        var command = new CellFillColorFilterCommand(sid, range, filterColOffset: 0, green);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.FilterHiddenRows.Should().BeEquivalentTo([3u, 4u]);
        sheet.HiddenRows.Should().Contain(99u);

        command.Revert(ctx);

        sheet.FilterHiddenRows.Should().BeEmpty();
        sheet.HiddenRows.Should().Contain(99u);
    }

    // ── New edge-case tests ───────────────────────────────────────────────────

    [Fact]
    public void CellNoFillColorFilterCommand_HidesRowsWithFillColorAndUndoRestores()
    {
        var (wb, sheet, ctx) = MakeContext();
        var sid = sheet.Id;
        var green = new CellColor(0, 176, 80);
        var greenCellStyle = CellStyle.Default.Clone();
        greenCellStyle.FillColor = green;
        var greenStyle = wb.RegisterStyle(greenCellStyle);

        sheet.SetCell(new CellAddress(sid, 1, 1), new TextValue("Status"));
        sheet.SetCell(new CellAddress(sid, 2, 1), new TextValue("Ready"));
        sheet.SetCell(new CellAddress(sid, 3, 1), new TextValue("Open"));
        sheet.GetCell(2, 1)!.StyleId = greenStyle;
        sheet.HiddenRows.Add(99);

        var range = new GridRange(
            new CellAddress(sid, 1, 1),
            new CellAddress(sid, 3, 1));
        var command = new CellNoFillColorFilterCommand(sid, range, filterColOffset: 0);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.FilterHiddenRows.Should().BeEquivalentTo([2u]);
        sheet.HiddenRows.Should().Contain(99u);

        command.Revert(ctx);

        sheet.FilterHiddenRows.Should().BeEmpty();
        sheet.HiddenRows.Should().Contain(99u);
    }

    [Fact]
    public void CellFontColorFilterCommand_HidesRowsWithoutMatchingFontColorAndUndoRestores()
    {
        var (wb, sheet, ctx) = MakeContext();
        var sid = sheet.Id;
        var red = new CellColor(192, 0, 0);
        var blue = new CellColor(0, 112, 192);
        var redStyleValue = CellStyle.Default.Clone();
        redStyleValue.FontColor = red;
        var blueStyleValue = CellStyle.Default.Clone();
        blueStyleValue.FontColor = blue;
        var redStyle = wb.RegisterStyle(redStyleValue);
        var blueStyle = wb.RegisterStyle(blueStyleValue);

        sheet.SetCell(new CellAddress(sid, 1, 1), new TextValue("Status"));
        sheet.SetCell(new CellAddress(sid, 2, 1), new TextValue("Ready"));
        sheet.SetCell(new CellAddress(sid, 3, 1), new TextValue("Blocked"));
        sheet.SetCell(new CellAddress(sid, 4, 1), new TextValue("Open"));
        sheet.GetCell(2, 1)!.StyleId = redStyle;
        sheet.GetCell(3, 1)!.StyleId = blueStyle;
        sheet.HiddenRows.Add(99);

        var range = new GridRange(
            new CellAddress(sid, 1, 1),
            new CellAddress(sid, 4, 1));
        var command = new CellFontColorFilterCommand(sid, range, filterColOffset: 0, red);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.FilterHiddenRows.Should().BeEquivalentTo([3u, 4u]);
        sheet.HiddenRows.Should().Contain(99u);

        command.Revert(ctx);

        sheet.FilterHiddenRows.Should().BeEmpty();
        sheet.HiddenRows.Should().Contain(99u);
    }

    [Fact]
    public void Filter_Clear_DoesNotDestroyExternallyHiddenRows()
    {
        var (wb, sheet, ctx) = MakeContext();
        var sid = sheet.Id;

        // A1=Header, A2=Apple, A3=Banana
        sheet.SetCell(new CellAddress(sid, 1, 1), new TextValue("Header"));
        sheet.SetCell(new CellAddress(sid, 2, 1), new TextValue("Apple"));
        sheet.SetCell(new CellAddress(sid, 3, 1), new TextValue("Banana"));

        // Externally hide row 5 (e.g. imported from XLSX) BEFORE applying any filter
        sheet.HiddenRows.Add(5u);

        var range = new GridRange(
            new CellAddress(sid, 1, 1),
            new CellAddress(sid, 3, 1));

        // Apply then clear filter on A1:A3
        var filterCmd = new FilterCommand(sid, range, filterColOffset: 0, allowedValues: ["Apple"]);
        filterCmd.Apply(ctx);

        var clearCmd = new FilterCommand(sid, range, filterColOffset: 0, allowedValues: []);
        clearCmd.Apply(ctx);

        // Row 5 must still be hidden — it was outside the filter's range
        sheet.HiddenRows.Should().Contain(5u);
        // Rows in filter range should be visible after clear
        sheet.HiddenRows.Should().NotContain(2u);
        sheet.HiddenRows.Should().NotContain(3u);
    }

    [Fact]
    public void Filter_PreservesManuallyHiddenRowsInsideFilterRange()
    {
        var (wb, sheet, ctx) = MakeContext();
        var sid = sheet.Id;

        sheet.SetCell(new CellAddress(sid, 1, 1), new TextValue("Header"));
        sheet.SetCell(new CellAddress(sid, 2, 1), new TextValue("Apple"));
        sheet.SetCell(new CellAddress(sid, 3, 1), new TextValue("Banana"));
        sheet.HiddenRows.Add(2u);

        var range = new GridRange(
            new CellAddress(sid, 1, 1),
            new CellAddress(sid, 3, 1));

        new FilterCommand(sid, range, filterColOffset: 0, allowedValues: ["Apple"]).Apply(ctx);

        sheet.HiddenRows.Should().Contain(2u, "manual hidden rows stay hidden even when they match the filter");
        sheet.FilterHiddenRows.Should().Contain(3u, "filter-hidden rows are tracked separately from manual row hiding");

        new FilterCommand(sid, range, filterColOffset: 0, allowedValues: []).Apply(ctx);

        sheet.HiddenRows.Should().Contain(2u);
        sheet.FilterHiddenRows.Should().BeEmpty();
    }

    [Fact]
    public void Filter_ReplacesExistingFilterInRange()
    {
        var (wb, sheet, ctx) = MakeContext();
        var sid = sheet.Id;

        // A1=Header, A2=Apple, A3=Banana, A4=Apple
        sheet.SetCell(new CellAddress(sid, 1, 1), new TextValue("Header"));
        sheet.SetCell(new CellAddress(sid, 2, 1), new TextValue("Apple"));
        sheet.SetCell(new CellAddress(sid, 3, 1), new TextValue("Banana"));
        sheet.SetCell(new CellAddress(sid, 4, 1), new TextValue("Apple"));

        var range = new GridRange(
            new CellAddress(sid, 1, 1),
            new CellAddress(sid, 4, 1));

        // First filter: show only Apple → Banana (row 3) hidden
        var appleCmd = new FilterCommand(sid, range, filterColOffset: 0, allowedValues: ["Apple"]);
        appleCmd.Apply(ctx);
        sheet.FilterHiddenRows.Should().Contain(3u);

        // Second filter: show only Banana → Apple rows (2, 4) hidden, Banana (row 3) visible
        var bananaCmd = new FilterCommand(sid, range, filterColOffset: 0, allowedValues: ["Banana"]);
        bananaCmd.Apply(ctx);

        sheet.FilterHiddenRows.Should().Contain(2u);
        sheet.FilterHiddenRows.Should().Contain(4u);
        sheet.FilterHiddenRows.Should().NotContain(3u);
    }
}
