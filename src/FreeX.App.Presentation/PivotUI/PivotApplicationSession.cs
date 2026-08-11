using FreeX.Core.Commands;
using FreeX.Core.Model;
using FreeX.App.Presentation.Localization;

namespace FreeX.App.Presentation.PivotUI;

public enum PivotTargetFallback
{
    SelectionOnly,
    FirstOnSheet
}

public enum PivotDestinationKind
{
    NewWorksheet,
    ExistingWorksheet
}

public enum PivotApplicationAction
{
    None,
    Create,
    Refresh,
    Rename,
    Move,
    ChangeDataSource,
    Clear,
    Select,
    ShowDetails,
    ConfigureLayout,
    ConfigureView,
    ConfigureFilters,
    ConfigureCalculations,
    ConfigureOptions,
    InsertSlicer,
    InsertTimeline,
    ConfigureSlicer,
    ConfigureTimeline
}

public enum PivotApplicationIssue
{
    None,
    NoPivotTable,
    MissingSource,
    MinimumSourceShape,
    MissingSourceHeaders,
    InvalidSourceReference,
    MissingValueField,
    InvalidDestinationReference,
    DestinationMustBeOnCurrentSheet,
    DestinationOutOfBounds,
    EmptyName,
    DuplicateName,
    InvalidDataSource,
    CommandFailed
}

public enum PivotMessageSeverity
{
    Information,
    Warning,
    Error
}

public sealed record PivotMessageModel(
    PivotApplicationIssue Issue,
    PivotMessageSeverity Severity,
    string? Detail = null);

public enum PivotMessageTextProfile
{
    Wpf,
    Avalonia
}

public static class PivotApplicationMessagePlanner
{
    public static LocalizedTextDescriptor DescribeIssue(
        PivotMessageModel message,
        PivotMessageTextProfile profile) =>
        profile == PivotMessageTextProfile.Wpf
            ? DescribeWpfIssue(message)
            : DescribeAvaloniaIssue(message);

    public static LocalizedTextDescriptor DescribeSuccess(PivotApplicationOutcome outcome) =>
        outcome.Action switch
        {
            PivotApplicationAction.Create => WithStatus("PivotLoc_InsertedPivotTableFrom", outcome),
            PivotApplicationAction.Refresh => WithStatus("PivotLoc_RefreshedPivot", outcome),
            PivotApplicationAction.Rename => WithStatus("PivotName_Renamed", outcome),
            PivotApplicationAction.Move => WithStatus("MovePivot_Moved", outcome),
            PivotApplicationAction.ChangeDataSource => WithStatus("PivotDataSource_Changed", outcome),
            PivotApplicationAction.Clear => WithStatus("PivotAnalyze_Cleared", outcome),
            PivotApplicationAction.Select => WithStatus("PivotAnalyze_Selected", outcome),
            PivotApplicationAction.ShowDetails => WithStatus("PivotAnalyze_ShowDetailsDone", outcome),
            _ => LocalizedTextDescriptor.Literal(outcome.StatusArgument ?? string.Empty)
        };

    private static LocalizedTextDescriptor DescribeWpfIssue(PivotMessageModel message) =>
        message.Issue switch
        {
            PivotApplicationIssue.MissingSource => Resource("MainWindowMessage_PivotTableSelectSourceRange"),
            PivotApplicationIssue.MinimumSourceShape => Resource("MainWindowMessage_PivotTableSourceMinimumShape"),
            PivotApplicationIssue.MissingSourceHeaders or PivotApplicationIssue.InvalidSourceReference => Resource("MainWindowMessage_PivotTableInvalidSourceRange"),
            PivotApplicationIssue.MissingValueField => Resource("MainWindowMessage_PivotTableRequiresValueField"),
            PivotApplicationIssue.InvalidDestinationReference => Resource("MainWindowMessage_PivotTableInvalidDestinationCell"),
            PivotApplicationIssue.DestinationMustBeOnCurrentSheet => Resource("MainWindowMessage_PivotTableMoveCurrentSheetOnly"),
            PivotApplicationIssue.DestinationOutOfBounds => Resource("MovePivotTable_EnterValidDestination"),
            PivotApplicationIssue.DuplicateName => Resource("MainWindowMessage_PivotTableNameAlreadyExists"),
            PivotApplicationIssue.NoPivotTable => Resource("MainWindowMessage_PivotTableSelectExistingForAnalyzeAction"),
            _ => DetailOr(message, "MainWindowMessage_CommandCouldNotBeCompleted")
        };

