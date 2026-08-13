using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class WorkbookDocumentContextTests
{
    [Fact]
    public void CreateHostOwnedSession_RetargetsCommandsAndForwardsStackChanges()
    {
        var original = CreateWorkbook("Original");
        var replacement = CreateWorkbook("Replacement");
        var workbookRef = new WorkbookRef { Current = original };
        var commandBus = new CommandBus(_ => new WorkbookCommandContext(workbookRef.Current));
        var context = WorkbookDocumentContext.Attach(workbookRef, commandBus, original);
        CommandStackChangedEventArgs? observedChange = null;
        context.CommandStackChanged += (_, change) => observedChange = change;

        using var session = context.CreateHostOwnedSession(
            new WorkbookSessionFactory(),
            new StartupWorkbookLoadResult(
                replacement,
                replacement.Name,
                "Opened replacement workbook.",
                IsFallback: false),
            new RecalcEngine(new DependencyGraph(), new FormulaEvaluator()),
            new ViewportService(),
            [],
            new WorkbookDocumentState(),
            viewportHeight: 120,
            viewportWidth: 160);
        var address = new CellAddress(replacement.GetSheetAt(0).Id, 1, 1);

        session.ExecuteCommandPreservingSelection(
                EditCellsCommand.ForValue(address.Sheet, address, new NumberValue(7)))
            .Success.Should().BeTrue();

        context.CurrentWorkbook.Should().BeSameAs(replacement);
        workbookRef.Current.Should().BeSameAs(replacement);
        replacement.GetSheetAt(0).GetCell(address)!.Value.Should().Be(new NumberValue(7));
        original.GetSheetAt(0).GetCell(1, 1).Should().BeNull();
        observedChange.Should().NotBeNull();
        observedChange!.WorkbookId.Should().Be(replacement.Id);
    }

    [Fact]
    public void CreateDetached_LeavesSiblingDocumentContextUntouched()
    {
        var sharedWorkbook = CreateWorkbook("Shared");
        var replacement = CreateWorkbook("Replacement");
        var sharedContext = WorkbookDocumentContext.Create(sharedWorkbook);

        var detachedContext = sharedContext.CreateDetached();
        detachedContext.SetCurrentWorkbook(replacement);

        sharedContext.CurrentWorkbook.Should().BeSameAs(sharedWorkbook);
        detachedContext.CurrentWorkbook.Should().BeSameAs(replacement);
    }

    private static Workbook CreateWorkbook(string name)
    {
        var workbook = new Workbook(name);
        workbook.AddSheet("Sheet1");
        return workbook;
    }
}
