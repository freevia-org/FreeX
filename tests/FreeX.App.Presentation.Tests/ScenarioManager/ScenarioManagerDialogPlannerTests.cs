using FluentAssertions;
using FreeX.App.Presentation.ScenarioManager;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.ScenarioManager;

public sealed class ScenarioManagerDialogPlannerTests
{
    [Fact]
    public void BuildItems_ProjectsScenarioFieldsAndFormattedChangingCells()
    {
        var workbook = CreateWorkbook(out var sheet);
        // A contiguous single-column block must still collapse to one readable range.
        var first = new CellAddress(sheet.Id, 2, 2);
        var second = new CellAddress(sheet.Id, 3, 2);
        var third = new CellAddress(sheet.Id, 4, 2);
        workbook.Scenarios.Add(new WorkbookScenario(
            "Best Case",
            [
                new ScenarioCellValue(first, new NumberValue(10)),
                new ScenarioCellValue(second, new NumberValue(15)),
                new ScenarioCellValue(third, new NumberValue(20))
            ],
            "Revenue lift",
            Hidden: true,
            Locked: true));

        var item = ScenarioManagerDialogPlanner.BuildItems(workbook).Single();

        item.Name.Should().Be("Best Case");
        item.ChangingCellsText.Should().Be("B2:B4");
        item.Comment.Should().Be("Revenue lift");
        item.Hidden.Should().BeTrue();
        item.Locked.Should().BeTrue();
    }

    [Fact]
    public void BuildItems_DoesNotAbsorbExtraCellsForNonContiguousChangingCells()
    {
        // Regression for P15: B2 and C4 do NOT form a contiguous block, so formatting them as
        // the bounding rectangle "B2:C4" would silently include B3, B4, C2 and C3 - cells that
        // were never part of the scenario - the next time the dialog recaptures changing cells.
        var workbook = CreateWorkbook(out var sheet);
        var first = new CellAddress(sheet.Id, 2, 2);
        var second = new CellAddress(sheet.Id, 4, 3);
        workbook.Scenarios.Add(new WorkbookScenario(
            "Best Case",
            [
                new ScenarioCellValue(first, new NumberValue(10)),
                new ScenarioCellValue(second, new NumberValue(20))
            ],
            "Revenue lift",
            Hidden: true,
            Locked: true));

        var item = ScenarioManagerDialogPlanner.BuildItems(workbook).Single();

        item.ChangingCellsText.Should().Be("B2:B2,C4:C4");
        WorkbookRangeTextCodec.TryParseMany(sheet.Id, item.ChangingCellsText, _ => sheet.Id, out var ranges)
            .Should().BeTrue();
        ranges.SelectMany(range => range.AllCells()).Should().BeEquivalentTo([first, second]);
    }

    [Fact]
    public void FormatChangingCells_QualifiesEachSheetInsteadOfGoingBlankForMixedSheetScenarios()
    {
        // Regression for P15: a blank projection here means the WPF Edit round-trip falls
        // through to whatever is currently selected in the grid, silently replacing the
        // scenario's real (cross-sheet) changing cells. The exact per-sheet cell set must
        // survive the round-trip instead.
        var workbook = CreateWorkbook(out var firstSheet);
        var secondSheet = workbook.AddSheet("Sheet2");
        var scenario = new WorkbookScenario(
            "Mixed",
            [
                new ScenarioCellValue(new CellAddress(firstSheet.Id, 1, 1), new NumberValue(1)),
                new ScenarioCellValue(new CellAddress(secondSheet.Id, 1, 1), new NumberValue(2))
            ]);

        var text = ScenarioManagerDialogPlanner.FormatChangingCells(workbook, scenario);

        text.Should().Be("A1:A1,Sheet2!A1:A1");
        WorkbookRangeTextCodec.TryParseMany(
                firstSheet.Id,
                text,
                name => name == secondSheet.Name ? secondSheet.Id : (name == firstSheet.Name ? firstSheet.Id : null),
                out var roundTripped)
            .Should().BeTrue();
        roundTripped.SelectMany(range => range.AllCells()).Should().BeEquivalentTo(
        [
            new CellAddress(firstSheet.Id, 1, 1),
            new CellAddress(secondSheet.Id, 1, 1)
        ]);
    }