    private static LocalizedTextDescriptor DescribeAvaloniaIssue(PivotMessageModel message) =>
        message.Issue switch
        {
            PivotApplicationIssue.MissingSource or
            PivotApplicationIssue.MinimumSourceShape or
            PivotApplicationIssue.MissingSourceHeaders or
            PivotApplicationIssue.InvalidSourceReference => Resource("PivotLoc_SelectRangeForPivot"),
            PivotApplicationIssue.MissingValueField => Resource("PivotLoc_AssignAtLeastOneValue"),
            PivotApplicationIssue.InvalidDestinationReference or
            PivotApplicationIssue.DestinationOutOfBounds => Resource("MovePivot_InvalidDestination"),
            PivotApplicationIssue.DestinationMustBeOnCurrentSheet => Resource("MovePivot_CurrentSheetOnly"),
            PivotApplicationIssue.NoPivotTable => Resource("PivotLoc_SelectCellToChangeLayout"),
            _ => DetailOr(message, "PivotLoc_UpdateFailed")
        };

    private static LocalizedTextDescriptor DetailOr(PivotMessageModel message, string fallbackResourceKey) =>
        message.Detail is { } detail
            ? LocalizedTextDescriptor.Literal(detail)
            : Resource(fallbackResourceKey);

    private static LocalizedTextDescriptor WithStatus(string key, PivotApplicationOutcome outcome) =>
        LocalizedTextDescriptor.Resource(key, outcome.StatusArgument ?? string.Empty);

    private static LocalizedTextDescriptor Resource(string key, string? argument = null) =>
        argument is null
            ? LocalizedTextDescriptor.Resource(key)
            : LocalizedTextDescriptor.Resource(key, argument);
}

public sealed record PivotApplicationTarget(Sheet Sheet, PivotTableModel PivotTable);

public sealed record PivotTargetResolution(
    PivotApplicationTarget? Target,
    PivotMessageModel? Message = null)
{
    public bool IsResolved => Target is not null && Message is null;
}

public sealed record PivotCreateDialogModel(
    GridRange? SourceRange,
    string SourceRangeText,
    string DestinationRangeText,
    IReadOnlyList<PivotCreatePlanner.SourceField> Fields,
    IReadOnlyDictionary<int, PivotCreatePlanner.FieldRole> DefaultRoles,
    PivotMessageModel? Message = null)
{
    public bool CanShow => SourceRange is not null && Message is null;
}

public sealed record PivotCreateSubmission(
    string? SourceRangeText,
    PivotDestinationKind DestinationKind,
    string? DestinationRangeText,
    bool OpenFieldList,
    IReadOnlyDictionary<int, PivotCreatePlanner.FieldRole>? Roles = null);

public sealed record PivotDisplayTransition(
    GridRange? SelectionRange = null,
    CellAddress? EnsureVisible = null,
    SheetId? ActivateSheetId = null,
    bool RefreshFieldList = false,
    bool RefreshSlicerTimeline = false,
    bool RefreshSheetTabs = false,
    bool RefreshViewport = false,
    bool RefreshStatus = false)
{
    public static PivotDisplayTransition None { get; } = new();
}

public sealed record PivotApplicationPlan(
    PivotApplicationAction Action,
    string CommandLabel,
    IWorkbookCommand? Command,
    PivotApplicationTarget? Target,
    PivotDisplayTransition Transition,
    PivotMessageModel? Message = null,
    string? StatusArgument = null,
    bool IsDisplayOnly = false)
{
    public bool CanApply =>
        Message is null &&
        (Command is not null || IsDisplayOnly);
}

