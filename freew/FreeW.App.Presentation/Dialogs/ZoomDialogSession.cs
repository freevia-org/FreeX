namespace FreeW.App.Presentation.Dialogs;

public enum ZoomDialogFocusTarget
{
    CustomPercent
}

public sealed record ZoomDialogControlState(
    ZoomDialogFitOption? FitOption,
    int? PresetPercent,
    string CustomPercentText)
{
    public bool IsCustomSelected => FitOption is null && PresetPercent is null;
}

public sealed record ZoomDialogValidation(
    ZoomDialogValidationError Error,
    string Message,
    ZoomDialogFocusTarget FocusTarget);

public sealed record ZoomDialogAcceptance(
    double? Result,
    ZoomDialogValidation? Validation,
    ZoomDialogControlState ControlState)
{
    public bool IsAccepted => Result is not null && Validation is null;
}

/// <summary>
/// Owns the neutral interaction state for the paired Zoom dialogs. Renderers translate native
/// checked/text events into semantic selections and apply the returned validation/focus projection.
/// </summary>
public sealed class ZoomDialogSession
{
    private ZoomDialogFitOption? _fitOption;
    private int? _presetPercent;
    private string _customPercentText;

    public ZoomDialogSession(double currentFactor)
    {
        InitialPlan = ZoomDialogPlanner.Build(currentFactor);
        _customPercentText = InitialPlan.CustomPercentText;
        _presetPercent = InitialPlan.Presets
            .FirstOrDefault(preset => preset.IsSelected)
            ?.Percent;
    }

    public ZoomDialogPlan InitialPlan { get; }

    public ZoomDialogControlState ControlState =>
        new(_fitOption, _presetPercent, _customPercentText);

    public void SelectFit(ZoomDialogFitOption fitOption)
    {
        _fitOption = fitOption;
        _presetPercent = null;
    }

    public void SelectPreset(int percent)
    {
        if (!ZoomDialogPlanner.IsPreset(percent))
            throw new ArgumentOutOfRangeException(nameof(percent), percent, "Unknown zoom preset.");

        _fitOption = null;
        _presetPercent = percent;
    }

    public void SelectCustom()
    {
        _fitOption = null;
        _presetPercent = null;
    }

    public void UpdateCustomPercentText(string? text)
    {
        _customPercentText = text ?? string.Empty;
        SelectCustom();
    }

    public ZoomDialogAcceptance PlanAcceptance(ZoomDialogFitFactors fitFactors)
    {
        ArgumentNullException.ThrowIfNull(fitFactors);

        var request = new ZoomDialogSelectionRequest(
            _fitOption,
            _presetPercent,
            _customPercentText);

        if (ZoomDialogPlanner.TryCreateResult(request, fitFactors, out var result, out var error))
            return new ZoomDialogAcceptance(result, Validation: null, ControlState);

        SelectCustom();
        var validationError = error ?? ZoomDialogValidationError.WholePercentRequired;
        return new ZoomDialogAcceptance(
            Result: null,
            new ZoomDialogValidation(
                validationError,
                ZoomDialogPlanner.ValidationMessageFor(validationError),
                ZoomDialogFocusTarget.CustomPercent),
            ControlState);
    }
}