    [Theory]
    [InlineData(ScenarioManagerDialogAction.Add, true)]
    [InlineData(ScenarioManagerDialogAction.Edit, true)]
    [InlineData(ScenarioManagerDialogAction.Save, true)]
    [InlineData(ScenarioManagerDialogAction.Show, false)]
    [InlineData(ScenarioManagerDialogAction.Delete, false)]
    [InlineData(ScenarioManagerDialogAction.List, false)]
    [InlineData(ScenarioManagerDialogAction.Report, false)]
    public void RequiresScenarioName_OnlyRequiresNamesForSaveActions(
        ScenarioManagerDialogAction action,
        bool expected)
    {
        ScenarioManagerDialogPlanner.RequiresScenarioName(action).Should().Be(expected);
    }

    [Fact]
    public void ValidateAcceptRequest_ReportsTheFailingFieldAndPortableError()
    {
        var workbook = CreateWorkbook(out var sheet);
        SheetId? ResolveSheet(string name) => name == sheet.Name ? sheet.Id : null;

        ScenarioManagerDialogPlanner.ValidateAcceptRequest(
                ScenarioManagerDialogAction.Add,
                " ",
                "A1",
                "",
                sheet.Id,
                ResolveSheet)
            .Should()
            .Be(new ScenarioManagerDialogValidationFailure(
                ScenarioManagerDialogValidationError.EnterScenarioName,
                ScenarioManagerDialogValidationField.ScenarioName));

        ScenarioManagerDialogPlanner.ValidateAcceptRequest(
                ScenarioManagerDialogAction.Add,
                "Scenario 1",
                "not a range",
                "",
                sheet.Id,
                ResolveSheet)
            .Should()
            .Be(new ScenarioManagerDialogValidationFailure(
                ScenarioManagerDialogValidationError.EnterValidChangingCellsReference,
                ScenarioManagerDialogValidationField.ChangingCells));

        ScenarioManagerDialogPlanner.ValidateAcceptRequest(
                ScenarioManagerDialogAction.Report,
                "",
                "",
                "not a range",
                sheet.Id,
                ResolveSheet)
            .Should()
            .Be(new ScenarioManagerDialogValidationFailure(
                ScenarioManagerDialogValidationError.EnterValidResultCellsReference,
                ScenarioManagerDialogValidationField.ResultCells));

        ScenarioManagerDialogPlanner.ValidateAcceptRequest(
                ScenarioManagerDialogAction.Report,
                "",
                "",
                "A1,Sheet1!B2:C2",
                sheet.Id,
                ResolveSheet)
            .Should()
            .BeNull();
    }

    [Fact]
    public void ValidateAcceptRequest_AcceptsMultiAreaCrossSheetChangingCells()
    {
        var workbook = CreateWorkbook(out var firstSheet);
        var secondSheet = workbook.AddSheet("Sheet2");
        SheetId? ResolveSheet(string name) => name switch
        {
            "Sheet1" => firstSheet.Id,
            "Sheet2" => secondSheet.Id,
            _ => null,
        };

        ScenarioManagerDialogPlanner.ValidateAcceptRequest(
                ScenarioManagerDialogAction.Edit,
                "Upside",
                "A1:B2,Sheet2!C3:C4,Sheet1!E5",
                "",
                firstSheet.Id,
                ResolveSheet)
            .Should()
            .BeNull();
    }

    [Fact]
    public void ProjectSelectionFields_UsesSelectedScenarioOrDefaultBlankState()
    {
        var selected = new ScenarioManagerDialogItem(
            "Best Case",
            [],
            "Revenue lift",
            "B2:C4",
            Hidden: true,
            Locked: true);

        var selectedFields = ScenarioManagerDialogPlanner.ProjectSelectionFields(
            selected,
            currentScenarioNameText: "",
            defaultScenarioName: "Scenario 2");
        var defaultFields = ScenarioManagerDialogPlanner.ProjectSelectionFields(
            selected: null,
            currentScenarioNameText: " ",
            defaultScenarioName: "Scenario 1");
        var preserveTypedFields = ScenarioManagerDialogPlanner.ProjectSelectionFields(
            selected: null,
            currentScenarioNameText: "Draft",
            defaultScenarioName: "Scenario 1");

        selectedFields.Should().Be(new ScenarioManagerDialogSelectionFields(
            "Best Case",
            "B2:C4",
            "",
            "Revenue lift",
            Locked: true,
            Hidden: true));
        defaultFields.Should().Be(new ScenarioManagerDialogSelectionFields(
            "Scenario 1",
            "",
            "",
            "",
            Locked: true,
            Hidden: false));
        preserveTypedFields.Should().BeNull();
    }