public sealed record PivotCommandExecutionResult(
    bool Success,
    string? ErrorMessage = null,
    bool IsNoOp = false,
    IReadOnlyList<CellAddress>? AffectedCells = null);

public sealed record PivotApplicationOutcome(
    PivotApplicationAction Action,
    bool Success,
    bool Executed,
    bool IsNoOp,
    PivotDisplayTransition Transition,
    PivotMessageModel? Message,
    string? StatusArgument = null);

public delegate bool PivotReferenceResolver(SheetId defaultSheetId, string referenceText, out GridRange range);

public delegate PivotCommandExecutionResult PivotCommandExecutor(
    IWorkbookCommand command,
    string commandLabel);

/// <summary>
/// Owns renderer-neutral PivotTable target interpretation, dialog submission validation, Core command
/// composition, execution outcomes, and post-command display transitions. WPF and Avalonia retain native
/// controls, modal lifetime, focus, messages, and visual-tree realization.
/// </summary>
public sealed partial class PivotApplicationSession
{
    private readonly Workbook _workbook;
    private readonly PivotReferenceResolver _resolveReference;
    private readonly PivotCommandExecutor _executeCommand;

    public PivotApplicationSession(
        Workbook workbook,
        PivotReferenceResolver resolveReference,
        PivotCommandExecutor executeCommand)
    {
        _workbook = workbook ?? throw new ArgumentNullException(nameof(workbook));
        _resolveReference = resolveReference ?? throw new ArgumentNullException(nameof(resolveReference));
        _executeCommand = executeCommand ?? throw new ArgumentNullException(nameof(executeCommand));
    }

    public PivotTargetResolution ResolveTarget(
        SheetId sheetId,
        GridRange? selectedRange,
        PivotTargetFallback fallback = PivotTargetFallback.SelectionOnly)
    {
        var sheet = _workbook.GetSheet(sheetId);
        if (sheet is null)
            return MissingTarget();

        var pivotTable = fallback == PivotTargetFallback.FirstOnSheet
            ? PivotUiPlanner.FindPivotTableForSelection(sheet, selectedRange)
            : PivotUiPlanner.FindPivotTableContainingSelection(sheet, selectedRange);
        return pivotTable is null
            ? MissingTarget()
            : new PivotTargetResolution(new PivotApplicationTarget(sheet, pivotTable));
    }

    public PivotCreateDialogModel PrepareCreate(SheetId sheetId, GridRange? selectedRange)
    {
        var sheet = _workbook.GetSheet(sheetId);
        var sourcePlan = PivotCreatePlanner.CreateSourceRangePlan(sheet, selectedRange);
        if (!sourcePlan.IsValid || sourcePlan.SourceRange is not { } sourceRange || sheet is null)
        {
            return new PivotCreateDialogModel(
                sourcePlan.SourceRange,
                string.Empty,
                string.Empty,
                [],
                new Dictionary<int, PivotCreatePlanner.FieldRole>(),
                MessageFor(sourcePlan.Error));
        }

        var fields = PivotCreatePlanner.ReadFields(sheet, sourceRange);
        return new PivotCreateDialogModel(
            sourceRange,
            PivotCreatePlanner.FormatRange(_workbook, sourceRange.Start.Sheet, sourceRange),
            PivotCreatePlanner.FormatDefaultDestination(_workbook, sheetId, sourceRange),
            fields,
            PivotCreatePlanner.DefaultRoles(fields));
    }

