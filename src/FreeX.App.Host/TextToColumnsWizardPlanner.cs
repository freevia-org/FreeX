namespace FreeX.App.Host;

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
        var surface = TextToColumnsWizardSurfacePlanner.CreateStepPlan(step, fixedWidth);
        var normalizedStep = surface.Step;
        return new TextToColumnsWizardStepPlan(
            Header: UiText.Format("TextToColumns_TextWizardStepOf3", normalizedStep),
            Instruction: UiText.Get(surface.InstructionKey),
            ShowOriginalDataTypePanel: surface.ShowOriginalDataTypePanel,
            ShowDelimiterPanel: surface.ShowDelimiterPanel,
            ShowFixedWidthPanel: surface.ShowFixedWidthPanel,
            ShowColumnFormatPanel: surface.ShowColumnFormatPanel,
            ShowDestinationPanel: surface.ShowDestinationPanel,
            BackEnabled: surface.BackEnabled,
            NextEnabled: surface.NextEnabled,
            NextDefault: surface.NextDefault,
            FinishDefault: surface.FinishDefault);
    }

    public static TextToColumnsWizardModePlan CreateModePlan(bool fixedWidth, bool otherDelimiterSelected)
    {
        var surface = TextToColumnsWizardSurfacePlanner.CreateModePlan(fixedWidth, otherDelimiterSelected);
        return new TextToColumnsWizardModePlan(
            surface.DelimitedControlsEnabled,
            surface.CustomDelimiterEnabled,
            surface.FixedWidthControlsEnabled,
            surface.FixedWidthRulerOpacity);
    }
}
