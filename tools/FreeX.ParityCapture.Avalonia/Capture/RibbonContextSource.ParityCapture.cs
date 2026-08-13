using Free.Shared.Ribbon;

namespace FreeX.App.Avalonia;

internal sealed partial class AvaloniaRibbonContextSource
{
    private string? _parityCaptureActivationKey;

    internal void SetParityCaptureContext(string? activationKey)
    {
        _parityCaptureActivationKey = activationKey;
        Recompute();
    }

    partial void ConfigureOptionalContextMutation(ref bool suppress) =>
        suppress = _parityCaptureActivationKey is not null;

    partial void ApplyOptionalContextOverride(ref RibbonContextState state)
    {
        if (_parityCaptureActivationKey is { } activationKey)
            state = RibbonContextState.None.With(activationKey);
    }
}