    public PivotApplicationPlan PlanCreate(SheetId targetSheetId, PivotCreateSubmission submission)
    {
        ArgumentNullException.ThrowIfNull(submission);

        var sourceText = PivotDataSourcePlanner.NormalizeReferenceText(submission.SourceRangeText);
        if (sourceText.Length == 0 || !_resolveReference(targetSheetId, sourceText, out var sourceRange))
        {
            return Invalid(
                PivotApplicationAction.Create,
                "Insert PivotTable",
                PivotApplicationIssue.InvalidSourceReference,
                PivotMessageSeverity.Warning);
        }

        var sourceSheet = _workbook.GetSheet(sourceRange.Start.Sheet);
        if (sourceSheet is null || !PivotCreatePlanner.IsValidSource(sourceRange))
        {
            return Invalid(
                PivotApplicationAction.Create,
                "Insert PivotTable",
                PivotApplicationIssue.MinimumSourceShape,
                PivotMessageSeverity.Information);
        }

        var fields = PivotCreatePlanner.ReadFields(sourceSheet, sourceRange);
        var roles = submission.Roles ?? PivotCreatePlanner.DefaultRoles(fields);
        var rowIndexes = PivotCreatePlanner.RowIndexes(roles);
        var dataIndexes = PivotCreatePlanner.ValueIndexes(roles);
        if (dataIndexes.Count == 0)
        {
            return Invalid(
                PivotApplicationAction.Create,
                "Insert PivotTable",
                PivotApplicationIssue.MissingValueField,
                PivotMessageSeverity.Information);
        }

        CellAddress? target = null;
        if (submission.DestinationKind == PivotDestinationKind.ExistingWorksheet)
        {
            var destinationText = PivotDataSourcePlanner.NormalizeReferenceText(submission.DestinationRangeText);
            if (destinationText.Length == 0 ||
                !_resolveReference(targetSheetId, destinationText, out var destinationRange))
            {
                return Invalid(
                    PivotApplicationAction.Create,
                    "Insert PivotTable",
                    PivotApplicationIssue.InvalidDestinationReference,
                    PivotMessageSeverity.Warning);
            }

            if (destinationRange.Start.Sheet != targetSheetId)
            {
                return Invalid(
                    PivotApplicationAction.Create,
                    "Insert PivotTable",
                    PivotApplicationIssue.DestinationMustBeOnCurrentSheet,
                    PivotMessageSeverity.Warning);
            }

            target = destinationRange.Start;
        }

        var name = PivotCreatePlanner.SuggestName(_workbook);
        var command = PivotCreatePlanner.BuildCommand(
            sourceRange,
            name,
            rowIndexes,
            dataIndexes,
            targetSheetId,
            target);
        var transition = new PivotDisplayTransition(
            RefreshFieldList: submission.OpenFieldList,
            RefreshSheetTabs: target is null,
            RefreshViewport: true,
            RefreshStatus: target is null);
        return Ready(
            PivotApplicationAction.Create,
            "Insert PivotTable",
            command,
            target: null,
            transition,
            StatusArgument: sourceText);
    }

    public PivotApplicationPlan PlanRefresh(PivotApplicationTarget target) =>
        Ready(
            PivotApplicationAction.Refresh,
            "Refresh PivotTable",
            new RefreshPivotTableCommand(target.Sheet.Id, target.PivotTable.Name),
            target,
            new PivotDisplayTransition(RefreshViewport: true),
            target.PivotTable.Name);

    public PivotApplicationPlan PlanRename(PivotApplicationTarget target, string? typedName)
    {
        if (!PivotNamePlanner.TryCreateResult(
                target.PivotTable,
                typedName,
                candidate => IsPivotNameInUseByOther(target.PivotTable, candidate),
                out var result,
                out var error))
        {
            var issue = error == PivotNamePlanner.DuplicateNameMessage
                ? PivotApplicationIssue.DuplicateName
                : PivotApplicationIssue.EmptyName;
            return Invalid(
                PivotApplicationAction.Rename,
                "Rename PivotTable",
                issue,
                PivotMessageSeverity.Warning,
                error,
                target);
        }

        return Ready(
            PivotApplicationAction.Rename,
            "Rename PivotTable",
            new RenamePivotTableCommand(target.Sheet.Id, target.PivotTable.Name, result!.Name),
            target,
            new PivotDisplayTransition(
                RefreshFieldList: true,
                RefreshSlicerTimeline: true),
            result.Name);
    }

