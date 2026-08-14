using Free.Shared.Ribbon;

namespace FreeW.App.Presentation.Ribbon;

/// <summary>Renderer adapters consumed by the shared Footnotes command family.</summary>
public sealed record NoteReferenceRibbonPorts(
    Action InsertFootnote,
    Action InsertEndnote,
    Action MoveToNextFootnote,
    Action MoveToPreviousFootnote,
    Action MoveToNextEndnote,
    Action MoveToPreviousEndnote,
    Action? OpenNotes,
    Action? ToggleNotesPane,
    Func<bool>? IsNotesPaneVisible,
    Action? OpenFootnoteEndnoteOptions);

/// <summary>
/// Owns the complete References &gt; Footnotes command policy for both renderers. Native hosts
/// provide only editor operations and toolkit-specific dialogs/panes.
/// </summary>
public static class NoteReferenceRibbonWorkflow
{
    public static IRibbonStatefulCommand? Register(
        FreeWRibbonEditorCommandFamilyBuilder bindings,
        NoteReferenceRibbonPorts ports)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        return RegisterCore(
            ports,
            (action, execute) => bindings.BindAction(action, execute),
            (action, toggle, isChecked) => bindings.BindToggle(action, toggle, isChecked),
            bindings.Bind,
            bindings.Register);
    }

    public static IRibbonStatefulCommand? Register(
        IRibbonCommandRegistry bindings,
        NoteReferenceRibbonPorts ports)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        return RegisterCore(
            ports,
            (action, execute) => bindings.BindAction(action, execute),
            (action, toggle, isChecked) => bindings.BindToggle(action, toggle, isChecked),
            (action, command) => bindings.Bind(action, command),
            bindings.Register);
    }

    private static IRibbonStatefulCommand? RegisterCore(
        NoteReferenceRibbonPorts ports,
        Func<FreeWRibbonCommandAction, Action, IRibbonCommand> bindAction,
        Func<FreeWRibbonCommandAction, Action, Func<bool>, IRibbonStatefulCommand> bindToggle,
        Func<FreeWRibbonCommandAction, IRibbonCommand, IRibbonCommand> bind,
        Action<RibbonCommandId, IRibbonCommand> register)
    {
        ArgumentNullException.ThrowIfNull(ports);
        ArgumentNullException.ThrowIfNull(ports.InsertFootnote);
        ArgumentNullException.ThrowIfNull(ports.InsertEndnote);
        ArgumentNullException.ThrowIfNull(ports.MoveToNextFootnote);
        ArgumentNullException.ThrowIfNull(ports.MoveToPreviousFootnote);
        ArgumentNullException.ThrowIfNull(ports.MoveToNextEndnote);
        ArgumentNullException.ThrowIfNull(ports.MoveToPreviousEndnote);

        var footnote = bindAction(
            FreeWRibbonCommandAction.Footnote,
            ports.InsertFootnote);
        register("freew.insert-footnote", footnote);

        var endnote = bindAction(
            FreeWRibbonCommandAction.Endnote,
            ports.InsertEndnote);
        register("freew.insert-endnote", endnote);

        bindAction(
            FreeWRibbonCommandAction.NextFootnote,
            ports.MoveToNextFootnote);
        bindAction(
            FreeWRibbonCommandAction.PreviousFootnote,
            ports.MoveToPreviousFootnote);
        bindAction(
            FreeWRibbonCommandAction.NextEndnote,
            ports.MoveToNextEndnote);
        bindAction(
            FreeWRibbonCommandAction.PreviousEndnote,
            ports.MoveToPreviousEndnote);

        IRibbonStatefulCommand? notesPane = null;
        if (ports.ToggleNotesPane is not null && ports.IsNotesPaneVisible is not null)
        {
            notesPane = bindToggle(
                FreeWRibbonCommandAction.ShowNotes,
                ports.ToggleNotesPane,
                ports.IsNotesPaneVisible);
        }
        else if (ports.OpenNotes is not null)
        {
            bindAction(
                FreeWRibbonCommandAction.ShowNotes,
                ports.OpenNotes);
        }
        else
        {
            bind(
                FreeWRibbonCommandAction.ShowNotes,
                FreeWRibbonExecutionProfile.UnavailableCommand);
        }

        bind(
            FreeWRibbonCommandAction.FootnoteEndnoteOptions,
            ports.OpenFootnoteEndnoteOptions is null
                ? FreeWRibbonExecutionProfile.UnavailableCommand
                : new ActionRibbonCommand(ports.OpenFootnoteEndnoteOptions));

        return notesPane;
    }
}