    [Fact]
    public void ProjectAcceptResult_CapturesSelectedAndEditedFieldValues()
    {
        var selected = new ScenarioManagerDialogItem(
            "Best Case",
            [],
            null,
            "B2",
            Hidden: false,
            Locked: false);

        var result = ScenarioManagerDialogPlanner.ProjectAcceptResult(
            ScenarioManagerDialogAction.Edit,
            selected,
            newScenarioName: "Better Case",
            changingCellsText: "C3",
            resultCellsText: "D4",
            commentText: "Updated",
            locked: true,
            hidden: true);

        result.Should().Be(new ScenarioManagerDialogAcceptResult(
            ScenarioManagerDialogAction.Edit,
            "Best Case",
            "Better Case",
            "C3",
            "D4",
            "Updated",
            Locked: true,
            Hidden: true));
    }

    [Fact]
    public void ScenarioManagerDialogPlanning_IsPortableAndHostUsesItAsAdapter()
    {
        var presentationRoot = RepositoryFileLocator.FindDirectory("src", "FreeX.App.Presentation");
        var repoRoot = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var plannerSource = File.ReadAllText(Path.Combine(
            presentationRoot,
            "ScenarioManager",
            "ScenarioManagerDialogPlanner.cs"));
        var hostPlanningSource = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "FreeX.App.Host",
            "ScenarioManagerDialog.Planning.cs"));
        var hostDialogSource = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "FreeX.App.Host",
            "ScenarioManagerDialog.cs"));
        var hostSource = hostPlanningSource + hostDialogSource;

        plannerSource.Should().Contain("public sealed record ScenarioManagerDialogItem");
        plannerSource.Should().Contain("public sealed record ScenarioManagerDialogAcceptResult");
        plannerSource.Should().Contain("WorkbookRangeTextCodec.TryParse");
        plannerSource.Should().Contain("WorkbookRangeTextCodec.TryParseMany");
        plannerSource.Should().NotContain("UiText");
        plannerSource.Should().NotContain("System.Windows");
        plannerSource.Should().NotContain("Avalonia");
        plannerSource.Should().NotContain("FreeX.App.Host");

        hostPlanningSource.Should().Contain("public static IReadOnlyList<ScenarioManagerDialogItem> BuildScenarioItems");
        hostSource.Should().Contain("SharedScenarioManagerDialogPlanner.BuildItems");
        hostSource.Should().Contain("SharedScenarioManagerDialogPlanner.ValidateAcceptRequest");
        hostSource.Should().Contain("SharedScenarioManagerDialogPlanner.ProjectSelectionFields");
        hostSource.Should().Contain("SharedScenarioManagerDialogPlanner.ProjectAcceptResult");
        hostSource.Should().Contain("LocalizeValidationError");
        hostSource.Should().Contain("ScenarioManagerDialogSelectionFields");
        hostSource.Should().Contain("ScenarioManagerDialogAcceptResult");
        hostSource.Should().Contain("ScenarioManagerDialogValidationField");
        hostSource.Should().NotContain("ScenarioManagerItem");
        hostSource.Should().NotContain("ScenarioManagerSelectionFields");
        hostSource.Should().NotContain("ScenarioManagerAcceptResult");
        hostSource.Should().NotContain("ScenarioManagerValidationField");
        hostSource.Should().NotContain("ToHostItem");
        hostSource.Should().NotContain("ToPlannerItem");
        hostSource.Should().NotContain("ToHostSelectionFields");
        hostSource.Should().NotContain("ToHostAcceptResult");
        hostSource.Should().NotContain("ToHostValidationField");
        hostSource.Should().NotContain("WorkbookRangeTextCodec.TryParse");
        hostSource.Should().NotContain("WorkbookRangeTextCodec.TryParseMany");
        hostSource.Should().NotContain("new GridRange");
        hostSource.Should().NotContain("scenario.ChangingCells.Min");
    }

    private static Workbook CreateWorkbook(out Sheet sheet)
    {
        var workbook = new Workbook("Budget");
        sheet = workbook.AddSheet("Sheet1");
        return workbook;
    }
}
