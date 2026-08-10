using Free.Shared.AppServices;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Dialogs;

public enum ZoomDialogInitialChoice
{
    Preset,
    Custom
}

public enum ZoomDialogFitOption
{
    PageWidth,
    TextWidth,
    WholePage
}

public enum ZoomDialogValidationError
{
    WholePercentRequired
}

public sealed record ZoomDialogPresetPlan(int Percent, bool IsSelected);

public sealed record ZoomDialogPlan(
    int CurrentPercent,
    string CustomPercentText,
    ZoomDialogInitialChoice InitialChoice,
    IReadOnlyList<ZoomDialogPresetPlan> Presets);

public sealed record ZoomDialogFitFactors(
    double PageWidthFactor,
    double TextWidthFactor,
    double WholePageFactor);

public sealed record ZoomDialogSelectionRequest(
    ZoomDialogFitOption? FitOption,
    int? PresetPercent,
    string? CustomPercentText);

public sealed record ZoomDialogTextSpec(
    string Title,
    string GroupLabel,
    string PageWidthLabel,
    string TextWidthLabel,
    string WholePageLabel,
    string PercentLabel,
    string CustomPercentAutomationName,
    string PercentSuffix);

public static class ZoomDialogPlanner
{
    private static readonly int[] PresetValues = [200, 100, 75];
    private static readonly ZoomPercentPolicy PercentPolicy = new(
        ZoomLevels.Min * 100d,
        ZoomLevels.Default * 100d,
        ZoomLevels.Max * 100d);

    public static ZoomDialogTextSpec Text { get; } = new(
        "Zoom",
        "Zoom to",
        "Page width",
        "Text width",
        "Whole page",
        "Percent:",
        "Custom zoom percent",
        "%");

    public static IReadOnlyList<int> Presets => PresetValues;

    public static string FormatPresetLabel(int percent) => $"{percent}%";

    public static ZoomDialogPlan Build(double currentFactor)
    {
        var currentPercent = PercentPolicy.NormalizeWholePercent(ZoomLevels.ToPercent(currentFactor));
        var matchedPreset = IsPreset(currentPercent);

        return new ZoomDialogPlan(
            currentPercent,
            PercentPolicy.FormatPercentText(currentPercent),
            matchedPreset ? ZoomDialogInitialChoice.Preset : ZoomDialogInitialChoice.Custom,
            PresetValues
                .Select(percent => new ZoomDialogPresetPlan(percent, percent == currentPercent))
                .ToArray());
    }

    public static bool IsPreset(int percent) =>
        PercentPolicy.IsPresetPercent(percent, PresetValues);

    public static ZoomDialogFitFactors BuildFitFactors(
        PageSettings page,
        double viewportWidthDip,
        double viewportHeightDip)
    {
        ArgumentNullException.ThrowIfNull(page);
        var (pageWidthDip, pageHeightDip) = PageLayout.PageSizeDip(page);
        var (contentWidthDip, _) = PageLayout.ContentAreaDip(page);
        var viewportWidth = Math.Max(0, viewportWidthDip);
        var viewportHeight = Math.Max(0, viewportHeightDip);
        return new ZoomDialogFitFactors(
            ZoomFit.PageWidth(pageWidthDip, viewportWidth),
            ZoomFit.TextWidth(contentWidthDip, viewportWidth),
            ZoomFit.WholePage(pageWidthDip, pageHeightDip, viewportWidth, viewportHeight));
    }

    public static bool TryCreateResult(
        ZoomDialogSelectionRequest request,
        ZoomDialogFitFactors fitFactors,
        out double result,
        out ZoomDialogValidationError? error)
    {
        ArgumentNullException.ThrowIfNull(request);

        result = ZoomLevels.Default;
        error = null;

        if (request.FitOption is { } fitOption)
        {
            result = ResolveFit(fitOption, fitFactors);
            return true;
        }

        if (request.PresetPercent is { } presetPercent)
        {
            result = ZoomLevels.FromPercent(presetPercent);
            return true;
        }

        return TryCreateCustomPercentResult(request.CustomPercentText, out result, out error);
    }

    public static bool TryCreateCustomPercentResult(
        string? input,
        out double result,
        out ZoomDialogValidationError? error)
    {
        result = ZoomLevels.Default;
        error = null;

        if (!PercentPolicy.TryParseWholePercent(input, out var percent))
        {
            error = ZoomDialogValidationError.WholePercentRequired;
            return false;
        }

        result = ZoomLevels.FromPercent(PercentPolicy.ClampPercent(percent));
        return true;
    }

    public static string ValidationMessageFor(ZoomDialogValidationError? error) =>
        error switch
        {
            ZoomDialogValidationError.WholePercentRequired => "Enter a whole zoom percentage.",
            _ => "Enter a whole zoom percentage."
        };

    private static double ResolveFit(ZoomDialogFitOption fitOption, ZoomDialogFitFactors fitFactors) =>
        fitOption switch
        {
            ZoomDialogFitOption.PageWidth => fitFactors.PageWidthFactor,
            ZoomDialogFitOption.TextWidth => fitFactors.TextWidthFactor,
            ZoomDialogFitOption.WholePage => fitFactors.WholePageFactor,
            _ => ZoomLevels.Default
        };
}