    public PivotApplicationPlan PlanMove(PivotApplicationTarget target, string? destinationText)
    {
        var normalized = PivotDataSourcePlanner.NormalizeReferenceText(destinationText);
        if (normalized.Length == 0 ||
            !_resolveReference(target.Sheet.Id, normalized, out var destinationRange))
        {
            return Invalid(
                PivotApplicationAction.Move,
                "Move PivotTable",
                PivotApplicationIssue.InvalidDestinationReference,
                PivotMessageSeverity.Warning,
                target: target);
        }

        if (destinationRange.Start.Sheet != target.Sheet.Id)
        {
            return Invalid(
                PivotApplicationAction.Move,
                "Move PivotTable",
                PivotApplicationIssue.DestinationMustBeOnCurrentSheet,
                PivotMessageSeverity.Warning,
                target: target);
        }

        if (!PivotUiPlanner.TryCreateMovedTargetRange(
                target.PivotTable,
                destinationRange.Start,
                out var movedRange))
        {
            return Invalid(
                PivotApplicationAction.Move,
                "Move PivotTable",
                PivotApplicationIssue.DestinationOutOfBounds,
                PivotMessageSeverity.Warning,
                target: target);
        }

        return Ready(
            PivotApplicationAction.Move,
            "Move PivotTable",
            new MovePivotTableCommand(target.Sheet.Id, target.PivotTable.Name, destinationRange.Start),
            target,
            new PivotDisplayTransition(
                SelectionRange: movedRange,
                EnsureVisible: movedRange.Start,
                RefreshFieldList: true,
                RefreshViewport: true),
            normalized);
    }

    public PivotApplicationPlan PlanChangeDataSource(
        PivotApplicationTarget target,
        string? sourceRangeText)
    {
        bool Resolve(string reference, out GridRange range) =>
            _resolveReference(target.Sheet.Id, reference, out range);

        if (!PivotDataSourcePlanner.TryCreateChange(
                sourceRangeText,
                Resolve,
                out var change,
                out var error))
        {
            return Invalid(
                PivotApplicationAction.ChangeDataSource,
                "Change PivotTable Data Source",
                PivotApplicationIssue.InvalidDataSource,
                PivotMessageSeverity.Warning,
                error,
                target);
        }

        return Ready(
            PivotApplicationAction.ChangeDataSource,
            "Change PivotTable Data Source",
            new ChangePivotTableSourceCommand(target.Sheet.Id, target.PivotTable.Name, change!.SourceRange),
            target,
            new PivotDisplayTransition(RefreshViewport: true),
            change.SourceRangeText);
    }

    public PivotApplicationPlan PlanClear(PivotApplicationTarget target) =>
        Ready(
            PivotApplicationAction.Clear,
            "Clear PivotTable",
            new ClearPivotTableViewCommand(target.Sheet.Id, target.PivotTable.Name),
            target,
            new PivotDisplayTransition(
                RefreshFieldList: true,
                RefreshViewport: true),
            target.PivotTable.Name);

    public PivotApplicationPlan PlanSelect(PivotApplicationTarget target)
    {
        var source = PivotUiPlanner.ResolvePivotTableSelectionRange(target.PivotTable);
        var range = new GridRange(
            new CellAddress(target.Sheet.Id, source.Start.Row, source.Start.Col),
            new CellAddress(target.Sheet.Id, source.End.Row, source.End.Col));
        return Ready(
            PivotApplicationAction.Select,
            "Select PivotTable",
            command: null,
            target,
            new PivotDisplayTransition(
                SelectionRange: range,
                EnsureVisible: range.Start,
                RefreshFieldList: true),
            target.PivotTable.Name,
            isDisplayOnly: true);
    }

