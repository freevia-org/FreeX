using System.Globalization;
using Free.Shared.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Ribbon;

internal static class FreeWRibbonSelectedValue
{
    public static string? Resolve(RibbonCommandContext context)
    {
        if (context.SelectedValue is { } selectedValue)
            return selectedValue;

        return context.Parameters.TryGetValue("value", out var legacyRaw)
            ? legacyRaw as string
            : null;
    }
}

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
        var value = FreeWRibbonSelectedValue.Resolve(context);
        if (value is null
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
        new(Value: FreeWRibbonNumericValueParser.FormatInvariant(getValue()));

}

public readonly record struct FreeWRibbonObjectPositionInput(
    double HorizontalOffsetPt,
    double VerticalOffsetPt,
    HorizontalAnchor HorizontalAnchor,
    VerticalAnchor VerticalAnchor);

public readonly record struct FreeWRibbonSizeInput(double WidthPt, double HeightPt);

public static class FreeWRibbonNumericValueParser
{
    public static string FormatInvariant(double value) =>
        value.ToString("0.##", CultureInfo.InvariantCulture);

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
        var value = FreeWRibbonSelectedValue.Resolve(context);
        if (string.IsNullOrWhiteSpace(value))
            return;

        apply(value);
        stateChanged?.Invoke(GetState());
    }

    public RibbonCommandState GetState() => new(Value: getValue());
}

public sealed class FreeWRibbonParagraphValueCommand(
    FreeWRibbonFormattingSession session,
    FreeWParagraphValueKind kind) : IRibbonStatefulCommand
{
    public void Execute(RibbonCommandContext context) =>
        session.ApplyParagraphValue(kind, FreeWRibbonSelectedValue.Resolve(context));

    public RibbonCommandState GetState() =>
        new(Value: session.CurrentParagraphValue(kind));
}

public sealed class FreeWRibbonParagraphStyleCommand(
    FreeWRibbonFormattingSession session) : IRibbonStatefulCommand
{
    public void Execute(RibbonCommandContext context) =>
        session.ApplyParagraphStyle(FreeWRibbonSelectedValue.Resolve(context));

    public RibbonCommandState GetState() =>
        new(Value: session.CurrentParagraphStyleName());
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

    // Runs as the continuation of a dialog ValueTask that did not complete synchronously (the
    // normal case for a real modal dialog). This method is async void because there is nothing to
    // await it from -- Execute() has already returned to the ribbon click handler by the time this
    // resumes. An exception escaping here would therefore become an unhandled exception on the
    // async void continuation, which tears the whole process down (Avalonia has no dispatcher-level
    // unhandled-exception hook; see RibbonCommandFaultReporter). Catch and report instead, matching
    // the synchronous Execute() path's own guard in AvaloniaRibbonRenderer.Execute.
    private static async void CompleteAsync(ValueTask execution)
    {
        try
        {
            await execution;
        }
        catch (Exception ex)
        {
            RibbonCommandFaultReporter.Report(ex, nameof(FreeWRibbonAsyncStatefulPortCommand));
        }
    }
}
