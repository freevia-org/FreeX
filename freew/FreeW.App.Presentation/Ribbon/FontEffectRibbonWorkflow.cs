using Free.Shared.Ribbon;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

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

public enum FontEffectRibbonKind
{
    Bold,
    Italic,
    Underline,
    Strikethrough,
    SmallCaps,
    AllCaps,
    Superscript,
    Subscript,
}

/// <summary>Shared checked-state policy for the Home &gt; Font toggle controls.</summary>
public static class FontEffectRibbonStatePlanner
{
    public static RibbonCommandState GetState(
        FontEffectRibbonKind kind,
        FontDialogSelectionState selection,
        bool isEnabled = true)
    {
        ArgumentNullException.ThrowIfNull(selection);
        bool indeterminate = kind switch
        {
            FontEffectRibbonKind.Bold => selection.BoldIndeterminate,
            FontEffectRibbonKind.Italic => selection.ItalicIndeterminate,
            FontEffectRibbonKind.Underline => selection.UnderlineIndeterminate,
            FontEffectRibbonKind.Strikethrough => selection.StrikethroughIndeterminate,
            FontEffectRibbonKind.SmallCaps => selection.SmallCapsIndeterminate,
            FontEffectRibbonKind.AllCaps => selection.AllCapsIndeterminate,
            FontEffectRibbonKind.Superscript => selection.SuperscriptIndeterminate,
            FontEffectRibbonKind.Subscript => selection.SubscriptIndeterminate,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
        return new RibbonCommandState(
            IsEnabled: isEnabled,
            IsChecked: !indeterminate && IsSet(kind, selection.Run));
    }

    public static IRibbonStatefulCommand CreateCommand(
        FontEffectRibbonKind kind,
        Action execute,
        Func<FontDialogSelectionState> getSelection,
        Func<bool>? isEnabled = null,
        Action? prepareExecution = null)
    {
        ArgumentNullException.ThrowIfNull(execute);
        ArgumentNullException.ThrowIfNull(getSelection);
        return new FreeWRibbonStatefulPortCommand(
            _ => execute(),
            () => GetState(kind, getSelection(), isEnabled?.Invoke() ?? true),
            prepareExecution);
    }

    private static bool IsSet(FontEffectRibbonKind kind, RunFormatting formatting) => kind switch
    {
        FontEffectRibbonKind.Bold => formatting.Bold,
        FontEffectRibbonKind.Italic => formatting.Italic,
        FontEffectRibbonKind.Underline => formatting.Underline,
        FontEffectRibbonKind.Strikethrough => formatting.Strikethrough,
        FontEffectRibbonKind.SmallCaps => formatting.SmallCaps,
        FontEffectRibbonKind.AllCaps => formatting.AllCaps,
        FontEffectRibbonKind.Superscript => formatting.VerticalAlign == VerticalAlign.Superscript,
        FontEffectRibbonKind.Subscript => formatting.VerticalAlign == VerticalAlign.Subscript,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };
}

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
