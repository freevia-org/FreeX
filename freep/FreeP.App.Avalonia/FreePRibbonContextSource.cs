using Free.Shared.Ribbon;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

/// <summary>
/// Maps the current FreeP shape selection to the activation keys declared by the
/// contextual presentation ribbon tabs.
/// </summary>
internal sealed class FreePRibbonContextSource : IRibbonContextSource
{
    public RibbonContextState Current { get; private set; } = RibbonContextState.None;

    public event EventHandler? ContextChanged;

    internal void Refresh(EditingSession editor)
    {
        ArgumentNullException.ThrowIfNull(editor);

        var state = PresentationRibbonContextPlanner.Build(editor);
        if (PresentationRibbonContextPlanner.AreEquivalent(Current, state))
            return;

        Current = state;
        ContextChanged?.Invoke(this, EventArgs.Empty);
    }
}
