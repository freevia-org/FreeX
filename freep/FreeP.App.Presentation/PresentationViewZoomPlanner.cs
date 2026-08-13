namespace FreeP.App.Compositor;

public enum PresentationViewZoomCommandKind
{
    Zoom,
    FitToWindow
}

public enum PresentationViewZoomMode
{
    FitToWindow,
    Percent
}

public readonly record struct PresentationViewZoomState(
    PresentationViewZoomMode Mode,
    int ZoomPercent)
{
    public static PresentationViewZoomState FitToWindow { get; } = new(
        PresentationViewZoomMode.FitToWindow,
        PresentationViewZoomPlanner.DefaultZoomPercent);
}

public readonly record struct PresentationViewZoomCommandPlan(
    string CommandId,
    PresentationViewZoomCommandKind Kind);

public readonly record struct PresentationViewZoomCommandResult(
    PresentationViewZoomState State,
    PresentationViewZoomCommandKind Kind,
    double StageScaleMultiplier,
    bool RequestsZoomDialog);

public static class PresentationViewZoomPlanner
{
    public const string ZoomCommandId = "freep.view.zoom";
    public const string FitToWindowCommandId = "freep.view.fit-to-window";

    public const int MinimumZoomPercent = 10;
    public const int MaximumZoomPercent = 400;
    public const int DefaultZoomPercent = 100;

    public static IReadOnlyList<int> PresetZoomPercents { get; } =
        [25, 33, 50, 66, 75, 100, 125, 150, 200, 400];

    public static IReadOnlyList<PresentationViewZoomCommandPlan> BuiltInPlans { get; } =
        [
            new(ZoomCommandId, PresentationViewZoomCommandKind.Zoom),
            new(FitToWindowCommandId, PresentationViewZoomCommandKind.FitToWindow),
        ];

    public static bool TryGetKind(string commandId, out PresentationViewZoomCommandKind kind)
    {
        switch (commandId)
        {
            case ZoomCommandId:
                kind = PresentationViewZoomCommandKind.Zoom;
                return true;
            case FitToWindowCommandId:
                kind = PresentationViewZoomCommandKind.FitToWindow;
                return true;
            default:
                kind = default;
                return false;
        }
    }

    public static bool TryBuildPlan(string commandId, out PresentationViewZoomCommandPlan plan)
    {
        if (!TryGetKind(commandId, out var kind))
        {
            plan = default;
            return false;
        }

        plan = new PresentationViewZoomCommandPlan(commandId, kind);
        return true;
    }

    public static int NormalizeZoomPercent(int percent) =>
        Math.Clamp(percent, MinimumZoomPercent, MaximumZoomPercent);

    public static int NormalizeZoomPercent(double percent)
    {
        if (double.IsNaN(percent) || double.IsInfinity(percent))
            return DefaultZoomPercent;

        return NormalizeZoomPercent((int)Math.Round(percent, MidpointRounding.AwayFromZero));
    }

    public static bool TryParseZoomPercent(string? value, out int percent)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            percent = default;
            return false;
        }

        var trimmed = value.Trim().TrimEnd('%').Trim();
        if (!double.TryParse(trimmed, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
        {
            percent = default;
            return false;
        }

        percent = NormalizeZoomPercent(parsed);
        return true;
    }

    public static PresentationViewZoomCommandResult Execute(
        PresentationViewZoomState state,
        PresentationViewZoomCommandPlan plan,
        string? selectedValue = null)
    {
        var next = plan.Kind switch
        {
            PresentationViewZoomCommandKind.Zoom => state with
            {
                Mode = PresentationViewZoomMode.Percent,
                ZoomPercent = TryParseZoomPercent(selectedValue, out var parsed)
                    ? parsed
                    : NormalizeZoomPercent(state.ZoomPercent <= 0 ? DefaultZoomPercent : state.ZoomPercent),
            },
            PresentationViewZoomCommandKind.FitToWindow => state with
            {
                Mode = PresentationViewZoomMode.FitToWindow,
                ZoomPercent = NormalizeZoomPercent(state.ZoomPercent <= 0 ? DefaultZoomPercent : state.ZoomPercent),
            },
            _ => state
        };

        return new PresentationViewZoomCommandResult(
            next,
            plan.Kind,
            StageScaleMultiplierFor(next),
            RequestsZoomDialog: plan.Kind == PresentationViewZoomCommandKind.Zoom);
    }

    public static bool TryExecute(
        PresentationViewZoomState state,
        string commandId,
        string? selectedValue,
        out PresentationViewZoomCommandResult result)
    {
        if (!TryBuildPlan(commandId, out var plan))
        {
            result = default;
            return false;
        }

        result = Execute(state, plan, selectedValue);
        return true;
    }

    public static double StageScaleMultiplierFor(PresentationViewZoomState state) =>
        state.Mode == PresentationViewZoomMode.FitToWindow
            ? 1.0
            : NormalizeZoomPercent(state.ZoomPercent) / 100.0;

    public static SlideTransformCore PlanStageTransform(
        double renderWidth,
        double renderHeight,
        double slideWidthDip,
        double slideHeightDip,
        PresentationViewZoomState state)
    {
        var fit = SlideTransformCore.Compute(
            renderWidth,
            renderHeight,
            slideWidthDip,
            slideHeightDip);
        double multiplier = StageScaleMultiplierFor(state);
        if (Math.Abs(multiplier - 1.0) < 0.0001)
            return fit;

        double scale = fit.Scale * multiplier;
        return new SlideTransformCore(
            scale,
            (renderWidth - slideWidthDip * scale) / 2.0,
            (renderHeight - slideHeightDip * scale) / 2.0,
            slideWidthDip,
            slideHeightDip);
    }
}