    public PivotApplicationPlan PlanShowDetails(
        SheetId sheetId,
        GridRange? selectedRange)
    {
        var sheet = _workbook.GetSheet(sheetId);
        var detailsTarget = PivotUiPlanner.ResolveShowDetailsTarget(sheet, selectedRange);
        if (sheet is null || detailsTarget is null)
        {
            return Invalid(
                PivotApplicationAction.ShowDetails,
                "Show PivotTable Details",
                PivotApplicationIssue.NoPivotTable,
                PivotMessageSeverity.Information);
        }

        var pivot = sheet.PivotTables.First(table =>
            string.Equals(table.Name, detailsTarget.PivotTableName, StringComparison.OrdinalIgnoreCase));
        var target = new PivotApplicationTarget(sheet, pivot);
        return Ready(
            PivotApplicationAction.ShowDetails,
            "Show PivotTable Details",
            new DrillDownPivotTableCommand(sheetId, detailsTarget.PivotTableName, detailsTarget.PivotCell),
            target,
            new PivotDisplayTransition(
                RefreshSheetTabs: true,
                RefreshViewport: true),
            detailsTarget.PivotTableName);
    }

    public PivotApplicationPlan PlanLayout(
        PivotApplicationTarget target,
        PivotFieldAreas areas)
    {
        ArgumentNullException.ThrowIfNull(areas);
        if (areas.DataFields.Count == 0)
        {
            return Invalid(
                PivotApplicationAction.ConfigureLayout,
                "PivotTable Fields",
                PivotApplicationIssue.MissingValueField,
                PivotMessageSeverity.Information,
                target: target);
        }

        return Ready(
            PivotApplicationAction.ConfigureLayout,
            "PivotTable Fields",
            new ConfigurePivotTableLayoutCommand(
                target.Sheet.Id,
                target.PivotTable.Name,
                areas.RowFields,
                areas.ColumnFields,
                areas.PageFields,
                areas.DataFields),
            target,
            new PivotDisplayTransition(
                RefreshFieldList: true,
                RefreshViewport: true),
            target.PivotTable.Name);
    }

    public PivotApplicationPlan PlanMutation(
        PivotApplicationTarget? target,
        IWorkbookCommand command,
        string? statusArgument = null)
    {
        ArgumentNullException.ThrowIfNull(command);
        var action = command switch
        {
            ConfigurePivotTableLayoutCommand => PivotApplicationAction.ConfigureLayout,
            ConfigurePivotTableFieldFiltersCommand => PivotApplicationAction.ConfigureFilters,
            ConfigurePivotTableCalculatedItemsCommand => PivotApplicationAction.ConfigureCalculations,
            ConfigurePivotTableOptionsCommand => PivotApplicationAction.ConfigureOptions,
            AddSlicerCommand => PivotApplicationAction.InsertSlicer,
            AddTimelineCommand => PivotApplicationAction.InsertTimeline,
            SetSlicerSelectionCommand => PivotApplicationAction.ConfigureSlicer,
            SetTimelineRangeCommand or SetTimelineGranularityCommand => PivotApplicationAction.ConfigureTimeline,
            _ => PivotApplicationAction.ConfigureView,
        };
        var refreshSlicerTimeline = action is
            PivotApplicationAction.InsertSlicer or
            PivotApplicationAction.InsertTimeline or
            PivotApplicationAction.ConfigureSlicer or
            PivotApplicationAction.ConfigureTimeline;
        return Ready(
            action,
            command.Label,
            command,
            target,
            new PivotDisplayTransition(
                RefreshFieldList: !refreshSlicerTimeline,
                RefreshSlicerTimeline: refreshSlicerTimeline,
                RefreshViewport: true),
            statusArgument ?? command.Label);
    }

    public PivotApplicationPlan PlanHeaderCommand(
        PivotApplicationTarget target,
        PivotHeaderCommandPlan headerPlan)
    {
        ArgumentNullException.ThrowIfNull(headerPlan);
        if (headerPlan.Command is null)
        {
            return Invalid(
                PivotApplicationAction.ConfigureView,
                "PivotTable Field",
                PivotApplicationIssue.CommandFailed,
                PivotMessageSeverity.Error,
                headerPlan.DeferredReason,
                target);
        }

        return PlanMutation(target, headerPlan.Command);
    }

    public PivotApplicationOutcome Execute(PivotApplicationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!plan.CanApply)
        {
            return new PivotApplicationOutcome(
                plan.Action,
                Success: false,
                Executed: false,
                IsNoOp: false,
                plan.Transition,
                plan.Message,
                plan.StatusArgument);
        }

