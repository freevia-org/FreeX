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

/// <summary>
/// r604: the value here is TYPED BY THE USER, so it is read in the user's locale.
///
/// <para>This defaulted to <see cref="CultureInfo.InvariantCulture"/>, so the line-spacing box --
/// its only two call sites, one per host -- silently discarded <c>1,5</c> for every comma-decimal
/// user on BOTH platforms. The two hosts also disagreed about <see cref="NumberStyles"/>: the WPF
/// site passed <c>Float | AllowThousands</c> and the Avalonia site took the <c>Any</c> default, so
/// the same box accepted different text depending on the platform.</para>
///
/// <para>The default is now <see cref="LocalizedNumberEntry"/> with <see cref="NumberStyles.Float"/>,
/// matching what FreeP's animation-duration field already settled for exactly this defect: Float
/// excludes AllowThousands, so <c>1.5</c> cannot be misread as fifteen on a culture whose group
/// separator is <c>.</c> -- it fails there and falls through to the invariant reading instead. An
/// explicit <paramref name="culture"/> still wins, for callers that genuinely have one.</para>
/// </summary>
public sealed class FreeWRibbonNumericValueCommand(
    Action<double> apply,
    Func<double> getValue,
    double minimumExclusive,
    NumberStyles numberStyles = NumberStyles.Float,
    Action? prepareExecution = null,
    CultureInfo? culture = null) : IRibbonStatefulCommand
{
    private bool TryParseTypedValue(string value, out double parsed) =>
        culture is null
            ? LocalizedNumberEntry.TryParse(value, numberStyles, out parsed)
            : FreeWRibbonNumericValueParser.TryParseScalar(value, culture, numberStyles, out parsed);

    public void Execute(RibbonCommandContext context)
    {
        var value = FreeWRibbonSelectedValue.Resolve(context);
        if (value is null
            || !TryParseTypedValue(value, out var parsed)
            || !double.IsFinite(parsed)
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

    /// <summary>
    /// r604: the font-size box, read the way the user typed it.
    ///
    /// <para>The two hosts disagreed: the WPF ribbon passed <see cref="CultureInfo.CurrentCulture"/>
    /// and the Avalonia ribbon passed <see cref="CultureInfo.InvariantCulture"/>, so the SAME box in
    /// the SAME app accepted "10,5" on Windows and silently ignored it on Linux and macOS. Neither
    /// host should be choosing: a typed number follows the user's locale, and
    /// <see cref="LocalizedNumberEntry"/> keeps the invariant spelling working as a guarded
    /// fallback.</para>
    /// </summary>
    public static bool TryParseTypedFontSize(string? value, out double points) =>
        LocalizedNumberEntry.TryParse(value, NumberStyles.Float, out points)
        && double.IsFinite(points)
        && points > 0;

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
