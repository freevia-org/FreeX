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
            AvaloniaRootRightMargin);
    }
}
