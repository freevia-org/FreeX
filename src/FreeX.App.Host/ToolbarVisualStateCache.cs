using System;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public sealed class ToolbarVisualStateCache
{
    private readonly record struct Source(StyleId StyleId);

    private Source? _lastSource;
    private ToolbarVisualState? _lastState;

    public ToolbarVisualState GetOrCreate(
        StyleId styleId,
        Func<ToolbarVisualState> create)
    {
        var source = new Source(styleId);
        if (_lastSource == source && _lastState is { } cached)
            return cached;

        var state = create();
        _lastSource = source;
        _lastState = state;
        return state;
    }

    public bool TryGetCurrent(StyleId styleId, out ToolbarVisualState state)
    {
        var source = new Source(styleId);
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
        _lastSource = null;
        _lastState = null;
    }
}
