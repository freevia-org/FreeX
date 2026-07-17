using Free.Shared.Ribbon;
using FreeX.App.Avalonia.Charts;
using FreeX.App.Avalonia.Ribbon;
using FreeX.App.Services;
using FreeX.App.Services.Ribbon;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private const string RibbonCommandBehaviorCategory = "ribbon-command-behavior";
    private const string RibbonPlacementBehaviorCategory = "ribbon-placement-behavior";

    private sealed record RibbonCommandBehaviorEvidence(
        RibbonCommandId CommandId,
        string Status,
        string EvidenceLevel,
        string Evidence,
        string Note);

    /// <summary>
    /// Appends behavior-aware ribbon evidence. Each runtime command is evaluated once and each visible
    /// placement receives a reference to that result, preventing duplicate split-button/menu placements
    /// from repeating mutations while keeping all rendered interactions accountable.
    /// </summary>
    internal void AddRibbonInteractionExecutionResults(List<InteractionValidationResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        var definition = AvaloniaRibbonComposition.BuildDefinition();
        var placements = AvaloniaRibbonComposition.EnumerateSurfaceRows(definition).ToArray();
        var evidenceByCommand = BuildRibbonCommandBehaviorEvidence(placements);

        foreach (var evidence in evidenceByCommand.Values.OrderBy(item => item.CommandId.Value, StringComparer.Ordinal))
        {
            results.Add(new InteractionValidationResult(
                Id: $"ribbon-command-behavior/{EscapeResultId(evidence.CommandId.Value)}",
                Category: RibbonCommandBehaviorCategory,
                Status: evidence.Status,
                EvidenceLevel: evidence.EvidenceLevel,
                Evidence: evidence.Evidence,
                Note: evidence.Note));
        }

        foreach (var placement in placements)
        {
            var evidence = evidenceByCommand[placement.CommandId];
            results.Add(new InteractionValidationResult(
                Id: $"ribbon-placement-behavior/{placement.RowId}",
                Category: RibbonPlacementBehaviorCategory,
                Status: evidence.Status,
                EvidenceLevel: "command-behavior-reference",
                Evidence: $"{placement.TabHeader} > {placement.GroupHeader} > {placement.Label} -> {evidence.EvidenceLevel}",
                Note: $"Command evidence: ribbon-command-behavior/{EscapeResultId(placement.CommandId.Value)}. {evidence.Note}"));
        }
    }

    private IReadOnlyDictionary<RibbonCommandId, RibbonCommandBehaviorEvidence> BuildRibbonCommandBehaviorEvidence(
        IReadOnlyList<AvaloniaRibbonComposition.SurfaceRow> placements)
    {
        var registry = _ribbonCommandRegistry;
        var firstPlacementByCommand = placements
            .GroupBy(row => row.CommandId)
            .ToDictionary(group => group.Key, group => group.First());
        var results = new Dictionary<RibbonCommandId, RibbonCommandBehaviorEvidence>();

        foreach (var (commandId, firstPlacement) in firstPlacementByCommand)
        {
            if (registry is null || !registry.TryGet(commandId, out var command) || command is null)
            {
                results[commandId] = Failure(
                    commandId,
                    "unregistered-command",
                    "No production command is registered for this runtime ribbon id.");
                continue;
            }

            if (command is EmptyRibbonCommand)
            {
                results[commandId] = Failure(
                    commandId,
                    "empty-command-gap",
                    "The production registry resolves this id to EmptyRibbonCommand; it was not reported as invoked.");
                continue;
            }

            if (command is DisabledNoOpRibbonCommand)
            {
                results[commandId] = Passed(
                    commandId,
                    "explicitly-disabled",
                    command.GetType().Name,
                    "The production command is deliberately disabled and ignores execution.");
                continue;
            }

            if (TryExecuteDisposableMutationProbe(commandId, out var mutationEvidence))
            {
                results[commandId] = mutationEvidence;
                continue;
            }

            if (command is IRibbonStatefulCommand stateful)
            {
                results[commandId] = ReadCommandState(commandId, command, stateful);
                continue;
            }

            results[commandId] = ClassifyUnexecutedCommand(commandId, command, firstPlacement);
        }

        return results;
    }

    private static bool TryExecuteDisposableMutationProbe(
        RibbonCommandId commandId,
        out RibbonCommandBehaviorEvidence evidence)
    {
        if (IsSharedFormatToggle(commandId.Value))
        {
            evidence = ExecuteDisposableFormatProbe(commandId);
            return true;
        }

        if (InsertChartCommandFactory.ChartTypeForRibbonCommand(commandId.Value) is not null)
        {
            evidence = ExecuteDisposableChartProbe(commandId);
            return true;
        }

        evidence = null!;
        return false;
    }

    private static RibbonCommandBehaviorEvidence ExecuteDisposableFormatProbe(RibbonCommandId commandId)
    {
        try
        {
            var session = CreateDisposableRibbonSession();
            var registry = AvaloniaRibbonComposition.BuildRegistry(() => session, _ => { });
            if (!registry.TryGet(commandId, out var command) || command is not WorkbookToggleFormatCommand toggle)
                return Failure(commandId, "disposable-probe-route-mismatch", "The isolated registry did not resolve the shared format command.");

            var before = toggle.GetState().IsChecked;
            toggle.Execute(RibbonCommandContext.Empty);
            var after = toggle.GetState().IsChecked;
            if (after == before || !session.IsDirty)
                return Failure(commandId, "executed-mutation-not-observed", "Execution completed but did not toggle format state and dirty the disposable workbook.");

            return Passed(
                commandId,
                "executed-mutation",
                $"{command.GetType().Name}: checked {before} -> {after}; dirty generation {session.DirtyGeneration}",
                "Executed against a fresh disposable WorkbookSession and verified the resulting format-state mutation.");
        }
        catch (Exception ex)
        {
            return Failure(commandId, "executed-mutation-threw", $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static RibbonCommandBehaviorEvidence ExecuteDisposableChartProbe(RibbonCommandId commandId)
    {
        try
        {
            var session = CreateDisposableRibbonSession();
            var registry = AvaloniaRibbonComposition.BuildRegistry(() => session, _ => { });
            if (!registry.TryGet(commandId, out var command) || command is not InsertChartRibbonCommand)
                return Failure(commandId, "disposable-probe-route-mismatch", "The isolated registry did not resolve the chart insertion command.");

            var expectedType = InsertChartCommandFactory.ChartTypeForRibbonCommand(commandId.Value)!.Value;
            var before = session.ActiveSheet.Charts.Count;
            command.Execute(RibbonCommandContext.Empty);
            var after = session.ActiveSheet.Charts.Count;
            if (after != before + 1 || session.ActiveSheet.Charts[^1].Type != expectedType || !session.IsDirty)
                return Failure(commandId, "executed-mutation-not-observed", "Execution did not add the expected chart to the disposable workbook.");

            return Passed(
                commandId,
                "executed-mutation",
                $"{command.GetType().Name}: charts {before} -> {after}; type {expectedType}",
                "Executed against a fresh disposable WorkbookSession and verified the inserted chart type and collection mutation.");
        }
        catch (Exception ex)
        {
            return Failure(commandId, "executed-mutation-threw", $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static RibbonCommandBehaviorEvidence ReadCommandState(
        RibbonCommandId commandId,
        IRibbonCommand command,
        IRibbonStatefulCommand stateful)
    {
        try
        {
            var state = stateful.GetState();
            if (!state.IsEnabled)
            {
                return Passed(
                    commandId,
                    "explicitly-disabled",
                    $"{command.GetType().Name}: enabled=false, checked={state.IsChecked}, value={state.Value ?? "<null>"}",
                    "The production command reports itself unavailable for the current workbook/object context and was not invoked.");
            }

            return Passed(
                commandId,
                "state-read",
                $"{command.GetType().Name}: enabled={state.IsEnabled}, checked={state.IsChecked}, value={state.Value ?? "<null>"}",
                "Read the production command's live enablement/checked/value contract; execution was not required for this state evidence.");
        }
        catch (Exception ex)
        {
            return Failure(commandId, "state-read-threw", $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static RibbonCommandBehaviorEvidence ClassifyUnexecutedCommand(
        RibbonCommandId commandId,
        IRibbonCommand command,
        AvaloniaRibbonComposition.SurfaceRow placement)
    {
        var id = commandId.Value;
        if (command is ValueRibbonCommand)
        {
            return Classified(
                commandId,
                "classified-value-input",
                command,
                "Requires a user-selected combo/gallery value; no arbitrary value was injected into the production handler.");
        }

        if (ContainsAny(id, NativeOrExternalTokens))
        {
            return Classified(
                commandId,
                "classified-native-external",
                command,
                "May invoke an operating-system picker, external process, network destination, clipboard, print, or window lifecycle action; intentionally not automated in-process.");
        }

        if (ContainsAny(id, DestructiveTokens))
        {
            return Classified(
                commandId,
                "classified-destructive",
                command,
                "Can remove, clear, hide, reset, cut, or overwrite workbook state; registry routing is present, but production execution is intentionally withheld.");
        }

        if (ContainsAny(id, ModalTokens))
        {
            return Classified(
                commandId,
                "classified-modal",
                command,
                "Opens a modal, modeless, gallery, pane, or picker surface; dialog interaction validation owns open/render/close and keyboard-contract evidence.");
        }

        if (placement.ActivationKey is not null)
        {
            return Classified(
                commandId,
                "classified-context-required",
                command,
                $"Requires contextual ribbon activation '{placement.ActivationKey}' and a matching selected workbook object.");
        }

        return Classified(
            commandId,
            "classified-host-workbook-action",
            command,
            "Production host callback is registered, but no isolated deterministic verifier exists yet; this remains explicit behavior debt rather than an invoked/pass claim.");
    }

    private static WorkbookSession CreateDisposableRibbonSession() =>
        new WorkbookSessionFactory().CreateParityDemo(
            viewportHeight: 480,
            viewportWidth: 800,
            includeObjects: true);

    private static bool IsSharedFormatToggle(string commandId) =>
        string.Equals(commandId, AvaloniaCommandIdAdapter.ToCanonical("home.bold"), StringComparison.Ordinal) ||
        string.Equals(commandId, AvaloniaCommandIdAdapter.ToCanonical("home.italic"), StringComparison.Ordinal) ||
        string.Equals(commandId, AvaloniaCommandIdAdapter.ToCanonical("home.underline"), StringComparison.Ordinal);

    private static bool ContainsAny(string commandId, IReadOnlyList<string> tokens) =>
        tokens.Any(token => commandId.Contains(token, StringComparison.OrdinalIgnoreCase));

    private static RibbonCommandBehaviorEvidence Passed(
        RibbonCommandId commandId,
        string evidenceLevel,
        string evidence,
        string note) =>
        new(commandId, "passed", evidenceLevel, evidence, note);

    private static RibbonCommandBehaviorEvidence Failure(
        RibbonCommandId commandId,
        string evidenceLevel,
        string note) =>
        new(commandId, "failed", evidenceLevel, commandId.Value, note);

    private static RibbonCommandBehaviorEvidence Classified(
        RibbonCommandId commandId,
        string evidenceLevel,
        IRibbonCommand command,
        string note) =>
        new(commandId, "skipped", evidenceLevel, $"{commandId.Value} | {command.GetType().Name}", note);

    private static string EscapeResultId(string value) => Uri.EscapeDataString(value);

    private static readonly string[] NativeOrExternalTokens =
    [
        "Open", "Save", "Print", "Export", "Share", "Get Data", "Refresh", "Picture", "Object",
        "Online", "Feedback", "Update", "Legal", "Clipboard", "New Window", "Arrange All", "Switch Windows",
        "Hide Window", "Side by Side", "Synchronous Scrolling", "Email", "Link", "Import", "Connection",
    ];

    private static readonly string[] DestructiveTokens =
    [
        "Delete", "Clear", "Remove", "Erase", "Reset", "Cut", "Hide", "Unhide", "Unfreeze", "Ungroup",
        "Unmerge", "Unprotect", "Break Link", "Convert", "Paste", "Replace", "Sort", "Filter",
    ];

    private static readonly string[] ModalTokens =
    [
        "Dialog", "Manager", "Options", "Properties", "Format Cells", "More Colors", "More Borders",
        "More Accounting", "More Rules", "More Functions", "Customize", "Gallery", "Pane", "Statistics",
        "Spelling", "Accessibility", "Thesaurus", "Translate", "Protect", "Allow Users", "Data Validation",
        "Text to Columns", "Consolidate", "Quick Analysis", "PivotTable", "PivotChart", "Table", "Hyperlink",
        "Equation", "Function", "Name", "Scenario", "Goal Seek", "What-If", "Data Table", "Zoom", "Find",
        "Go To", "Row Height", "Column Width", "Rename", "Tab Color", "Insert Cells", "Delete Cells",
        "Conditional Formatting", "Rule", "Comments", "Notes", "Watch Window", "Page Setup", "Theme",
    ];
}
