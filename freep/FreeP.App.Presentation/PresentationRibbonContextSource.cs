using Free.Shared.Ribbon;

namespace FreeP.App.Compositor;

/// <summary>Shared context source consumed by contextual ribbon renderers.</summary>
public sealed class PresentationRibbonContextSource : IRibbonContextSource
{
    public RibbonContextState Current { get; private set; } = RibbonContextState.None;

    public event EventHandler? ContextChanged;

    public void Apply(RibbonContextState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (ReferenceEquals(Current, state))
            return;

        Current = state;
        ContextChanged?.Invoke(this, EventArgs.Empty);
    }
}
