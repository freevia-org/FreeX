namespace FreeX.App.Presentation.TextToColumns;

public sealed record TextToColumnsWizardSurfaceStepPlan(
    int Step,
    string InstructionKey,
    bool ShowOriginalDataTypePanel,
    bool ShowDelimiterPanel,
    bool ShowFixedWidthPanel,
    bool ShowColumnFormatPanel,
    bool ShowDestinationPanel,
    bool BackEnabled,
    bool NextEnabled,
    bool NextDefault,
    bool FinishDefault);

public sealed record TextToColumnsWizardSurfaceModePlan(
    bool DelimitedControlsEnabled,
    bool CustomDelimiterEnabled,
    bool FixedWidthControlsEnabled,
    double FixedWidthRulerOpacity);

public static class TextToColumnsWizardSurfacePlanner
{
    public const string HeaderFormatKey = "TextToColumns_TextWizardStepOf3";

    public static TextToColumnsWizardSurfaceStepPlan CreateStepPlan(int step, bool fixedWidth)
    {
        var normalizedStep = Math.Clamp(step, 1, 3);
        return new TextToColumnsWizardSurfaceStepPlan(
            Step: normalizedStep,
            InstructionKey: normalizedStep switch
            {
                1 => "TextToColumns_ChooseFileTypeInstruction",
                2 => "TextToColumns_ChooseDelimitersInstruction",
                _ => "TextToColumns_SelectColumnFormatAndDestinationInstruction"
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

    public static TextToColumnsWizardSurfaceModePlan CreateModePlan(bool fixedWidth, bool otherDelimiterSelected) =>
        new(
            DelimitedControlsEnabled: !fixedWidth,
            CustomDelimiterEnabled: !fixedWidth && otherDelimiterSelected,
            FixedWidthControlsEnabled: fixedWidth,
            FixedWidthRulerOpacity: fixedWidth ? 1.0 : 0.55);
}
