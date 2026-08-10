using Free.Shared.Shell;

namespace FreeP.App.Compositor;

public enum PresentationDialogControlKind
{
    Text,
    Choice,
    Toggle,
    List,
    Status,
    Label,
}

public sealed record PresentationDialogFieldPlan<TField> : DialogFieldPlan<TField>
    where TField : notnull
{
    public PresentationDialogFieldPlan(
        TField Id,
        PresentationDialogControlKind ControlKind,
        string Label,
        string AccessibleName,
        string AutomationId,
        string? HelpText = null)
        : base(Id, (DialogControlKind)ControlKind, Label, AccessibleName, AutomationId, HelpText)
    {
    }

    public new PresentationDialogControlKind ControlKind =>
        (PresentationDialogControlKind)base.ControlKind;

    public void Deconstruct(
        out TField Id,
        out PresentationDialogControlKind ControlKind,
        out string Label,
        out string AccessibleName,
        out string AutomationId,
        out string? HelpText)
    {
        Id = this.Id;
        ControlKind = this.ControlKind;
        Label = this.Label;
        AccessibleName = this.AccessibleName!;
        AutomationId = this.AutomationId;
        HelpText = this.HelpText;
    }
}

public sealed record PresentationDialogActionPlan<TAction> : DialogSurfaceActionPlan<TAction>
    where TAction : notnull
{
    public PresentationDialogActionPlan(
        TAction Id,
        string Label,
        string AccessibleName,
        string AutomationId,
        bool IsDefault = false,
        bool IsCancel = false)
        : base(Id, Label, AccessibleName, AutomationId, IsDefault, IsCancel)
    {
    }
}

/// <summary>
/// Stable renderer-neutral schema for a native presentation dialog. Renderers map
/// these semantics to framework controls, event handlers, focus, and modal lifecycle.
/// </summary>
public sealed class PresentationDialogSurfacePlan<TField, TAction> : DialogSurfacePlan<TField, TAction>
    where TField : notnull
    where TAction : notnull
{
    public PresentationDialogSurfacePlan(
        string title,
        string accessibleName,
        string automationId,
        IEnumerable<PresentationDialogFieldPlan<TField>> fields,
        IEnumerable<PresentationDialogActionPlan<TAction>> actions)
        : base(title, accessibleName, automationId, fields, actions)
    {
        Fields = base.Fields.Cast<PresentationDialogFieldPlan<TField>>().ToArray();
        Actions = base.Actions.Cast<PresentationDialogActionPlan<TAction>>().ToArray();
    }

    public new IReadOnlyList<PresentationDialogFieldPlan<TField>> Fields { get; }

    public new IReadOnlyList<PresentationDialogActionPlan<TAction>> Actions { get; }

    public new PresentationDialogFieldPlan<TField> Field(TField id) =>
        (PresentationDialogFieldPlan<TField>)base.Field(id);

    public new PresentationDialogActionPlan<TAction> Action(TAction id) =>
        (PresentationDialogActionPlan<TAction>)base.Action(id);
}
