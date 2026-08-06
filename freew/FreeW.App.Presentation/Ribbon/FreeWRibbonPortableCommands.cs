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
    Action? prepareExecution = null) : IRibbonStatefulCommand
{
    public void Execute(RibbonCommandContext context)
    {
        if (!TryGetSelectedValue(context, out var value)
            || !double.TryParse(value, numberStyles, CultureInfo.InvariantCulture, out var parsed)
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
