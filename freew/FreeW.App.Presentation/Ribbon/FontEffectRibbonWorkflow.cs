using Free.Shared.Ribbon;

namespace FreeW.App.Presentation.Ribbon;

public sealed record FontEffectRibbonPorts(
    IRibbonCommand Bold,
    IRibbonCommand Italic,
    IRibbonCommand Underline,
    IRibbonCommand Strikethrough,
    IRibbonCommand SmallCaps,
    IRibbonCommand AllCaps,
    IRibbonCommand Superscript,
    IRibbonCommand Subscript,
    IRibbonCommand GrowFont,
    IRibbonCommand ShrinkFont);

/// <summary>
/// Owns Home &gt; Font effect command mapping for both renderers. Native routed/stateful commands
/// remain renderer adapters, while semantic ownership and route completeness stay in Presentation.
/// </summary>
public static class FontEffectRibbonWorkflow
{
    public static IReadOnlyList<FreeWRibbonCommandAction> Actions { get; } =
    [
        FreeWRibbonCommandAction.Bold,
        FreeWRibbonCommandAction.Italic,
        FreeWRibbonCommandAction.Underline,
        FreeWRibbonCommandAction.Strikethrough,
        FreeWRibbonCommandAction.Smallcaps,
        FreeWRibbonCommandAction.Allcaps,
        FreeWRibbonCommandAction.Superscript,
        FreeWRibbonCommandAction.Subscript,
        FreeWRibbonCommandAction.GrowFont,
        FreeWRibbonCommandAction.ShrinkFont,
    ];

    public static void Register(
        IRibbonCommandRegistry bindings,
        FontEffectRibbonPorts ports)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(ports);

        bindings.Bind(FreeWRibbonCommandAction.Bold, ports.Bold);
        bindings.Bind(FreeWRibbonCommandAction.Italic, ports.Italic);
        bindings.Bind(FreeWRibbonCommandAction.Underline, ports.Underline);
        bindings.Bind(FreeWRibbonCommandAction.Strikethrough, ports.Strikethrough);
        bindings.Bind(FreeWRibbonCommandAction.Smallcaps, ports.SmallCaps);
        bindings.Bind(FreeWRibbonCommandAction.Allcaps, ports.AllCaps);
        bindings.Bind(FreeWRibbonCommandAction.Superscript, ports.Superscript);
        bindings.Bind(FreeWRibbonCommandAction.Subscript, ports.Subscript);
        bindings.Bind(FreeWRibbonCommandAction.GrowFont, ports.GrowFont);
        bindings.Bind(FreeWRibbonCommandAction.ShrinkFont, ports.ShrinkFont);
    }
}
