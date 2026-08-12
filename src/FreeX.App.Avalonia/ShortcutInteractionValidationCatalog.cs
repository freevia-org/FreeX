using FreeX.App.Avalonia.Ribbon;
using FreeX.App.Presentation.InteractionValidation;
using FreeX.App.Presentation.Shell;
using Avalonia.Input;

namespace FreeX.App.Avalonia;

internal static class ShortcutInteractionValidationCatalog
{
    internal static bool TryResolveAvaloniaHostInteraction(
        ShortcutInteractionDescriptor interaction,
        out Key key,
        out KeyModifiers modifiers,
        out KeyboardCommandShortcut shortcut)
    {
        key = default;
        modifiers = KeyModifiers.None;
        shortcut = default;
        if (interaction.Steps.Count != 1 ||
            !TryMapAvaloniaKey(interaction.Steps[0].Key, out key))
        {
            return false;
        }

        var sourceModifiers = interaction.Steps[0].Modifiers;
        if (sourceModifiers.HasFlag(ShortcutModifierKeys.Control)) modifiers |= KeyModifiers.Control;
        if (sourceModifiers.HasFlag(ShortcutModifierKeys.Shift)) modifiers |= KeyModifiers.Shift;
        if (sourceModifiers.HasFlag(ShortcutModifierKeys.Alt)) modifiers |= KeyModifiers.Alt;
        if (sourceModifiers.HasFlag(ShortcutModifierKeys.Meta)) modifiers |= KeyModifiers.Meta;
        return MainWindow.TryResolveApplicationShortcutForTest(key, modifiers, out shortcut);
    }

    internal static bool TryMapAvaloniaGesture(
        ShortcutGestureStep step,
        out Key key,
        out KeyModifiers modifiers)
    {
        modifiers = KeyModifiers.None;
        if (!TryMapAvaloniaKey(step.Key, out key))
            return false;

        if (step.Modifiers.HasFlag(ShortcutModifierKeys.Control)) modifiers |= KeyModifiers.Control;
        if (step.Modifiers.HasFlag(ShortcutModifierKeys.Shift)) modifiers |= KeyModifiers.Shift;
        if (step.Modifiers.HasFlag(ShortcutModifierKeys.Alt)) modifiers |= KeyModifiers.Alt;
        if (step.Modifiers.HasFlag(ShortcutModifierKeys.Meta)) modifiers |= KeyModifiers.Meta;
        return true;
    }

    internal static bool TryResolveRibbonKeytipInteraction(
        ShortcutInteractionDescriptor interaction,
        out string route)
    {
        route = "";
        if (interaction.Kind != ShortcutInteractionKind.RibbonKeytipSequence ||
            !TryParseDisplayTokens(interaction.DisplayText, out var tokens))
        {
            return false;
        }

        if (!AvaloniaRibbonKeyTipRoutes.TryResolveExact(string.Concat(tokens), out var resolved))
            return false;

        route = resolved.RouteName;
        return true;
    }

    private static bool TryParseDisplayTokens(string displayText, out IReadOnlyList<string> tokens)
    {
        const string twoStepPrefix = "Alt, then ";
        if (displayText.StartsWith(twoStepPrefix, StringComparison.Ordinal))
        {
            tokens = [displayText[twoStepPrefix.Length..]];
            return true;
        }

        const string sequencePrefix = "Alt,";
        if (displayText.StartsWith(sequencePrefix, StringComparison.Ordinal))
        {
            tokens = displayText[sequencePrefix.Length..]
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return tokens.Count > 0;
        }

        tokens = [];
        return false;
    }

    private static bool TryMapAvaloniaKey(string source, out Key key)
    {
        var name = source switch
        {
            "Alt" => nameof(Key.LeftAlt),
            "Backspace" => nameof(Key.Back),
            "ArrowUp" => nameof(Key.Up),
            "ArrowDown" => nameof(Key.Down),
            "Page Up" => nameof(Key.PageUp),
            "Page Down" => nameof(Key.PageDown),
            "Grave" => nameof(Key.Oem3),
            "Plus" => nameof(Key.OemPlus),
            "Menu" => nameof(Key.Apps),
            "Semicolon" => nameof(Key.OemSemicolon),
            "0" => nameof(Key.D0),
            "1" => nameof(Key.D1),
            "2" => nameof(Key.D2),
            "3" => nameof(Key.D3),
            "4" => nameof(Key.D4),
            "5" => nameof(Key.D5),
            "6" => nameof(Key.D6),
            "7" => nameof(Key.D7),
            "8" => nameof(Key.D8),
            "9" => nameof(Key.D9),
            "ArrowRight" => nameof(Key.Right),
            "ArrowLeft" => nameof(Key.Left),
            "Equals" => nameof(Key.OemPlus),
            "NumpadPlus" => nameof(Key.Add),
            "Minus" => nameof(Key.OemMinus),
            "NumpadMinus" => nameof(Key.Subtract),
            "Quote" => nameof(Key.OemQuotes),
            "Period" => nameof(Key.OemPeriod),
            "Decimal" => nameof(Key.Decimal),
            "OpenBracket" => nameof(Key.OemOpenBrackets),
            "CloseBracket" => nameof(Key.OemCloseBrackets),
            _ => source,
        };
        return Enum.TryParse(name, ignoreCase: true, out key);
    }
}
