using System.Globalization;
using Free.Shared.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Ribbon;

public sealed class FreeWRibbonFormatPainterCommand(Action<bool> activate) : IRibbonCommand
{
    private readonly FormatPainterActivationSession _activation = new();

    public void Execute(RibbonCommandContext context) => activate(_activation.Activate());
}

public sealed class FreeWRibbonNumericValueCommand(
    Action<double> apply,
    Func<double> getValue,
    double minimumExclusive,
    NumberStyles numberStyles = NumberStyles.Any,
    Action? prepareExecution = null,
    CultureInfo? culture = null) : IRibbonStatefulCommand
{
    public void Execute(RibbonCommandContext context)
    {
        if (!TryGetSelectedValue(context, out var value)
            || !FreeWRibbonNumericValueParser.TryParseScalar(
                value,
                culture ?? CultureInfo.InvariantCulture,
                numberStyles,
                out var parsed)
            || parsed <= minimumExclusive)
        {
            return;
        }

        prepareExecution?.Invoke();
        apply(parsed);
    }

    public RibbonCommandState GetState() =>
        new(Value: getValue().ToString("0.##", CultureInfo.InvariantCulture));

    private static bool TryGetSelectedValue(RibbonCommandContext context, out string? value)
    {
        value = context.SelectedValue;
        if (value is not null)
            return true;

        if (context.Parameters.TryGetValue("value", out var legacyRaw))
            value = legacyRaw as string;

        return value is not null;
    }
}

public readonly record struct FreeWRibbonObjectPositionInput(
    double HorizontalOffsetPt,
    double VerticalOffsetPt,
    HorizontalAnchor HorizontalAnchor,
    VerticalAnchor VerticalAnchor);

public readonly record struct FreeWRibbonSizeInput(double WidthPt, double HeightPt);

public static class FreeWRibbonNumericValueParser
{
    public static bool TryParseScalar(
        string? value,
        CultureInfo culture,
        NumberStyles numberStyles,
        out double result)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return double.TryParse(value, numberStyles, culture, out result);
    }

    public static bool TryParseFontSize(
        string? value,
        CultureInfo culture,
        NumberStyles numberStyles,
        out double points) =>
        TryParseScalar(value, culture, numberStyles, out points) && points > 0;

    public static bool TryParseObjectPosition(
        string? value,
        CultureInfo culture,
        out FreeWRibbonObjectPositionInput input)
    {
        input = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var parts = value.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length < 2
            || !TryParseScalar(parts[0], culture, NumberStyles.Float, out var horizontalOffsetPt)
            || !TryParseScalar(parts[1], culture, NumberStyles.Float, out var verticalOffsetPt))
        {
            return false;
        }

        var horizontalAnchor = HorizontalAnchor.Column;
        var verticalAnchor = VerticalAnchor.Paragraph;
        if (parts.Length >= 3)
            Enum.TryParse(parts[2], ignoreCase: true, out horizontalAnchor);
        if (parts.Length >= 4)
            Enum.TryParse(parts[3], ignoreCase: true, out verticalAnchor);

        input = new FreeWRibbonObjectPositionInput(
            horizontalOffsetPt,
            verticalOffsetPt,
            horizontalAnchor,
            verticalAnchor);
        return true;
    }

    public static bool TryParseObjectSize(
        string? value,
        CultureInfo culture,
        out FreeWRibbonSizeInput input) =>
        TryParseSize(
            value,
            culture,
            [','],
            NumberStyles.Float,
            StringSplitOptions.TrimEntries,
            allowTrailingParts: true,
            out input);

    public static bool TryParseChartSize(
        string? value,
        CultureInfo culture,
        out FreeWRibbonSizeInput input) =>
        TryParseSize(
            value,
            culture,
            ['x', 'X'],
            NumberStyles.Float | NumberStyles.AllowThousands,
            StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries,
            allowTrailingParts: false,
            out input);

    private static bool TryParseSize(
        string? value,
        CultureInfo culture,
        char[] separators,
        NumberStyles numberStyles,
        StringSplitOptions splitOptions,
        bool allowTrailingParts,
        out FreeWRibbonSizeInput input)
    {
        input = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var parts = value.Split(separators, splitOptions);
        if (parts.Length < 2
            || (!allowTrailingParts && parts.Length != 2)
            || !TryParseScalar(parts[0], culture, numberStyles, out var widthPt)
            || !TryParseScalar(parts[1], culture, numberStyles, out var heightPt)
            || widthPt <= 0
            || heightPt <= 0)
        {
            return false;
        }

        input = new FreeWRibbonSizeInput(widthPt, heightPt);
        return true;
    }
}

public sealed class FreeWRibbonChoiceCommand(
    Action<string> apply,
    Func<string> getValue,
    Action<RibbonCommandState>? stateChanged = null) : IRibbonStatefulCommand
{
    public void Execute(RibbonCommandContext context)
    {
        var value = context.SelectedValue;
        if (value is null && context.Parameters.TryGetValue("value", out var legacyRaw))
            value = legacyRaw as string;
        if (string.IsNullOrWhiteSpace(value))
            return;

        apply(value);
        stateChanged?.Invoke(GetState());
    }

    public RibbonCommandState GetState() => new(Value: getValue());
}

public sealed class FreeWRibbonStatefulPortCommand(
    Action<RibbonCommandContext> execute,
    Func<RibbonCommandState> getState,
    Action? prepareExecution = null) : IRibbonStatefulCommand
{
    public void Execute(RibbonCommandContext context)
    {
        if (!getState().IsEnabled)
            return;

        prepareExecution?.Invoke();
        execute(context);
    }

    public RibbonCommandState GetState() => getState();
}

/// <summary>
/// Stateful ribbon command for native dialogs whose completion can be synchronous (WPF) or
/// asynchronous (Avalonia). The ribbon contract is synchronous, so incomplete operations resume on
/// the captured UI context just like an async event handler.
/// </summary>
public sealed class FreeWRibbonAsyncStatefulPortCommand(
    Func<RibbonCommandContext, ValueTask> executeAsync,
    Func<RibbonCommandState> getState,
    Action? prepareExecution = null) : IRibbonStatefulCommand
{
    public void Execute(RibbonCommandContext context)
    {
        if (!getState().IsEnabled)
            return;

        prepareExecution?.Invoke();
        var execution = executeAsync(context);
        if (execution.IsCompletedSuccessfully)
            execution.GetAwaiter().GetResult();
        else
            CompleteAsync(execution);
    }

    public RibbonCommandState GetState() => getState();

    private static async void CompleteAsync(ValueTask execution) =>
        await execution;
}
