using FluentAssertions;
using FreeX.App.Presentation.Filtering;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Filtering;

public sealed class WorksheetFilterWorkflowSessionTests
{
    [Fact]
    public void PlanDialogResult_AutoFilterSortKeepsHeaderOutsideCommandRange()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        SetText(sheet, 1, 1, "Name");
        SetText(sheet, 2, 1, "Zulu");
        SetText(sheet, 3, 1, "Alpha");
        var range = Range(sheet, 1, 1, 3, 1);
        var session = new WorksheetFilterWorkflowSession();

        var plan = session.PlanDialogResult(
            sheet.Id,
            range,
            0,
            new AutoFilterDialogResult(
                AutoFilterSortDirection.Ascending,
                [],
                string.Empty,
                string.Empty));
        var outcome = plan.CreateCommand().Apply(new TestCommandContext(workbook));

        outcome.Success.Should().BeTrue();
        sheet.GetValue(1, 1).Should().Be(new TextValue("Name"));
        sheet.GetValue(2, 1).Should().Be(new TextValue("Alpha"));
        sheet.GetValue(3, 1).Should().Be(new TextValue("Zulu"));
    }

    [Fact]
    public void PlanDialogResult_InvalidCriteriaReturnsPortableError()
    {
        var sheet = new Workbook("Book").AddSheet("Sheet1");
        var range = Range(sheet, 1, 1, 3, 1);
        var session = new WorksheetFilterWorkflowSession();

        var plan = session.PlanDialogResult(
            sheet.Id,
            range,
            0,
            new AutoFilterDialogResult(
                AutoFilterSortDirection.None,
                [],
                string.Empty,
                "top:0"));

        plan.Success.Should().BeFalse();
        plan.Error.Should().Be(WorksheetFilterMutationError.InvalidCriteria);
        plan.PromptError.Should().Be(FilterPromptPlanError.PositiveItemCount);
    }

    [Fact]
    public void CreateClearAllPlan_ClearsEveryActiveColumnAsOneWorkflow()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        SetText(sheet, 1, 1, "Region");
        SetText(sheet, 1, 2, "Status");
        SetText(sheet, 2, 1, "North");
        SetText(sheet, 2, 2, "Open");
        SetText(sheet, 3, 1, "South");
        SetText(sheet, 3, 2, "Closed");
        var range = Range(sheet, 1, 1, 3, 2);
        sheet.AutoFilter = new WorksheetAutoFilterModel(range.ToString(), null);
        var context = new TestCommandContext(workbook);
        new FilterCommand(sheet.Id, range, 0, ["North"]).Apply(context).Success.Should().BeTrue();
        new FilterCommand(sheet.Id, range, 1, ["Open"]).Apply(context).Success.Should().BeTrue();
        var session = new WorksheetFilterWorkflowSession();

        var plan = session.CreateClearAllPlan(sheet, range);
        var outcome = plan.Command.Apply(context);

        outcome.Success.Should().BeTrue();
        plan.DefinitionCount.Should().Be(2);
        sheet.ActiveValueFilterColumns.Should().BeEmpty();
        sheet.FilterHiddenRows.Should().BeEmpty();
        sheet.AutoFilter!.FilterColumns.Should().BeEmpty();
    }

    [Fact]
    public void CreateReapplyPlan_ReconstructsWorksheetValueFilterFromDurableMetadata()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        SetText(sheet, 1, 1, "Status");
        SetText(sheet, 2, 1, "Open");
        SetText(sheet, 3, 1, "Closed");
        var range = Range(sheet, 1, 1, 3, 1);
        sheet.AutoFilter = new WorksheetAutoFilterModel(range.ToString(), null);
        sheet.AutoFilter.FilterColumns.Add(new WorksheetAutoFilterColumnModel(0, ["Open"]));
        var session = new WorksheetFilterWorkflowSession();

        var plan = session.CreateReapplyPlan(sheet);
        var outcome = plan!.CreateCommand("Reapply Filters").Apply(new TestCommandContext(workbook));

        outcome.Success.Should().BeTrue();
        plan.DefinitionCount.Should().Be(1);
        sheet.FilterHiddenRows.Should().Contain(3);
        sheet.FilterHiddenRows.Should().NotContain(2);
    }

    [Fact]
    public void CreateReapplyPlan_RetainsLiveStructuredTableCriterionNotModeledDurably()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        SetText(sheet, 1, 1, "Score");
        SetNumber(sheet, 2, 1, 1);
        SetNumber(sheet, 3, 1, 10);
        SetNumber(sheet, 4, 1, 5);
        var range = Range(sheet, 1, 1, 4, 1);
        sheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = range,
            HasAutoFilter = true
        });
        var context = new TestCommandContext(workbook);
        var session = new WorksheetFilterWorkflowSession();
        var mutation = session.PlanDialogResult(
            sheet.Id,
            range,
            0,
            new AutoFilterDialogResult(
                AutoFilterSortDirection.None,
                [],
                string.Empty,
                "top:1"));
        mutation.CreateCommand().Apply(context).Success.Should().BeTrue();
        session.RecordSuccessfulMutation(mutation);
        SetNumber(sheet, 2, 1, 20);

        var reapply = session.CreateReapplyPlan(sheet);
        var outcome = reapply!.CreateCommand("Reapply Filters").Apply(context);

        outcome.Success.Should().BeTrue();
        sheet.FilterHiddenRows.Should().NotContain(2);
        sheet.FilterHiddenRows.Should().Contain([3u, 4u]);
    }

    private static GridRange Range(Sheet sheet, uint startRow, uint startCol, uint endRow, uint endCol) =>
        new(new CellAddress(sheet.Id, startRow, startCol), new CellAddress(sheet.Id, endRow, endCol));

    private static void SetText(Sheet sheet, uint row, uint col, string text) =>
        sheet.SetCell(new CellAddress(sheet.Id, row, col), new TextValue(text));

    private static void SetNumber(Sheet sheet, uint row, uint col, double value) =>
        sheet.SetCell(new CellAddress(sheet.Id, row, col), new NumberValue(value));

    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;
        public Sheet GetSheet(SheetId sheetId) => Workbook.GetSheet(sheetId)!;
    }
}

