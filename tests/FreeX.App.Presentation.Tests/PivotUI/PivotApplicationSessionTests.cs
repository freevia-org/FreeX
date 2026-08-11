using FluentAssertions;
using FreeX.App.Presentation.PivotUI;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PivotUI;

public sealed class PivotApplicationSessionTests
{
    [Fact]
    public void ResolveTarget_InterpretsStrictAndFallbackSelections()
    {
        var fixture = new Fixture();
        var outside = fixture.Range(20, 20, 20, 20);

        fixture.Session.ResolveTarget(fixture.Sheet.Id, outside)
            .Message!.Issue.Should().Be(PivotApplicationIssue.NoPivotTable);

        fixture.Session.ResolveTarget(
                fixture.Sheet.Id,
                outside,
                PivotTargetFallback.FirstOnSheet)
            .Target!.PivotTable.Should().BeSameAs(fixture.Pivot);
    }

    [Fact]
    public void PrepareCreate_ProjectsSourceFieldsDefaultsAndReferences()
    {
        var fixture = new Fixture();

        var model = fixture.Session.PrepareCreate(fixture.Sheet.Id, fixture.SourceRange);

        model.CanShow.Should().BeTrue();
        model.SourceRangeText.Should().Be("Data!A1:B4");
        model.DestinationRangeText.Should().Be("Data!D1");
        model.Fields.Select(field => field.Header).Should().Equal("Region", "Sales");
        model.DefaultRoles[0].Should().Be(PivotCreatePlanner.FieldRole.Row);
        model.DefaultRoles[1].Should().Be(PivotCreatePlanner.FieldRole.Value);
    }

    [Fact]
    public void PlanCreate_UsesSharedRolesAndBuildsInPlaceCommand()
    {
        var fixture = new Fixture();
        fixture.References["source"] = fixture.SourceRange;
        fixture.References["target"] = fixture.Range(12, 5, 12, 5);
        var roles = new Dictionary<int, PivotCreatePlanner.FieldRole>
        {
            [0] = PivotCreatePlanner.FieldRole.Row,
            [1] = PivotCreatePlanner.FieldRole.Value,
        };

        var plan = fixture.Session.PlanCreate(
            fixture.Sheet.Id,
            new PivotCreateSubmission(
                " source ",
                PivotDestinationKind.ExistingWorksheet,
                "target",
                OpenFieldList: true,
                roles));

        plan.CanApply.Should().BeTrue();
        plan.Command.Should().BeOfType<AddPivotTableCommand>();
        plan.Transition.RefreshFieldList.Should().BeTrue();
        plan.Transition.RefreshViewport.Should().BeTrue();
        plan.StatusArgument.Should().Be("source");
    }

    [Fact]
    public void PlanCreate_RejectsMissingValueAndCrossSheetDestination()
    {
        var fixture = new Fixture();
        fixture.References["source"] = fixture.SourceRange;
        var other = fixture.Workbook.AddSheet("Other");
        fixture.References["other"] = new GridRange(
            new CellAddress(other.Id, 1, 1),
            new CellAddress(other.Id, 1, 1));

        var noValue = fixture.Session.PlanCreate(
            fixture.Sheet.Id,
            new PivotCreateSubmission(
                "source",
                PivotDestinationKind.NewWorksheet,
                null,
                OpenFieldList: false,
                new Dictionary<int, PivotCreatePlanner.FieldRole>
                {
                    [0] = PivotCreatePlanner.FieldRole.Row,
                    [1] = PivotCreatePlanner.FieldRole.Unused,
                }));
        var crossSheet = fixture.Session.PlanCreate(
            fixture.Sheet.Id,
            new PivotCreateSubmission(
                "source",
                PivotDestinationKind.ExistingWorksheet,
                "other",
                OpenFieldList: false));

        noValue.Message!.Issue.Should().Be(PivotApplicationIssue.MissingValueField);
        crossSheet.Message!.Issue.Should().Be(PivotApplicationIssue.DestinationMustBeOnCurrentSheet);
    }

    [Fact]
    public void PlanRename_CentralizesCollisionValidationAndTransitions()
    {
        var fixture = new Fixture();
        var otherSheet = fixture.Workbook.AddSheet("Other");
        otherSheet.PivotTables.Add(new PivotTableModel { Name = "Taken" });

        var duplicate = fixture.Session.PlanRename(fixture.Target, "taken");
        var accepted = fixture.Session.PlanRename(fixture.Target, "  Summary  ");

        duplicate.Message!.Issue.Should().Be(PivotApplicationIssue.DuplicateName);
        accepted.Command.Should().BeOfType<RenamePivotTableCommand>();
        accepted.StatusArgument.Should().Be("Summary");
        accepted.Transition.RefreshFieldList.Should().BeTrue();
        accepted.Transition.RefreshSlicerTimeline.Should().BeTrue();
    }

