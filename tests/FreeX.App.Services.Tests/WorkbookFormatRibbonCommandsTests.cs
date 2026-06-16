using FluentAssertions;
using Free.Shared.Ribbon;
using FreeX.App.Services.Ribbon;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class WorkbookFormatRibbonCommandsTests
{
    [Fact]
    public void BoldCommand_TogglesSelectionBoldAndReportsState()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, new TextValue("value"));
        var session = CreateSession(workbook);
        session.SelectCell(a1);

        WorkbookCellEditResult? lastResult = null;
        bool? lastApplied = null;
        var bold = WorkbookFormatRibbonCommands.Bold(
            () => session,
            (result, on) => { lastResult = result; lastApplied = on; });

        bold.GetState().IsChecked.Should().BeFalse("the selection starts unbolded");

        bold.Execute(RibbonCommandContext.Empty);

        session.IsSelectedRangeStartBold.Should().BeTrue();
        bold.GetState().IsChecked.Should().BeTrue("the command's state mirrors the session");
        lastResult!.Success.Should().BeTrue();
        lastApplied.Should().BeTrue();

        bold.Execute(RibbonCommandContext.Empty);

        session.IsSelectedRangeStartBold.Should().BeFalse("executing again toggles it off");
        bold.GetState().IsChecked.Should().BeFalse();
        lastApplied.Should().BeFalse();
    }

    [Fact]
    public void ItalicAndUnderline_AreIndependentFacets()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, new TextValue("value"));
        var session = CreateSession(workbook);
        session.SelectCell(a1);

        WorkbookFormatRibbonCommands.Italic(() => session).Execute(RibbonCommandContext.Empty);
        WorkbookFormatRibbonCommands.Underline(() => session).Execute(RibbonCommandContext.Empty);

        session.IsSelectedRangeStartItalic.Should().BeTrue();
        session.IsSelectedRangeStartUnderline.Should().BeTrue();
        session.IsSelectedRangeStartBold.Should().BeFalse("italic/underline must not touch bold");
    }

    [Fact]
    public void Command_WithNoSession_IsNoOpAndReportsDefaultState()
    {
        var bold = WorkbookFormatRibbonCommands.Bold(() => null);

        bold.GetState().Should().Be(RibbonCommandState.Default);

        var act = () => bold.Execute(RibbonCommandContext.Empty);
        act.Should().NotThrow();
    }

    private static WorkbookSession CreateSession(Workbook workbook) =>
        new WorkbookSessionFactory().Create(
            new StartupWorkbookLoadResult(workbook, "Book.fxl", "Opened .fxl.", IsFallback: false),
            viewportHeight: 240,
            viewportWidth: 320);

    private static Workbook CreateWorkbook()
    {
        var workbook = new Workbook("Book");
        workbook.AddSheet("Sheet1");
        workbook.ActiveSheetIndex = 0;
        return workbook;
    }
}
