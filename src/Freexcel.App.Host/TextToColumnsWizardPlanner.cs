namespace Freexcel.App.Host;

public sealed record TextToColumnsWizardStepPlan(
    string Header,
    string Instruction,
    bool ShowOriginalDataTypePanel,
    bool ShowDelimiterPanel,
    bool ShowFixedWidthPanel,
    bool ShowColumnFormatPanel,
    bool ShowDestinationPanel,
    bool BackEnabled,
    bool NextEnabled,
    bool NextDefault,
    bool FinishDefault);

public sealed record TextToColumnsWizardModePlan(
    bool DelimitedControlsEnabled,
    bool CustomDelimiterEnabled,
    bool FixedWidthControlsEnabled,
    double FixedWidthRulerOpacity);

public static class TextToColumnsWizardPlanner
{
    public static TextToColumnsWizardStepPlan CreateStepPlan(int step, bool fixedWidth)
    {
        var normalizedStep = Math.Clamp(step, 1, 3);
        return new TextToColumnsWizardStepPlan(
            Header: $"Text Wizard - Step {normalizedStep} of 3",
            Instruction: normalizedStep switch
            {
                1 => "Choose the file type that best describes your data.",
                2 => "Choose the delimiters that separate your selected text.",
                _ => "Select each column and set the data format and destination."
            },
            ShowOriginalDataTypePanel: normalizedStep == 1,
            ShowDelimiterPanel: normalizedStep == 2 && !fixedWidth,
            ShowFixedWidthPanel: normalizedStep == 2 && fixedWidth,
            ShowColumnFormatPanel: normalizedStep == 3,
            ShowDestinationPanel: normalizedStep == 3,
            BackEnabled: normalizedStep > 1,
            NextEnabled: normalizedStep < 3,
            NextDefault: normalizedStep < 3,
            FinishDefault: normalizedStep == 3);
    }

    public static TextToColumnsWizardModePlan CreateModePlan(bool fixedWidth, bool otherDelimiterSelected) =>
        new(
            DelimitedControlsEnabled: !fixedWidth,
            CustomDelimiterEnabled: !fixedWidth && otherDelimiterSelected,
            FixedWidthControlsEnabled: fixedWidth,
            FixedWidthRulerOpacity: fixedWidth ? 1.0 : 0.55);
}