        if (plan.Command is null)
        {
            return new PivotApplicationOutcome(
                plan.Action,
                Success: true,
                Executed: false,
                IsNoOp: false,
                plan.Transition,
                Message: null,
                plan.StatusArgument);
        }

        var execution = _executeCommand(plan.Command, plan.CommandLabel);
        if (!execution.Success)
        {
            return new PivotApplicationOutcome(
                plan.Action,
                Success: false,
                Executed: true,
                execution.IsNoOp,
                PivotDisplayTransition.None,
                new PivotMessageModel(
                    PivotApplicationIssue.CommandFailed,
                    PivotMessageSeverity.Error,
                    execution.ErrorMessage),
                plan.StatusArgument);
        }

        var transition = CompleteDynamicTransition(plan, execution);
        return new PivotApplicationOutcome(
            plan.Action,
            Success: true,
            Executed: true,
            execution.IsNoOp,
            transition,
            Message: null,
            plan.StatusArgument);
    }

    private PivotDisplayTransition CompleteDynamicTransition(
        PivotApplicationPlan plan,
        PivotCommandExecutionResult execution)
    {
        var transition = plan.Transition;
        if (plan.Command is AddPivotTableToNewWorksheetCommand { CreatedSheetId: { } createdSheetId })
        {
            var anchor = new CellAddress(createdSheetId, 1, 1);
            transition = transition with
            {
                ActivateSheetId = createdSheetId,
                SelectionRange = new GridRange(anchor, anchor),
                EnsureVisible = anchor,
            };
        }

        if (plan.Action == PivotApplicationAction.ShowDetails &&
            execution.AffectedCells is { Count: > 0 } affected)
        {
            transition = transition with { ActivateSheetId = affected[0].Sheet };
        }

        return transition;
    }

    private bool IsPivotNameInUseByOther(PivotTableModel target, string candidate) =>
        _workbook.Sheets
            .SelectMany(sheet => sheet.PivotTables)
            .Any(pivot =>
                !ReferenceEquals(pivot, target) &&
                string.Equals(pivot.Name, candidate, StringComparison.OrdinalIgnoreCase));

    private static PivotTargetResolution MissingTarget() =>
        new(
            Target: null,
            new PivotMessageModel(
                PivotApplicationIssue.NoPivotTable,
                PivotMessageSeverity.Information));

    private static PivotMessageModel MessageFor(PivotCreateSourceRangeError error) =>
        error switch
        {
            PivotCreateSourceRangeError.MissingSource =>
                new PivotMessageModel(PivotApplicationIssue.MissingSource, PivotMessageSeverity.Information),
            PivotCreateSourceRangeError.MinimumShape =>
                new PivotMessageModel(PivotApplicationIssue.MinimumSourceShape, PivotMessageSeverity.Information),
            PivotCreateSourceRangeError.MissingHeaders =>
                new PivotMessageModel(PivotApplicationIssue.MissingSourceHeaders, PivotMessageSeverity.Warning),
            _ => new PivotMessageModel(PivotApplicationIssue.InvalidSourceReference, PivotMessageSeverity.Warning),
        };

    private static PivotApplicationPlan Ready(
        PivotApplicationAction action,
        string commandLabel,
        IWorkbookCommand? command,
        PivotApplicationTarget? target,
        PivotDisplayTransition transition,
        string? StatusArgument = null,
        bool isDisplayOnly = false) =>
        new(
            action,
            commandLabel,
            command,
            target,
            transition,
            StatusArgument: StatusArgument,
            IsDisplayOnly: isDisplayOnly);

    private static PivotApplicationPlan Invalid(
        PivotApplicationAction action,
        string commandLabel,
        PivotApplicationIssue issue,
        PivotMessageSeverity severity,
        string? detail = null,
        PivotApplicationTarget? target = null) =>
        new(
            action,
            commandLabel,
            Command: null,
            target,
            PivotDisplayTransition.None,
            new PivotMessageModel(issue, severity, detail));
}