    [Fact]
    public void PlanMove_ValidatesSheetAndPlansSelectionTransition()
    {
        var fixture = new Fixture();
        fixture.References["move"] = fixture.Range(20, 6, 20, 6);

        var plan = fixture.Session.PlanMove(fixture.Target, "move");

        plan.Command.Should().BeOfType<MovePivotTableCommand>();
        plan.Transition.SelectionRange.Should().Be(fixture.Range(20, 6, 24, 9));
        plan.Transition.EnsureVisible.Should().Be(new CellAddress(fixture.Sheet.Id, 20, 6));
        plan.Transition.RefreshFieldList.Should().BeTrue();
    }

    [Fact]
    public void PlanChangeDataSource_ReusesPortableValidation()
    {
        var fixture = new Fixture();
        fixture.References["short"] = fixture.Range(1, 1, 1, 2);
        fixture.References["valid"] = fixture.Range(1, 1, 3, 2);

        fixture.Session.PlanChangeDataSource(fixture.Target, "short")
            .Message!.Issue.Should().Be(PivotApplicationIssue.InvalidDataSource);
        fixture.Session.PlanChangeDataSource(fixture.Target, "valid")
            .Command.Should().BeOfType<ChangePivotTableSourceCommand>();
    }

    [Fact]
    public void PlanSelect_IsDisplayOnlyAndDoesNotInvokeExecutor()
    {
        var fixture = new Fixture();

        var outcome = fixture.Session.Execute(fixture.Session.PlanSelect(fixture.Target));

        outcome.Success.Should().BeTrue();
        outcome.Executed.Should().BeFalse();
        fixture.ExecutedCommands.Should().BeEmpty();
        outcome.Transition.SelectionRange.Should().Be(fixture.Pivot.TargetRange);
    }

    [Fact]
    public void Execute_MapsCommandFailureToNeutralMessage()
    {
        var fixture = new Fixture
        {
            NextExecution = new PivotCommandExecutionResult(false, "Core rejected the refresh."),
        };

        var outcome = fixture.Session.Execute(fixture.Session.PlanRefresh(fixture.Target));

        outcome.Success.Should().BeFalse();
        outcome.Executed.Should().BeTrue();
        outcome.Message.Should().Be(new PivotMessageModel(
            PivotApplicationIssue.CommandFailed,
            PivotMessageSeverity.Error,
            "Core rejected the refresh."));
    }

    [Fact]
    public void Execute_ShowDetailsActivatesAffectedSheet()
    {
        var fixture = new Fixture();
        var details = fixture.Workbook.AddSheet("Details");
        fixture.NextExecution = new PivotCommandExecutionResult(
            true,
            AffectedCells: [new CellAddress(details.Id, 1, 1)]);
        var selected = fixture.Range(7, 2, 7, 2);

        var outcome = fixture.Session.Execute(
            fixture.Session.PlanShowDetails(fixture.Sheet.Id, selected));

        outcome.Success.Should().BeTrue();
        outcome.Transition.ActivateSheetId.Should().Be(details.Id);
        outcome.Transition.RefreshSheetTabs.Should().BeTrue();
    }

    [Fact]
    public void PlanLayout_RejectsAValuesLessLayout()
    {
        var fixture = new Fixture();
        var plan = fixture.Session.PlanLayout(
            fixture.Target,
            new PivotFieldAreas([], [], [], []));

        plan.Message!.Issue.Should().Be(PivotApplicationIssue.MissingValueField);
        plan.Command.Should().BeNull();
    }

