namespace Free.Shared.Shell;

public static class ZoomDialogVisualMetrics
{
    public const double WindowWidth = 300;
    public const double WindowHeight = 240;
    public const double OuterPadding = 12;
    public const double MagnificationGroupPadding = 8;
    public const double MagnificationGroupBottomMargin = 12;
    public const double PresetColumnWidth = 96;
    public const double PresetItemBottomMargin = 4;
    public const double FitSelectionBottomMargin = 10;
    public const double CustomPercentBoxWidth = 72;
    public const double CustomPercentBoxHeight = 24;
    public const double ActionButtonWidth = 72;
    public const double AvaloniaControlHeight = 20;
    public const double AvaloniaCompactRadioButtonHeight = 16;
    public const double AvaloniaButtonHeight = 22;

    public const string DialogAutomationId = "ZoomDialog";
    public const string CustomButtonAutomationId = "ZoomCustomButton";
    public const string FitSelectionButtonAutomationId = "ZoomFitSelectionButton";
    public const string CustomPercentBoxAutomationId = "ZoomCustomPercentBox";
    public const string ValidationTextAutomationId = "ZoomValidationText";
    public const string OkButtonAutomationId = "ZoomOkButton";
    public const string CancelButtonAutomationId = "ZoomCancelButton";

    public static string PresetButtonAutomationId(int percent) => $"ZoomPreset{percent}Button";
}
