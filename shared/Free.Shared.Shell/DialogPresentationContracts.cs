namespace Free.Shared.Shell;

/// <summary>Renderer-neutral control roles used by dialog surface descriptions.</summary>
public enum DialogControlKind
{
    Text,
    Choice,
    Toggle,
    List,
    Status,
    Label,
}

/// <summary>
/// Stable field semantics for a native dialog. Text values remain app-owned and may be literals or
/// resource keys; renderers decide how they are resolved and which native control realizes the role.
/// </summary>
public record DialogFieldPlan<TField>(
    TField Id,
    DialogControlKind ControlKind,
    string Label,
    string? AccessibleName,
    string AutomationId,
    string? HelpText = null)
    where TField : notnull;

/// <summary>Stable ordering and Enter/Escape semantics for a native dialog action row.</summary>
public record DialogActionPlan(
    string Label,
    bool IsDefault = false,
    bool IsCancel = false);

/// <summary>Stable identity, accessibility, and keyboard semantics for a dialog surface action.</summary>
public record DialogSurfaceActionPlan<TAction>(
    TAction Id,
    string Label,
    string? AccessibleName,
    string AutomationId,
    bool IsDefault = false,
    bool IsCancel = false)
    : DialogActionPlan(Label, IsDefault, IsCancel)
    where TAction : notnull;

/// <summary>
/// Stable renderer-neutral schema for a native dialog. Renderers retain native control creation,
/// event handling, focus realization, and modal lifecycle.
/// </summary>
public class DialogSurfacePlan<TField, TAction>
    where TField : notnull
    where TAction : notnull
{
    private readonly IReadOnlyDictionary<TField, DialogFieldPlan<TField>> _fields;
    private readonly IReadOnlyDictionary<TAction, DialogSurfaceActionPlan<TAction>> _actions;

    public DialogSurfacePlan(
        string title,
        string accessibleName,
        string automationId,
        IEnumerable<DialogFieldPlan<TField>> fields,
        IEnumerable<DialogSurfaceActionPlan<TAction>> actions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(accessibleName);
        ArgumentException.ThrowIfNullOrWhiteSpace(automationId);
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(actions);

        Title = title;
        AccessibleName = accessibleName;
        AutomationId = automationId;
        Fields = fields.ToArray();
        Actions = actions.ToArray();
        _fields = BuildIndex(Fields, field => field.Id, nameof(fields));
        _actions = BuildIndex(Actions, action => action.Id, nameof(actions));
    }

    public string Title { get; }

    public string AccessibleName { get; }

    public string AutomationId { get; }

    public IReadOnlyList<DialogFieldPlan<TField>> Fields { get; }

    public IReadOnlyList<DialogSurfaceActionPlan<TAction>> Actions { get; }

    public DialogFieldPlan<TField> Field(TField id) =>
        _fields.TryGetValue(id, out var field)
            ? field
            : throw new KeyNotFoundException($"The dialog surface does not define field '{id}'.");

    public DialogSurfaceActionPlan<TAction> Action(TAction id) =>
        _actions.TryGetValue(id, out var action)
            ? action
            : throw new KeyNotFoundException($"The dialog surface does not define action '{id}'.");

    private static IReadOnlyDictionary<TKey, TItem> BuildIndex<TKey, TItem>(
        IEnumerable<TItem> items,
        Func<TItem, TKey> keySelector,
        string parameterName)
        where TKey : notnull
    {
        var index = new Dictionary<TKey, TItem>();
        foreach (var item in items)
        {
            var key = keySelector(item);
            if (!index.TryAdd(key, item))
                throw new ArgumentException($"Duplicate dialog surface identifier: {key}.", parameterName);
        }

        return index;
    }
}

/// <summary>
/// Renderer-neutral focus and keyboard policy. The focus target is an app-owned semantic identity;
/// native hosts map it to framework controls and perform the actual focus operation.
/// </summary>
public sealed record DialogFocusPlan<TFocusTarget>(
    TFocusTarget InitialFocusTarget,
    TFocusTarget ValidationFocusTarget,
    bool SelectAllOnFocus,
    IReadOnlyList<DialogActionPlan> ActionButtons)
    where TFocusTarget : notnull;