    [Fact]
    public void FieldFilterPlans_OwnSelectionAndClearPolicy()
    {
        var fixture = new Fixture();
        fixture.Pivot.LabelFilters.Add(new PivotLabelFilterModel(0, PivotLabelFilterKind.Contains, "E"));
        fixture.Pivot.ValueFilters.Add(new PivotValueFilterModel(
            0,
            PivotValueFilterKind.GreaterThan,
            ComparisonValue: 5,
            SourceFieldIndex: 0));

        var select = fixture.Session.PlanFieldItemSelection(
            fixture.Target,
            PivotHeaderArea.Row,
            sourceFieldIndex: 0,
            ["East"]);

        select.Action.Should().Be(PivotApplicationAction.ConfigureFilters);
        select.Command.Should().BeOfType<ConfigurePivotTableFieldFiltersCommand>();
        select.Command!.Apply(new TestCommandContext(fixture.Workbook)).Success.Should().BeTrue();
        fixture.Pivot.RowFields[0].SelectedItems.Should().Equal("East");

        var clear = fixture.Session.PlanClearFieldFilters(
            fixture.Target,
            PivotHeaderArea.Row,
            sourceFieldIndex: 0);
        clear.Command!.Apply(new TestCommandContext(fixture.Workbook)).Success.Should().BeTrue();

        fixture.Pivot.RowFields[0].SelectedItems.Should().BeNull();
        fixture.Pivot.LabelFilters.Should().BeEmpty();
        fixture.Pivot.ValueFilters.Should().BeEmpty();
    }

    [Fact]
    public void SourceReads_UseTheResolvedPivotTargetAndRejectInvalidFieldIndexes()
    {
        var fixture = new Fixture();

        fixture.Session.ReadSourceHeaders(fixture.Target).Should().Equal("Region", "Sales");
        fixture.Session.ReadSourceItems(fixture.Target, sourceFieldIndex: 0).Should().Equal("(blank)", "East");
        fixture.Session.ReadSourceItems(fixture.Target, sourceFieldIndex: -1).Should().BeEmpty();
    }

    [Fact]
    public void FieldViewAndCalculatedPlans_CentralizeCoreCommandConstruction()
    {
        var fixture = new Fixture();
        var sort = new PivotSortModel(
            PivotSortTarget.Label,
            PivotSortDirection.Descending,
            FieldIndex: 0);

        var sortPlan = fixture.Session.PlanFieldSort(fixture.Target, sort);
        var calculatedPlan = fixture.Session.PlanCalculatedConfiguration(
            fixture.Target,
            fixture.Pivot.RowFields.ToList(),
            fixture.Pivot.ColumnFields.ToList(),
            fixture.Pivot.PageFields.ToList(),
            [new PivotCalculatedFieldModel("Margin", "=1")],
            []);

        sortPlan.Command.Should().BeOfType<ConfigurePivotTableViewCommand>();
        calculatedPlan.Action.Should().Be(PivotApplicationAction.ConfigureCalculations);
        calculatedPlan.Command.Should().BeOfType<ConfigurePivotTableCalculatedItemsCommand>();

        sortPlan.Command!.Apply(new TestCommandContext(fixture.Workbook)).Success.Should().BeTrue();
        calculatedPlan.Command!.Apply(new TestCommandContext(fixture.Workbook)).Success.Should().BeTrue();
        fixture.Pivot.Sorts.Should().ContainSingle().Which.Should().Be(sort);
        fixture.Pivot.CalculatedFields.Should().ContainSingle().Which.Name.Should().Be("Margin");
    }

    [Fact]
    public void OptionPlans_OwnDesignAndFullDialogCommandFactories()
    {
        var fixture = new Fixture();
        var designValues = PivotOptionsPlanner.CaptureDesignValues(fixture.Pivot) with
        {
            ShowRowHeaders = false,
            StyleName = "PivotStyleMedium2",
        };
        var dialogValues = PivotOptionsPlanner.CaptureDialogValues(fixture.Pivot) with
        {
            ShowColumnHeaders = false,
            EmptyValueText = "-",
        };

        var designPlan = fixture.Session.PlanDesignOptions(fixture.Target, designValues);
        var dialogPlan = fixture.Session.PlanDialogOptions(fixture.Target, dialogValues);

        designPlan.Action.Should().Be(PivotApplicationAction.ConfigureOptions);
        designPlan.Command.Should().BeOfType<ConfigurePivotTableOptionsCommand>();
        dialogPlan.Command.Should().BeOfType<ConfigurePivotTableOptionsCommand>();

        designPlan.Command!.Apply(new TestCommandContext(fixture.Workbook)).Success.Should().BeTrue();
        fixture.Pivot.ShowRowHeaders.Should().BeFalse();
        fixture.Pivot.StyleName.Should().Be("PivotStyleMedium2");
    }

