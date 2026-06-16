using System.Collections.Immutable;

namespace Free.Shared.Ribbon;

/// <summary>Immutable set of active selection-context keys (e.g. "chart.selected").</summary>
public sealed class RibbonContextState
{
    private readonly ImmutableHashSet<string> _keys;

    private RibbonContextState(ImmutableHashSet<string> keys) => _keys = keys;

    public static readonly RibbonContextState None =
        new(ImmutableHashSet<string>.Empty.WithComparer(StringComparer.Ordinal));

    public bool IsActive(string key) => _keys.Contains(key);

    public RibbonContextState With(string key) => new(_keys.Add(key));

    public RibbonContextState Without(string key) => new(_keys.Remove(key));
}
