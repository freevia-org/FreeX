using FluentAssertions;
using FreeX.App.Presentation.Charts.Editing;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Charts;

public sealed class ChartCommandWorkflowPlannerTests
{
    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;
        public Sheet GetSheet(SheetId id) => Workbook.GetSheet(id)!;
    }

    [Fact]
    public void PlanQuickCommand_AppliesSharedTargetPolicyAndSupportGate()
    {
        var (workbook, sheet, chart) = NewChartWorkbook(ChartType.Column);

        var selectedOnly = ChartCommandWorkflowPlanner.PlanQuickCommand(
            sheet.Id,
            sheet,
            selectedChartId: null,
            ChartWorkflowTargetPolicy.SelectedOnly,
            ChartQuickCommandCatalog.ChartTitleFontSize);
        selectedOnly.Issue.Should().Be(ChartLayoutCommandIssue.MissingChart);
        selectedOnly.Command.Should().BeNull();

        var fallback = ChartCommandWorkflowPlanner.PlanQuickCommand(
            sheet.Id,
            sheet,
            selectedChartId: null,
            ChartWorkflowTargetPolicy.SelectedOrFirst,
            ChartQuickCommandCatalog.ChartTitleFontSize);
        fallback.Chart.Should().BeSameAs(chart);
        fallback.Command.Should().NotBeNull();

        var unsupported = ChartCommandWorkflowPlanner.PlanQuickCommand(
            sheet.Id,
            sheet,
            chart.Id,
            ChartWorkflowTargetPolicy.SelectedOnly,
            ChartQuickCommandCatalog.DoughnutHoleSize);
        unsupported.Issue.Should().Be(ChartLayoutCommandIssue.Unsupported);
        unsupported.Command.Should().BeNull();
    }

    [Fact]
    public void PlanQuickCommand_ReturnsExecutableLayoutTransition()
    {
        var (workbook, sheet, chart) = NewChartWorkbook(ChartType.Column);
        var initialSize = chart.ChartTitleFontSize;

        var plan = ChartCommandWorkflowPlanner.PlanQuickCommand(
            sheet.Id,
            sheet,
            chart.Id,
            ChartWorkflowTargetPolicy.SelectedOnly,
            ChartQuickCommandCatalog.ChartTitleFontSize);

        plan.CanExecute.Should().BeTrue();
        plan.Command!.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();
        chart.ChartTitleFontSize.Should().NotBe(initialSize);
    }

    [Fact]
    public void CommandFactories_ApplyChartEditsWithoutRendererOwnedConstruction()
    {
        var (workbook, sheet, chart) = NewChartWorkbook(ChartType.Column);
        var context = new TestCommandContext(workbook);
        var newRange = Range(sheet.Id, 1, 1, 5, 4);

        ChartCommandWorkflowPlanner.BuildChangeTypeCommand(sheet.Id, chart, ChartType.Line)
            .Apply(context).Success.Should().BeTrue();
        ChartCommandWorkflowPlanner.BuildChangeSourceCommand(
                sheet.Id,
                chart,
                newRange,
                firstColumnIsCategories: true,
                switchRowColumn: true)
            .Apply(context).Success.Should().BeTrue();
        ChartCommandWorkflowPlanner.BuildStyleCommand(sheet.Id, chart, 17)
            .Apply(context).Success.Should().BeTrue();
        ChartCommandWorkflowPlanner.BuildBoundsCommand(sheet.Id, chart, 12, 18, 320, 180)
            .Apply(context).Success.Should().BeTrue();

        chart.Type.Should().Be(ChartType.Line);
        chart.DataRange.Should().Be(newRange);
        chart.SeriesInRows.Should().BeTrue();
        chart.ChartStyleId.Should().Be(17);
        (chart.Left, chart.Top, chart.Width, chart.Height).Should().Be((12, 18, 320, 180));
    }

    [Fact]
    public void PlanMoveCommand_ResolvesExistingAndNewSheetTransitions()
    {
        var (workbook, source, chart) = NewChartWorkbook(ChartType.Column);
        var target = workbook.AddSheet("Target");

        var existing = ChartCommandWorkflowPlanner.PlanMoveCommand(
            workbook,
            source.Id,
            chart,
            new ChartMoveInput(ChartMoveTargetKind.ObjectInSheet, " Target "));
        existing.CanExecute.Should().BeTrue();
        existing.Command.Should().BeOfType<MoveChartCommand>();
        existing.ExistingTargetSheetId.Should().Be(target.Id);
        existing.TargetName.Should().Be("Target");

        var newSheet = ChartCommandWorkflowPlanner.PlanMoveCommand(
            workbook,
            source.Id,
            chart,
            new ChartMoveInput(ChartMoveTargetKind.NewSheet, "Chart Sheet"));
        newSheet.CanExecute.Should().BeTrue();
        newSheet.Command.Should().BeOfType<MoveChartToNewSheetCommand>();
        newSheet.ExistingTargetSheetId.Should().BeNull();

        var missing = ChartCommandWorkflowPlanner.PlanMoveCommand(
            workbook,
            source.Id,
            chart,
            new ChartMoveInput(ChartMoveTargetKind.ObjectInSheet, "Missing"));
        missing.CanExecute.Should().BeFalse();
        missing.Error.Should().Contain("Missing");
    }

    [Fact]
    public void LiveHosts_DelegateChartCommandConstructionToSharedWorkflow()
    {
        var srcRoot = RepositoryFileLocator.FindDirectory("src");
        var sources = new[] { "FreeX.App.Host", "FreeX.App.Avalonia" }
            .SelectMany(project => Directory.EnumerateFiles(
                Path.Combine(srcRoot, project),
                "MainWindow*.cs",
                SearchOption.AllDirectories))
            .Where(path => !Path.GetFileName(path).StartsWith("MainWindow.ScreenshotTour", StringComparison.Ordinal))
            .Select(File.ReadAllText)
            .ToArray();
        var combined = string.Join(Environment.NewLine, sources);

        combined.Should().Contain("ChartCommandWorkflowPlanner");
        combined.Should().NotContain("ChartQuickCommandPlanner.CanApply(");
        combined.Should().NotContain("ChartQuickCommandPlanner.Plan(");
        foreach (var commandType in new[]
                 {
                     "ChangeChartTypeCommand",
                     "ChangeChartSourceCommand",
                     "MoveChartCommand",
                     "MoveChartToNewSheetCommand",
                     "SetChartLayoutCommand",
                     "SetChartBoundsCommand",
                     "SetChartStyleCommand",
                 })
        {
            combined.Should().NotContain($"new {commandType}(");
        }
    }

    [Fact]
    public void WorkflowPlanner_RemainsRendererNeutral()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryFileLocator.FindDirectory("src"),
            "FreeX.App.Presentation",
            "Charts",
            "Editing",
            "ChartCommandWorkflowPlanner.cs"));

        source.Should().NotContain("System.Windows");
        source.Should().NotContain("Avalonia.");
        source.Should().NotContain("FreeX.App.Host");
        source.Should().NotContain("FreeX.App.Avalonia");
    }

    private static (Workbook Workbook, Sheet Sheet, ChartModel Chart) NewChartWorkbook(ChartType type)
    {
        var workbook = new Workbook("Charts");
        var sheet = workbook.AddSheet("Sheet1");
        var chart = new ChartModel
        {
            Type = type,
            DataRange = Range(sheet.Id, 1, 1, 4, 3),
            FirstRowIsHeader = true,
            FirstColIsCategories = true,
            IsVisible = true,
        };
        sheet.Charts.Add(chart);
        return (workbook, sheet, chart);
    }

    private static GridRange Range(
        SheetId sheetId,
        uint startRow,
        uint startCol,
        uint endRow,
        uint endCol) =>
        new(
            new CellAddress(sheetId, startRow, startCol),
            new CellAddress(sheetId, endRow, endCol));
}
