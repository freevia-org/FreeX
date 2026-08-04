using System.Reflection;
using Free.Shared.Shell;

namespace FreeX.App.Services;

/// <summary>FreeX About content and host-neutral dialog configuration for both desktop hosts.</summary>
public static class FreeXAboutDialogPresentation
{
    public const string WindowTitle = "About FreeX";
    public const string DialogAutomationId = "AboutFreeXDialog";
    public const string TextAutomationId = "AboutFreeXText";
    public const string OkAutomationId = "AboutFreeXOkButton";
    public const string HelpText = "View version and license information about FreeX.";

    public static AboutDialogPresentation Create(
        Assembly assembly,
        string uiFramework,
        string? windowTitle = null,
        string? helpText = null)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentException.ThrowIfNullOrWhiteSpace(uiFramework);

        var versionText = AppHelpInfo.GetVersionText(assembly);
        var aboutText = string.Equals(uiFramework, "WPF", StringComparison.Ordinal)
            ? AppHelpInfo.BuildWpfAboutText(versionText)
            : AppHelpInfo.BuildAboutText(
                versionText,
                $"Built with .NET 10, {uiFramework}, ClosedXML.");

        return new AboutDialogPresentation(
            windowTitle ?? WindowTitle,
            aboutText,
            DialogAutomationId,
            TextAutomationId,
            OkAutomationId,
            helpText ?? HelpText);
    }
}
