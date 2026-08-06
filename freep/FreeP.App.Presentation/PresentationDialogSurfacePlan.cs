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

public sealed record PresentationDialogFieldPlan<TField>(
    TField Id,
    PresentationDialogControlKind ControlKind,
    string Label,
    string AccessibleName,
    string AutomationId,
    string? HelpText = null)
    where TField : notnull;

public sealed record PresentationDialogActionPlan<TAction>(
    TAction Id,
    string Label,
    string AccessibleName,
    string AutomationId,
    bool IsDefault = false,
    bool IsCancel = false)
    where TAction : notnull;

/// <summary>
/// Stable renderer-neutral schema for a native presentation dialog. Renderers map
/// these semantics to framework controls, event handlers, focus, and modal lifecycle.
/// </summary>
public sealed class PresentationDialogSurfacePlan<TField, TAction>
    where TField : notnull
    where TAction : notnull
{
    private readonly IReadOnlyDictionary<TField, PresentationDialogFieldPlan<TField>> _fields;
    private readonly IReadOnlyDictionary<TAction, PresentationDialogActionPlan<TAction>> _actions;

    public PresentationDialogSurfacePlan(
        string title,
        string accessibleName,
        string automationId,
        IEnumerable<PresentationDialogFieldPlan<TField>> fields,
        IEnumerable<PresentationDialogActionPlan<TAction>> actions)
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

    public IReadOnlyList<PresentationDialogFieldPlan<TField>> Fields { get; }

    public IReadOnlyList<PresentationDialogActionPlan<TAction>> Actions { get; }

    public PresentationDialogFieldPlan<TField> Field(TField id) =>
        _fields.TryGetValue(id, out var field)
            ? field
            : throw new KeyNotFoundException($"The dialog surface does not define field '{id}'.");

    public PresentationDialogActionPlan<TAction> Action(TAction id) =>
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
