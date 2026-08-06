using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class FindReplaceWorkflowSessionTests
{
    [Fact]
    public void FindNext_ChangedSearchOrder_RestartsFromActiveCellInTheNewOrder()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = Address(sheet, 1, 1);
        var b1 = Address(sheet, 1, 2);
        var a2 = Address(sheet, 2, 1);
        sheet.SetCell(b1, new TextValue("match"));
        sheet.SetCell(a2, new TextValue("match"));
        var activeCell = a1;
        var workflow = CreateWorkflow(workbook, () => activeCell, address => activeCell = address);

        var byRows = workflow.FindNext(
            "match",
            Options(sheet, FindSearchOrder.ByRows));
        var byColumns = workflow.FindNext(
            "match",
            Options(sheet, FindSearchOrder.ByColumns));

        byRows.SelectedMatch!.Address.Should().Be(b1);
        byColumns.SelectedMatch!.Address.Should().Be(a2);
        activeCell.Should().Be(a2);
    }

    [Fact]
    public void ReplaceNext_SubmittedDialogStyle_AdvancesPastStillMatchingReplacementWithoutSkippingItNextTime()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = Address(sheet, 1, 1);
        var a2 = Address(sheet, 2, 1);
        sheet.SetCell(a1, new TextValue("Report"));
        sheet.SetCell(a2, new TextValue("Report"));
        var activeCell = a1;
        var workflow = CreateWorkflow(workbook, () => activeCell, address => activeCell = address);

        var first = workflow.ReplaceNext(
            "Report",
            "Report_v2",
            Options(sheet, FindSearchOrder.ByRows),
            behavior: FindReplaceNextBehavior.SubmittedDialogStyle);
        var second = workflow.ReplaceNext(
            "Report",
            "Report_v2",
            Options(sheet, FindSearchOrder.ByRows),
            behavior: FindReplaceNextBehavior.SubmittedDialogStyle);

        first.ReplacedMatch!.Address.Should().Be(a2);
        first.CurrentMatches[first.CurrentIndex].Address.Should().Be(a1);
        second.ReplacedMatch!.Address.Should().Be(a1);
        sheet.GetCell(a1)!.Value.Should().Be(new TextValue("Report_v2"));
        sheet.GetCell(a2)!.Value.Should().Be(new TextValue("Report_v2"));
    }

    [Fact]
    public void FreeXFindReplaceRenderers_DelegateWorkflowPolicyToPortableSession()
    {
        var workflowSource = File.ReadAllText(RepositoryFileLocator.Find(
            "src", "FreeX.App.Services", "FindReplaceWorkflowSession.cs"));
        var workbookSessionSource = File.ReadAllText(RepositoryFileLocator.Find(
            "src", "FreeX.App.Services", "WorkbookSession.cs"));
        var wpfSource = File.ReadAllText(RepositoryFileLocator.Find(
            "src", "FreeX.App.Host", "FindReplaceDialog.xaml.cs"));
        var avaloniaSource = File.ReadAllText(RepositoryFileLocator.Find(
            "src", "FreeX.App.Avalonia", "MainWindow.cs"));

        workflowSource.Should().Contain("public sealed class FindReplaceWorkflowSession");
        workbookSessionSource.Should().Contain("_findReplaceWorkflow.FindNext(");
        workbookSessionSource.Should().Contain("_findReplaceWorkflow.ReplaceNext(");
        wpfSource.Should().Contain("_workflow.FindNext(");
        wpfSource.Should().Contain("_workflow.ReplaceNext(");
        avaloniaSource.Should().Contain("_session.FindNext(");
        avaloniaSource.Should().Contain("_session.ReplaceNextValue(");
        wpfSource.Should().NotContain("FindReplaceService.Find(");
        wpfSource.Should().NotContain("FindReplaceService.TryReplaceAll(");
        workbookSessionSource.Should().NotContain("private int GetNextFindResultIndex(");
        workbookSessionSource.Should().NotContain("private int GetReplaceTargetIndex(");
    }

    private static FindReplaceWorkflowSession CreateWorkflow(
        Workbook workbook,
        Func<CellAddress?> getActiveCell,
        Action<CellAddress> setActiveCell)
    {
        var commandBus = new CommandBus(_ => new WorkbookCommandContext(workbook));
        return new FindReplaceWorkflowSession(
            () => workbook,
            getActiveCell,
            address =>
            {
                setActiveCell(address);
                return WorkbookNavigationResult.Selected(new GridRange(address, address));
            },
            command =>
            {
                var outcome = commandBus.Execute(workbook.Id, command);
                return new WorkbookCellEditResult(
                    outcome.Success,
                    outcome.ErrorMessage,
                    outcome.AffectedCells ?? [],
                    RecalcReport: null,
                    IsNoOp: outcome.IsNoOp);
            });
    }

    private static FindOptions Options(Sheet sheet, FindSearchOrder searchOrder) =>
        new(
            Within: FindWithin.Sheet,
            CurrentSheetId: sheet.Id,
            SearchOrder: searchOrder,
            LookIn: FindLookIn.Values);

    private static Workbook CreateWorkbook()
    {
        var workbook = new Workbook("Book");
        workbook.AddSheet("Sheet1");
        workbook.ActiveSheetIndex = 0;
        return workbook;
    }

    private static CellAddress Address(Sheet sheet, uint row, uint column) =>
        new(sheet.Id, row, column);
}
