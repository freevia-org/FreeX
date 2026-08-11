using Avalonia.Input;

namespace Free.Shared.Ribbon.Avalonia;

public enum AvaloniaRibbonKeyTipInputAction
{
    Ignore,
    ToggleMode,
    DismissMode,
    ProcessToken,
}

public readonly record struct AvaloniaRibbonKeyTipInputPlan(
    AvaloniaRibbonKeyTipInputAction Action,
    string? Token = null);

public readonly record struct AvaloniaRibbonKeyTipModeTransitionPlan(
    bool ShouldRouteToken,
    bool Handled,
    bool? ModeVisible = null,
    string? Token = null);

/// <summary>
/// Owns the common Avalonia shell decision that maps keyboard input to ribbon key-tip mode actions.
/// Product hosts retain scope traversal, command activation, and visual realization.
/// </summary>
public static class AvaloniaRibbonKeyTipInputPlanner
{
    public static AvaloniaRibbonKeyTipModeTransitionPlan ResolveModeTransition(
        Key key,
        KeyModifiers modifiers,
        bool modeVisible,
        bool acceptDirectAltToken = false)
    {
        var input = Resolve(key, modifiers, modeVisible, acceptDirectAltToken);
        return input.Action switch
        {
            AvaloniaRibbonKeyTipInputAction.ToggleMode => new(
                ShouldRouteToken: false,
                Handled: true,
                ModeVisible: !modeVisible),
            AvaloniaRibbonKeyTipInputAction.DismissMode => new(
                ShouldRouteToken: false,
                Handled: true,
                ModeVisible: false),
            AvaloniaRibbonKeyTipInputAction.ProcessToken => new(
                ShouldRouteToken: true,
                Handled: false,
                Token: input.Token),
            _ => new(ShouldRouteToken: false, Handled: false),
        };
    }

    public static AvaloniaRibbonKeyTipInputPlan Resolve(
        Key key,
        KeyModifiers modifiers,
        bool modeVisible,
        bool acceptDirectAltToken = false)
    {
        if (key is Key.LeftAlt or Key.RightAlt ||
            key == Key.F10 && modifiers == KeyModifiers.None)
        {
            return new(AvaloniaRibbonKeyTipInputAction.ToggleMode);
        }

        var directAltToken = acceptDirectAltToken && modifiers == KeyModifiers.Alt
            ? AvaloniaKeyTipTokenFormatter.Format(key)
            : null;
        if (!modeVisible && directAltToken is null)
            return new(AvaloniaRibbonKeyTipInputAction.Ignore);

        if (key == Key.Escape)
            return new(AvaloniaRibbonKeyTipInputAction.DismissMode);

        var token = directAltToken ?? AvaloniaKeyTipTokenFormatter.Format(key);
        return token is null
            ? new(AvaloniaRibbonKeyTipInputAction.Ignore)
            : new(AvaloniaRibbonKeyTipInputAction.ProcessToken, token);
    }
}
