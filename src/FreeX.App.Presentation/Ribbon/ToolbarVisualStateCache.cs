using FreeX.Core.Model;

namespace FreeX.App.Presentation.Ribbon;

public sealed class ToolbarVisualStateCache
{
    private const int MaxCachedStates = 16;

    private readonly record struct Source(WorkbookId WorkbookId, StyleId StyleId);

    private readonly Dictionary<Source, ToolbarVisualState> _states = [];
    private readonly List<Source> _stateOrder = [];
    private Source? _lastSource;
    private ToolbarVisualState? _lastState;
    private Source? _previousSource;
    private ToolbarVisualState? _previousState;

    public bool TryGet(WorkbookId workbookId, StyleId styleId, out ToolbarVisualState state)
    {
        var source = new Source(workbookId, styleId);
        if (_lastSource == source && _lastState is { } cached)
        {
            state = cached;
            return true;
        }

        if (_previousSource == source && _previousState is { } previous)
        {
            PromoteRecent(source, previous);
            state = previous;
            return true;
        }

        if (_states.TryGetValue(source, out cached))
        {
            PromoteRecent(source, cached);
            state = cached;
            return true;
        }

        state = null!;
        return false;
    }

    public ToolbarVisualState AddOrUpdate(
        WorkbookId workbookId,
        StyleId styleId,
        ToolbarVisualState state)
    {
        var source = new Source(workbookId, styleId);
        if (_states.ContainsKey(source))
            _stateOrder.Remove(source);
        else
            TrimCachedStatesForNewSource();

        _states[source] = state;
        _stateOrder.Add(source);
        PromoteRecent(source, state);
        return state;
    }

    public ToolbarVisualState GetOrCreate(
        WorkbookId workbookId,
        StyleId styleId,
        Func<ToolbarVisualState> create)
    {
        if (TryGet(workbookId, styleId, out var cached))
            return cached;

        return AddOrUpdate(workbookId, styleId, create());
    }

    public bool TryGetCurrent(WorkbookId workbookId, StyleId styleId, out ToolbarVisualState state)
    {
        var source = new Source(workbookId, styleId);
        if (_lastSource == source && _lastState is { } cached)
        {
            state = cached;
            return true;
        }

        state = null!;
        return false;
    }

    public void Clear()
    {
        _states.Clear();
        _stateOrder.Clear();
        _lastSource = null;
        _lastState = null;
        _previousSource = null;
        _previousState = null;
    }

    private void PromoteRecent(Source source, ToolbarVisualState state)
    {
        if (_lastSource == source)
        {
            _lastState = state;
            return;
        }

        _previousSource = _lastSource;
        _previousState = _lastState;
        _lastSource = source;
        _lastState = state;
    }

    private void TrimCachedStatesForNewSource()
    {
        if (_states.Count < MaxCachedStates || _stateOrder.Count == 0)
            return;

        var source = _stateOrder[0];
        _stateOrder.RemoveAt(0);
        _states.Remove(source);
    }
}
