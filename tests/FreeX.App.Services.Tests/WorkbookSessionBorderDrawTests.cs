using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class WorkbookSessionBorderDrawTests
{
    private static readonly CellColor Accent = new(41, 92, 173);

    [Fact]
    public void SetSelectedRangeDrawBorder_DrawGridCarriesSelectedStyleAndColor()
    {
        using var session = CreateTwoByTwoSelection();

        var result = session.SetSelectedRangeDrawBorder(
            BorderDrawMode.DrawGrid,
            BorderStyle.Double,
            Accent);

        result.Success.Should().BeTrue(result.ErrorMessage);
        foreach (var address in session.SelectedRange.AllCells())
        {
            var style = GetStyle(session, address);
            style.BorderTop.Should().Be(new CellBorder(BorderStyle.Double, Accent));
            style.BorderRight.Should().Be(new CellBorder(BorderStyle.Double, Accent));
            style.BorderBottom.Should().Be(new CellBorder(BorderStyle.Double, Accent));
            style.BorderLeft.Should().Be(new CellBorder(BorderStyle.Double, Accent));
        }
    }

    [Fact]
    public void SetSelectedRangeDrawBorder_DrawAppliesOnlyRangeOutline()
    {
        using var session = CreateTwoByTwoSelection();

        var result = session.SetSelectedRangeDrawBorder(
            BorderDrawMode.Draw,
            BorderStyle.Dotted,
            Accent);

        result.Success.Should().BeTrue(result.ErrorMessage);
        var topLeft = GetStyle(session, session.SelectedRange.Start);
        topLeft.BorderTop.Should().Be(new CellBorder(BorderStyle.Dotted, Accent));
        topLeft.BorderLeft.Should().Be(new CellBorder(BorderStyle.Dotted, Accent));
        topLeft.BorderRight.Should().Be(new CellBorder(BorderStyle.None));
        topLeft.BorderBottom.Should().Be(new CellBorder(BorderStyle.None));

        var bottomRight = GetStyle(session, session.SelectedRange.End);
        bottomRight.BorderRight.Should().Be(new CellBorder(BorderStyle.Dotted, Accent));
        bottomRight.BorderBottom.Should().Be(new CellBorder(BorderStyle.Dotted, Accent));
        bottomRight.BorderTop.Should().Be(new CellBorder(BorderStyle.None));
        bottomRight.BorderLeft.Should().Be(new CellBorder(BorderStyle.None));
    }

    [Fact]
    public void SetSelectedRangeDrawBorder_EraseClearsEveryEdge()
    {
        using var session = CreateTwoByTwoSelection();
        session.SetSelectedRangeDrawBorder(BorderDrawMode.DrawGrid, BorderStyle.Thick, Accent)
            .Success.Should().BeTrue();

        var result = session.SetSelectedRangeDrawBorder(BorderDrawMode.Erase);

        result.Success.Should().BeTrue(result.ErrorMessage);
        foreach (var address in session.SelectedRange.AllCells())
        {
            var style = GetStyle(session, address);
            style.BorderTop.Should().Be(new CellBorder(BorderStyle.None));
            style.BorderRight.Should().Be(new CellBorder(BorderStyle.None));
            style.BorderBottom.Should().Be(new CellBorder(BorderStyle.None));
            style.BorderLeft.Should().Be(new CellBorder(BorderStyle.None));
        }
    }

    [Fact]
    public void SetSelectedRangeDrawBorder_RejectsInactiveMode()
    {
        using var session = CreateTwoByTwoSelection();

        var act = () => session.SetSelectedRangeDrawBorder(BorderDrawMode.None);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("mode");
    }

    private static WorkbookSession CreateTwoByTwoSelection()
    {
        var session = new WorkbookSessionFactory().CreateNew(viewportHeight: 240, viewportWidth: 320);
        var sheetId = session.ActiveSheet.Id;
        session.SelectRange(new GridRange(
            new CellAddress(sheetId, 1, 1),
            new CellAddress(sheetId, 2, 2)));
        return session;
    }

    private static CellStyle GetStyle(WorkbookSession session, CellAddress address)
    {
        var styleId = session.ActiveSheet.GetCell(address)?.StyleId ??
            session.ActiveSheet.GetStyleOnly(address.Row, address.Col) ??
            StyleId.Default;
        return session.Workbook.GetStyle(styleId);
    }
}
