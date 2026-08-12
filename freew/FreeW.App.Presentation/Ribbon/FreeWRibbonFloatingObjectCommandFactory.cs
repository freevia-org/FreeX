using System.Globalization;
using Free.Shared.Ribbon;

namespace FreeW.App.Presentation.Ribbon;

public sealed record FreeWRibbonFloatingObjectCommandPorts(
    Func<bool> HasSelection,
    Action<FreeWRibbonObjectPositionInput> ApplyPosition,
    Action<double, double> ApplySize,
    Action? OpenPositionDialog = null,
    Action? OpenSizeDialog = null,
    Action? PrepareExecution = null);

/// <summary>
/// Owns selection gating, value parsing, preset execution, and dialog routing for the
/// image and shape position/size command families. Renderers provide only native ports.
/// </summary>
public static class FreeWRibbonFloatingObjectCommandFactory
{
    public static IRibbonStatefulCommand CreatePosition(
        FreeWRibbonFloatingObjectCommandPorts ports)
    {
        ArgumentNullException.ThrowIfNull(ports);
        return new FreeWRibbonStatefulPortCommand(
            context =>
            {
                ports.PrepareExecution?.Invoke();
                if (!ports.HasSelection())
                    return;

                if (FreeWRibbonNumericValueParser.TryParseObjectPosition(
                        context.SelectedValue,
                        CultureInfo.InvariantCulture,
                        out var position))
                {
                    ports.ApplyPosition(position);
                }
                else
                {
                    ports.OpenPositionDialog?.Invoke();
                }
            },
            () => new RibbonCommandState(IsEnabled: ports.HasSelection()));
    }

    public static IRibbonStatefulCommand CreatePositionPreset(
        FreeWRibbonFloatingObjectCommandPorts ports,
        FreeWRibbonObjectPositionInput position)
    {
        ArgumentNullException.ThrowIfNull(ports);
        return new FreeWRibbonStatefulPortCommand(
            _ =>
            {
                ports.PrepareExecution?.Invoke();
                if (ports.HasSelection())
                    ports.ApplyPosition(position);
            },
            () => new RibbonCommandState(IsEnabled: ports.HasSelection()));
    }

    public static IRibbonStatefulCommand CreateSize(
        FreeWRibbonFloatingObjectCommandPorts ports)
    {
        ArgumentNullException.ThrowIfNull(ports);
        return new FreeWRibbonStatefulPortCommand(
            context =>
            {
                ports.PrepareExecution?.Invoke();
                if (!ports.HasSelection())
                    return;

                if (FreeWRibbonNumericValueParser.TryParseObjectSize(
                        context.SelectedValue,
                        CultureInfo.InvariantCulture,
                        out var size))
                {
                    ports.ApplySize(size.WidthPt, size.HeightPt);
                }
                else if (string.IsNullOrWhiteSpace(context.SelectedValue))
                {
                    ports.OpenSizeDialog?.Invoke();
                }
            },
            () => new RibbonCommandState(IsEnabled: ports.HasSelection()));
    }

    public static IRibbonStatefulCommand CreateSizePreset(
        FreeWRibbonFloatingObjectCommandPorts ports,
        double widthPt,
        double heightPt)
    {
        ArgumentNullException.ThrowIfNull(ports);
        return new FreeWRibbonStatefulPortCommand(
            _ =>
            {
                ports.PrepareExecution?.Invoke();
                if (ports.HasSelection())
                    ports.ApplySize(widthPt, heightPt);
            },
            () => new RibbonCommandState(IsEnabled: ports.HasSelection()));
    }
}
