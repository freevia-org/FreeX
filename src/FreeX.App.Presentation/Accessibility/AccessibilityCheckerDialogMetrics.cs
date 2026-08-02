namespace FreeX.App.Presentation.Accessibility;

/// <summary>
/// Pixel-facing dimensions shared by the WPF dialog, Avalonia dialog, and their parity capture paths.
/// Keeping these values in Presentation prevents platform shells from slowly growing different dialog
/// footprints while leaving the control implementations platform-native.
/// </summary>
public static class AccessibilityCheckerDialogMetrics
{
    public const int Width = 360;
    public const int Height = 520;
    public const double ContentMargin = 16;
    public const double TitleFontSize = 16;
    public const double BodyFontSize = 12;
    public const double ResultsTreeTop = 70;
    public const double ResultsTreeHeight = 176;
    public const double ResultsTreeWidth = 328;
    public const double AdditionalInformationTop = 262;
    public const double StatusTop = 426;
    public const double ButtonDividerTop = 458;
    public const double ActionButtonTop = 474;
    public const double ActionButtonWidth = 76;
    public const double ActionButtonHeight = 26;
    public const double ActionButtonSpacing = 16;
}
