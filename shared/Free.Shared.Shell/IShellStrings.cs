namespace Free.Shared.Shell;

/// <summary>
/// Localized strings the shared WPF shell helpers need (dialog buttons, message-box
/// titles). Apps supply their own implementation so the shell stays free of any
/// single app's resource catalog; FreeX delegates to its UiText, FreeW will provide
/// its own. Underscores in button text denote WPF access keys.
/// </summary>
public interface IShellStrings
{
    string Ok { get; }
    string Cancel { get; }
    string ErrorTitle { get; }
    string WarningTitle { get; }
    string InformationTitle { get; }
    string ConfirmTitle { get; }

    /// <summary>Derives an accessibility name from button text, stripping access-key markers.</summary>
    string CreateAutomationName(string textWithAccessKey);
}

/// <summary>
/// Neutral English fallback used until an app installs its own <see cref="IShellStrings"/>
/// via <see cref="ShellStrings.Current"/>. Keeps the shared shell usable standalone.
/// </summary>
public sealed class DefaultShellStrings : IShellStrings
{
    public string Ok => "_OK";
    public string Cancel => "_Cancel";
    public string ErrorTitle => "Error";
    public string WarningTitle => "Warning";
    public string InformationTitle => "Information";
    public string ConfirmTitle => "Confirm";

    public string CreateAutomationName(string textWithAccessKey) =>
        textWithAccessKey?.Replace("_", string.Empty, System.StringComparison.Ordinal) ?? string.Empty;
}

/// <summary>
/// Ambient provider for shell strings. An app sets <see cref="Current"/> once at startup
/// (before any shell dialog is shown). Defaults to <see cref="DefaultShellStrings"/>.
/// </summary>
public static class ShellStrings
{
    public static IShellStrings Current { get; set; } = new DefaultShellStrings();
}
