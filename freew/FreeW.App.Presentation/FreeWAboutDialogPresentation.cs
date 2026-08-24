using System.Reflection;
using Free.Shared.Shell;

namespace FreeW.App.Presentation;

/// <summary>Shared FreeW About content contract for both desktop hosts.</summary>
public static class FreeWAboutDialogPresentation
{
    public const string WindowTitle = "About FreeW";
    public const string DialogAutomationId = "AboutFreeWDialog";
    public const string TextAutomationId = "AboutFreeWText";
    public const string OkAutomationId = "AboutFreeWOkButton";
    public const string HelpText = "View version, license, privacy, and source information about FreeW.";

    // The WPF authority paints the final content pixel at x=528 in the 560x600
    // harness frame. Avalonia needs the measured one-DIP right-edge reserve.
    public const double AvaloniaRootRightMargin = AboutDialogMetrics.FreeWAvaloniaRootRightMargin;
    // WPF's native About TextBox keeps its standard 8-DIP right inset. Keep the shared
    // Avalonia default unchanged for other products and pass FreeW's measured inset here.
    public const double AvaloniaTextPaddingRight = AboutDialogMetrics.TextPadding;
    public const double AvaloniaTextFontSize = AboutDialogMetrics.TextFontSize;
    public const double AvaloniaTextPaddingTop = AboutDialogMetrics.TextPadding + 1;
    public const bool AvaloniaDefaultButtonAccent = true;
    // WPF's 12px About TextBox advances its wrapped lines at 16 device pixels.
    // Keeping the Avalonia line box at that measured cadence prevents the centered
    // document from drifting upward at the first paragraph and downward by the last.
    public const double AvaloniaTextLineHeight = 16.0;

    public static AboutDialogPresentation Create(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        return new AboutDialogPresentation(
            WindowTitle,
            FreeWProductInfo.CreateAboutText(assembly),
            DialogAutomationId,
            TextAutomationId,
            OkAutomationId,
            HelpText,
            AvaloniaRootRightMargin,
            AvaloniaTextPaddingRight,
            AvaloniaTextFontSize,
            AvaloniaTextPaddingTop,
            AvaloniaDefaultButtonAccent,
            AvaloniaTextLineHeight);
    }
}