public sealed class WorksheetFilterWorkflowSessionSourceGuardTests
{
    [Fact]
    public void RendererLayersDelegateFilterWorkflowOwnershipToPresentation()
    {
        var presentationRoot = RepositoryFileLocator.FindDirectory("src", "FreeX.App.Presentation");
        var repoRoot = Directory.GetParent(presentationRoot)?.Parent?.FullName
            ?? throw new DirectoryNotFoundException("Could not resolve repository root.");
        var hostFilter = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "FreeX.App.Host",
            "MainWindow.DataFilterCommands.cs"));
        var hostData = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "FreeX.App.Host",
            "MainWindow.DataCommands.cs"));
        var avaloniaFilter = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "FreeX.App.Avalonia",
            "MainWindow.AutoFilter.cs"));
        var avaloniaData = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "FreeX.App.Avalonia",
            "MainWindow.DataTools.cs"));
        var avaloniaMain = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "FreeX.App.Avalonia",
            "MainWindow.cs"));

        hostFilter.Should().Contain("_filterWorkflowSession.PlanDialogResult(");
        hostFilter.Should().Contain("_filterWorkflowSession.CreateReapplyPlan(sheet)");
        hostFilter.Should().Contain("_filterWorkflowSession.CreateClearAllPlan(sheet, range)");
        hostFilter.Should().NotContain("_activeAutoFilterColumnFactories");
        hostFilter.Should().NotContain("_lastAutoFilterRange");
        hostFilter.Should().NotContain("BuildClearAllValueFiltersCommand");
        hostData.Should().Contain("_filterWorkflowSession.RememberAdvancedFilter(");

        avaloniaFilter.Should().Contain("_filterWorkflowSession.PlanDialogResult(");
        avaloniaFilter.Should().Contain("_filterWorkflowSession.CreateClearAllPlan(sheet, range)");
        avaloniaFilter.Should().NotContain("new FilterCommand(");
        avaloniaFilter.Should().NotContain("new SortCommand(");
        avaloniaFilter.Should().NotContain("new CellFillColorFilterCommand(");
        avaloniaData.Should().Contain("_filterWorkflowSession.CreateReapplyPlan(sheet)");
        avaloniaData.Should().NotContain("TryBuildAutoFilterColumnReapplyCommand");
        avaloniaData.Should().NotContain("new CompositeWorkbookCommand(");
        avaloniaMain.Should().Contain("_filterWorkflowSession.RememberAdvancedFilter(");
    }
}
