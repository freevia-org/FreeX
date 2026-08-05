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
    public void InsertionAndOptionsFactories_PreserveChartCommandSemantics()
    {
        var workbook = new Workbook("Chart workflow");
        var sheet = workbook.AddSheet("Sheet1");
        var context = new TestCommandContext(workbook);
        var sourceRange = Range(sheet.Id, 1, 1, 4, 3);

        var insertion = ChartCommandWorkflowPlanner.BuildEmbeddedChartCommand(
            sheet,
            sourceRange,
            ChartType.Column,
            "Sales");
        insertion.Apply(context).Success.Should().BeTrue();

        var chart = sheet.Charts.Should().ContainSingle().Subject;
        chart.Id.Should().Be(insertion.ChartId);
        chart.DataRange.Should().Be(sourceRange);
        chart.Title.Should().Be("Sales");

        ChartCommandWorkflowPlanner.BuildHiddenEmptyCellsCommand(
                sheet.Id,
                chart,
                ChartBlankDisplayMode.Zero,
                showDataInHiddenRowsAndColumns: true)
            .Apply(context).Success.Should().BeTrue();
        chart.BlankDisplayMode.Should().Be(ChartBlankDisplayMode.Zero);
        chart.ShowDataInHiddenRowsAndColumns.Should().BeTrue();

        chart.IsPivotChart = true;
        chart.PivotTableName = "PivotTable1";
        ChartCommandWorkflowPlanner.BuildChangePivotChartTypeCommand(sheet.Id, chart, ChartType.Line)
            .Apply(context).Success.Should().BeTrue();
        ChartCommandWorkflowPlanner.BuildPivotChartOptionsCommand(
                sheet.Id,
                chart,
                PivotChartOptionsPlanner.CreateResult(
                    chartStyleId: 17,
                    showFieldButtons: false,
                    showReportFilterButtons: false,
                    showAxisFieldButtons: true,
                    showValueFieldButtons: false,
                    showDataTable: true,
                    showDataTableLegendKeys: true,
                    roundedCorners: true,
                    showHiddenData: false,
                    blankDisplayMode: ChartBlankDisplayMode.Span))
            .Apply(context).Success.Should().BeTrue();

        chart.Type.Should().Be(ChartType.Line);
        chart.ChartStyleId.Should().Be(17);
        chart.ShowPivotChartFieldButtons.Should().BeFalse();
        chart.ShowPivotChartReportFilterButtons.Should().BeFalse();
        chart.ShowPivotChartAxisFieldButtons.Should().BeTrue();
        chart.ShowPivotChartValueFieldButtons.Should().BeFalse();
        chart.DataTable.Should().NotBeNull();
        chart.DataTable!.ShowLegendKeys.Should().BeTrue();
        chart.RoundedCorners.Should().BeTrue();
        chart.ShowDataInHiddenRowsAndColumns.Should().BeFalse();
        chart.BlankDisplayMode.Should().Be(ChartBlankDisplayMode.Span);
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
                "*.cs",
                SearchOption.AllDirectories))
            .Where(path => !IsChartEvidenceHarness(Path.GetFileName(path)))
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
                     "AddPivotChartCommand",
                     "ChangePivotChartTypeCommand",
                     "ConfigurePivotChartOptionsCommand",
                     "ConfigureChartHiddenEmptyCellsCommand",
                     "RemoveChartSeriesCommand",
                 })
        {
            combined.Should().NotContain($"new {commandType}(");
        }
    }

    [Fact]
    public void ProductionRenderers_DoNotConstructAddChartCommandsDirectly()
    {
        var srcRoot = RepositoryFileLocator.FindDirectory("src");
        var offenders = new[] { "FreeX.App.Host", "FreeX.App.Avalonia" }
            .SelectMany(project => Directory.EnumerateFiles(
                Path.Combine(srcRoot, project),
                "*.cs",
                SearchOption.AllDirectories))
            .Where(path => !IsChartEvidenceHarness(Path.GetFileName(path)))
            .Where(path => System.Text.RegularExpressions.Regex.IsMatch(
                File.ReadAllText(path),
                @"\bnew\s+AddChartCommand\s*\("))
            .Select(path => Path.GetRelativePath(srcRoot, path))
            .ToArray();

        offenders.Should().BeEmpty(
            "live WPF and Avalonia chart insertion must be owned by ChartCommandWorkflowPlanner");
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

    private static bool IsChartEvidenceHarness(string fileName) =>
        fileName.StartsWith("MainWindow.ScreenshotTour", StringComparison.Ordinal) ||
        fileName is "MainWindow.ParityCapture.cs" or
            "MainWindow.RibbonInteractionValidation.cs" or
            "MainWindow.ContextMenuInteractionValidation.cs";
}
