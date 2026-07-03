using Free.Shared.Ribbon;

namespace FreeW.App.Avalonia.Ribbon;

/// <summary>
/// AV-PICTAB: Merges several <see cref="IRibbonContextSource"/> instances into one so the shared
/// ribbon renderer (which accepts a single context source) can show contextual tabs driven by
/// independent triggers — e.g. the Table context (caret-in-cell) and the Floating context
/// (picture/drawing selected) at the same time.
///
/// <para>
/// <see cref="Current"/> is the union of every child's active keys; <see cref="ContextChanged"/>
/// fires whenever any child changes. The keys are disjoint in practice (a caret can't be in a table
/// cell and have a floating object selected simultaneously), but the union handles overlap safely.
/// </para>
/// </summary>
internal sealed class CompositeRibbonContextSource : IRibbonContextSource
{
    private readonly IReadOnlyList<IRibbonContextSource> _sources;

    public RibbonContextState Current
    {
        get
        {
            var state = RibbonContextState.None;
            foreach (var source in _sources)
            {
                var child = source.Current;
                foreach (var key in AllKeys)
                    if (child.IsActive(key))
                        state = state.With(key);
            }
            return state;
        }
    }

    public event EventHandler? ContextChanged;

    /// <summary>
    /// The complete set of activation keys any child source can emit. Adding a new contextual
    /// trigger requires listing its key here so <see cref="Current"/> can union it.
    /// </summary>
    private static readonly string[] AllKeys =
    [
        TableRibbonContextSource.TableContextKey,
        HeaderFooterRibbonContextSource.HeaderFooterContextKey,
        FloatingRibbonContextSource.PictureContextKey,
        FloatingRibbonContextSource.DrawingContextKey,
        FloatingRibbonContextSource.ChartContextKey,
        FloatingRibbonContextSource.SmartArtContextKey,
    ];

    public CompositeRibbonContextSource(params IRibbonContextSource[] sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        _sources = sources;
        foreach (var source in _sources)
            source.ContextChanged += (_, _) => ContextChanged?.Invoke(this, EventArgs.Empty);
    }
}
