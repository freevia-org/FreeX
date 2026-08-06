using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>Native rich-text and table-editor endpoints supplied by a renderer host.</summary>
public sealed class FreePRibbonTextActionEndpoints
{
    public Func<TableCellTextFormatKind, bool>? ToggleFormat { get; init; }
    public Func<TextAlign, bool>? SetParagraphAlignment { get; init; }
    public Func<TableCellListPresetDescriptor, bool>? ApplyListPreset { get; init; }
    public Func<bool>? ToggleBullets { get; init; }
    public Func<bool>? ToggleNumbering { get; init; }
    public Func<bool>? Indent { get; init; }
    public Func<bool>? Outdent { get; init; }
    public Func<string, bool>? SetFontFamily { get; init; }
    public Func<double, bool>? SetFontSize { get; init; }
    public Func<ThemeAwareColor?, bool>? SetColor { get; init; }
    public Func<TextVerticalType, bool>? SetTextVerticalType { get; init; }
    public Func<ThemeAwareColor?, bool>? SetTableCellFill { get; init; }
    public Func<TableCellAnchor?, bool>? SetTableCellAnchor { get; init; }
    public Func<TableCellBorderSide, ShapeOutline?, bool>? SetTableCellBorder { get; init; }
    public Func<TableCellInsetSide, double?, bool>? SetTableCellInset { get; init; }
    public Func<long, bool>? SetTableRowHeight { get; init; }
    public Func<bool>? RemoveHyperlink { get; init; }
}

/// <summary>Exhaustive typed dispatch from portable ribbon text actions to native editor ports.</summary>
public static class FreePRibbonTextActionDispatcher
{
    public static bool Dispatch(
        FreePRibbonTextAction action,
        FreePRibbonTextActionEndpoints endpoints)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(endpoints);

        return action.Kind switch
        {
            FreePRibbonTextActionKind.ToggleFormat =>
                Invoke(action.Argument, endpoints.ToggleFormat),
            FreePRibbonTextActionKind.SetParagraphAlignment =>
                Invoke(action.Argument, endpoints.SetParagraphAlignment),
            FreePRibbonTextActionKind.ApplyListPreset =>
                Invoke(action.Argument, endpoints.ApplyListPreset),
            FreePRibbonTextActionKind.ToggleBullets => Invoke(endpoints.ToggleBullets),
            FreePRibbonTextActionKind.ToggleNumbering => Invoke(endpoints.ToggleNumbering),
            FreePRibbonTextActionKind.Indent => Invoke(endpoints.Indent),
            FreePRibbonTextActionKind.Outdent => Invoke(endpoints.Outdent),
            FreePRibbonTextActionKind.SetFontFamily =>
                Invoke(action.Argument, endpoints.SetFontFamily),
            FreePRibbonTextActionKind.SetFontSize =>
                Invoke(action.Argument, endpoints.SetFontSize),
            FreePRibbonTextActionKind.SetColor =>
                InvokeNullableReference(action.Argument, endpoints.SetColor),
            FreePRibbonTextActionKind.SetTextVerticalType =>
                Invoke(action.Argument, endpoints.SetTextVerticalType),
            FreePRibbonTextActionKind.SetTableCellFill =>
                InvokeNullableReference(action.Argument, endpoints.SetTableCellFill),
            FreePRibbonTextActionKind.SetTableCellAnchor =>
                InvokeNullableValue(action.Argument, endpoints.SetTableCellAnchor),
            FreePRibbonTextActionKind.SetTableCellBorder =>
                InvokeBorder(action, endpoints.SetTableCellBorder),
            FreePRibbonTextActionKind.SetTableCellInset =>
                InvokeInset(action, endpoints.SetTableCellInset),
            FreePRibbonTextActionKind.SetTableRowHeight =>
                Invoke(action.Argument, endpoints.SetTableRowHeight),
            FreePRibbonTextActionKind.RemoveHyperlink => Invoke(endpoints.RemoveHyperlink),
            _ => false,
        };
    }

    private static bool Invoke(Func<bool>? endpoint) => endpoint?.Invoke() == true;

    private static bool Invoke<T>(object? argument, Func<T, bool>? endpoint) =>
        endpoint is not null && argument is T typedArgument && endpoint(typedArgument);

    private static bool InvokeNullableReference<T>(object? argument, Func<T?, bool>? endpoint)
        where T : class =>
        endpoint is not null &&
        (argument is null || argument is T) &&
        endpoint((T?)argument);

    private static bool InvokeNullableValue<T>(object? argument, Func<T?, bool>? endpoint)
        where T : struct =>
        endpoint is not null &&
        (argument is null || argument is T) &&
        endpoint(argument is T value ? value : null);

    private static bool InvokeBorder(
        FreePRibbonTextAction action,
        Func<TableCellBorderSide, ShapeOutline?, bool>? endpoint) =>
        endpoint is not null &&
        action.Argument is TableCellBorderSide side &&
        (action.SecondaryArgument is null || action.SecondaryArgument is ShapeOutline) &&
        endpoint(side, (ShapeOutline?)action.SecondaryArgument);

    private static bool InvokeInset(
        FreePRibbonTextAction action,
        Func<TableCellInsetSide, double?, bool>? endpoint) =>
        endpoint is not null &&
        action.Argument is TableCellInsetSide side &&
        (action.SecondaryArgument is null || action.SecondaryArgument is double) &&
        endpoint(side, action.SecondaryArgument is double value ? value : null);
}
