namespace Free.Shared.Shell;

/// <summary>Host-neutral About dialog content and measured host layout inputs.</summary>
public sealed record AboutDialogPresentation(
    string WindowTitle,
    string AboutText,
    string DialogAutomationId,
    string TextAutomationId,
    string OkAutomationId,
    string HelpText,
    double AvaloniaRootRightMargin = AboutDialogMetrics.RootMargin,
    double AvaloniaTextPaddingRight = AboutDialogMetrics.AvaloniaTextPaddingRight,
    double AvaloniaTextFontSize = AboutDialogMetrics.AvaloniaTextFontSize,
    double AvaloniaTextPaddingTop = AboutDialogMetrics.AvaloniaTextPaddingTop,
    bool AvaloniaDefaultButtonAccent = false,
    double AvaloniaTextLineHeight = AboutDialogMetrics.AvaloniaTextLineHeight);
