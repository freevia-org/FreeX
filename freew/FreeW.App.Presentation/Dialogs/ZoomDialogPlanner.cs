using System.Globalization;
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

public static class ZoomDialogPlanner
{
    private static readonly int[] PresetValues = [200, 100, 75];

    public static IReadOnlyList<int> Presets => PresetValues;

    public static ZoomDialogPlan Build(double currentFactor)
    {
        var currentPercent = ZoomLevels.ToPercent(currentFactor);
        var matchedPreset = IsPreset(currentPercent);

        return new ZoomDialogPlan(
            currentPercent,
            currentPercent.ToString(CultureInfo.CurrentCulture),
            matchedPreset ? ZoomDialogInitialChoice.Preset : ZoomDialogInitialChoice.Custom,
            PresetValues
                .Select(percent => new ZoomDialogPresetPlan(percent, percent == currentPercent))
                .ToArray());
    }

    public static bool IsPreset(int percent) =>
        PresetValues.Contains(percent);

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

        if (!int.TryParse(input, NumberStyles.Integer, CultureInfo.CurrentCulture, out var percent))
        {
            error = ZoomDialogValidationError.WholePercentRequired;
            return false;
        }

        result = ZoomLevels.FromPercent(percent);
        return true;
    }

    private static double ResolveFit(ZoomDialogFitOption fitOption, ZoomDialogFitFactors fitFactors) =>
        fitOption switch
        {
            ZoomDialogFitOption.PageWidth => fitFactors.PageWidthFactor,
            ZoomDialogFitOption.TextWidth => fitFactors.TextWidthFactor,
            ZoomDialogFitOption.WholePage => fitFactors.WholePageFactor,
            _ => ZoomLevels.Default
        };
}