    [Fact]
    public void SlicerPlans_OwnGesturesInsertionAndRefreshTransitions()
    {
        var fixture = new Fixture();
        var slicer = new SlicerModel
        {
            Name = "Region Slicer",
            SourcePivotTableName = fixture.Pivot.Name,
            SourceFieldName = "Region",
        };
        fixture.Workbook.Slicers.Add(slicer);

        var selectionPlan = fixture.Session.PlanSlicerSelection(
            slicer,
            "East",
            SlicerSelectionGesture.Replace);
        var insertPlan = fixture.Session.PlanInsertSlicer(
            fixture.Target,
            "Second Slicer",
            "Region");

        selectionPlan.Action.Should().Be(PivotApplicationAction.ConfigureSlicer);
        selectionPlan.Transition.RefreshSlicerTimeline.Should().BeTrue();
        selectionPlan.Command!.Apply(new TestCommandContext(fixture.Workbook)).Success.Should().BeTrue();
        slicer.SelectedItems.Should().Equal("East");

        insertPlan.Action.Should().Be(PivotApplicationAction.InsertSlicer);
        insertPlan.Command.Should().BeOfType<AddSlicerCommand>();
        insertPlan.Transition.RefreshFieldList.Should().BeFalse();
        insertPlan.Transition.RefreshSlicerTimeline.Should().BeTrue();
        insertPlan.Transition.RefreshViewport.Should().BeTrue();
    }

    [Fact]
    public void TimelinePlans_NormalizeDatesAndOwnGranularityRouting()
    {
        var fixture = new Fixture();
        var timeline = new TimelineModel
        {
            Name = "Date Timeline",
            SourcePivotTableName = fixture.Pivot.Name,
            SourceFieldName = "Region",
            Level = 2,
        };
        fixture.Workbook.Timelines.Add(timeline);

        var rangePlan = fixture.Session.PlanTimelineRange(
            timeline,
            " 2026-01-01 ",
            " ");
        var granularityPlan = fixture.Session.PlanCycleTimelineGranularity("date timeline");

        var rangeCommand = rangePlan.Command.Should().BeOfType<SetTimelineRangeCommand>().Which;
        rangeCommand.SelectedStartDate.Should().Be("2026-01-01");
        rangeCommand.SelectedEndDate.Should().BeNull();
        rangePlan.Action.Should().Be(PivotApplicationAction.ConfigureTimeline);
        granularityPlan.Should().NotBeNull();
        granularityPlan!.Command.Should().BeOfType<SetTimelineGranularityCommand>();
    }

    private sealed class Fixture
    {
        public Fixture()
        {
            Workbook = new Workbook("Book");
            Sheet = Workbook.AddSheet("Data");
            SourceRange = Range(1, 1, 4, 2);
            Sheet.SetCell(new CellAddress(Sheet.Id, 1, 1), new TextValue("Region"));
            Sheet.SetCell(new CellAddress(Sheet.Id, 1, 2), new TextValue("Sales"));
            Sheet.SetCell(new CellAddress(Sheet.Id, 2, 1), new TextValue("East"));
            Sheet.SetCell(new CellAddress(Sheet.Id, 2, 2), new NumberValue(10));

            Pivot = new PivotTableModel
            {
                Name = "SalesPivot",
                SourceRange = SourceRange,
                TargetRange = Range(6, 1, 10, 4),
            };
            Pivot.RowFields.Add(new PivotFieldModel(0));
            Pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Sales", "sum"));
            Sheet.PivotTables.Add(Pivot);
            Target = new PivotApplicationTarget(Sheet, Pivot);

            Session = new PivotApplicationSession(Workbook, Resolve, Execute);
        }

        public Workbook Workbook { get; }
        public Sheet Sheet { get; }
        public GridRange SourceRange { get; }
        public PivotTableModel Pivot { get; }
        public PivotApplicationTarget Target { get; }
        public Dictionary<string, GridRange> References { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<IWorkbookCommand> ExecutedCommands { get; } = [];
        public PivotCommandExecutionResult NextExecution { get; set; } = new(true);
        public PivotApplicationSession Session { get; }

        public GridRange Range(uint startRow, uint startCol, uint endRow, uint endCol) =>
            new(
                new CellAddress(Sheet.Id, startRow, startCol),
                new CellAddress(Sheet.Id, endRow, endCol));

        private bool Resolve(SheetId defaultSheetId, string reference, out GridRange range) =>
            References.TryGetValue(reference, out range);

        private PivotCommandExecutionResult Execute(IWorkbookCommand command, string commandLabel)
        {
            ExecutedCommands.Add(command);
            return NextExecution;
        }
    }

    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }
}
