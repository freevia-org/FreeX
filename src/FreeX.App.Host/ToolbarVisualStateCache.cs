using System;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public sealed class ToolbarVisualStateCache
{
    private const int MaxCachedStates = 16;

    private readonly record struct Source(WorkbookId WorkbookId, StyleId StyleId);

    private readonly Dictionary<Source, ToolbarVisualState> _states = [];
    private readonly Queue<Source> _stateOrder = [];
    private Source? _lastSource;
    private ToolbarVisualState? _lastState;

    public ToolbarVisualState GetOrCreate(
        WorkbookId workbookId,
        StyleId styleId,
        Func<ToolbarVisualState> create)
    {
        var source = new Source(workbookId, styleId);
        if (_lastSource == source && _lastState is { } cached)
            return cached;

        if (_states.TryGetValue(source, out cached))
        {
            _lastSource = source;
            _lastState = cached;
            return cached;
        }

        var state = create();
        _states[source] = state;
        _stateOrder.Enqueue(source);
        TrimCachedStates();
        _lastSource = source;
        _lastState = state;
        return state;
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
    }

    private void TrimCachedStates()
    {
        while (_states.Count > MaxCachedStates && _stateOrder.TryDequeue(out var source))
            _states.Remove(source);
    }
}
