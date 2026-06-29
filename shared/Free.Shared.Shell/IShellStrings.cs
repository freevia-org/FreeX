namespace Free.Shared.Shell;

/// <summary>
/// Localized strings the shared WPF shell helpers need (dialog buttons, message-box
/// titles). Apps supply their own implementation so the shell stays free of any
/// single app's resource catalog. Underscores in button text denote WPF access keys.
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
/// Immutable shell strings for apps that do not yet have a localized resource catalog.
/// </summary>
public sealed class StaticShellStrings : IShellStrings
{
    public StaticShellStrings(
        string ok = "_OK",
        string cancel = "_Cancel",
        string errorTitle = "Error",
        string warningTitle = "Warning",
        string informationTitle = "Information",
        string confirmTitle = "Confirm")
    {
        Ok = ok;
        Cancel = cancel;
        ErrorTitle = errorTitle;
        WarningTitle = warningTitle;
        InformationTitle = informationTitle;
        ConfirmTitle = confirmTitle;
    }

    public static StaticShellStrings NeutralEnglish { get; } = new();

    public string Ok { get; }
    public string Cancel { get; }
    public string ErrorTitle { get; }
    public string WarningTitle { get; }
    public string InformationTitle { get; }
    public string ConfirmTitle { get; }

    public static StaticShellStrings ForProductTitle(string productTitle)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productTitle);
        return new StaticShellStrings(
            errorTitle: productTitle,
            warningTitle: productTitle,
            informationTitle: productTitle,
            confirmTitle: productTitle);
    }

    public string CreateAutomationName(string textWithAccessKey) =>
        ShellStringText.CreateAutomationName(textWithAccessKey);
}

/// <summary>
/// Shell string adapter for app-owned resource catalogs.
/// </summary>
public sealed class ResourceShellStrings : IShellStrings
{
    private readonly Func<string> _ok;
    private readonly Func<string> _cancel;
    private readonly Func<string> _errorTitle;
    private readonly Func<string> _warningTitle;
    private readonly Func<string> _informationTitle;
    private readonly Func<string> _confirmTitle;
    private readonly Func<string, string> _createAutomationName;

    public ResourceShellStrings(
        Func<string> ok,
        Func<string> cancel,
        Func<string> errorTitle,
        Func<string> warningTitle,
        Func<string> informationTitle,
        Func<string> confirmTitle,
        Func<string, string>? createAutomationName = null)
    {
        _ok = ok ?? throw new ArgumentNullException(nameof(ok));
        _cancel = cancel ?? throw new ArgumentNullException(nameof(cancel));
        _errorTitle = errorTitle ?? throw new ArgumentNullException(nameof(errorTitle));
        _warningTitle = warningTitle ?? throw new ArgumentNullException(nameof(warningTitle));
        _informationTitle = informationTitle ?? throw new ArgumentNullException(nameof(informationTitle));
        _confirmTitle = confirmTitle ?? throw new ArgumentNullException(nameof(confirmTitle));
        _createAutomationName = createAutomationName ?? ShellStringText.CreateAutomationName;
    }

    public string Ok => _ok();
    public string Cancel => _cancel();
    public string ErrorTitle => _errorTitle();
    public string WarningTitle => _warningTitle();
    public string InformationTitle => _informationTitle();
    public string ConfirmTitle => _confirmTitle();

    public string CreateAutomationName(string textWithAccessKey) =>
        _createAutomationName(textWithAccessKey);
}

public static class ShellStringText
{
    public static string CreateAutomationName(string? textWithAccessKey) =>
        textWithAccessKey?.Replace("_", string.Empty, StringComparison.Ordinal) ?? string.Empty;
}

/// <summary>
/// Neutral English fallback used until an app installs its own <see cref="IShellStrings"/>
/// via <see cref="ShellStrings.Current"/>. Keeps the shared shell usable standalone.
/// </summary>
public sealed class DefaultShellStrings : IShellStrings
{
    public static DefaultShellStrings Instance { get; } = new();

    public string Ok => StaticShellStrings.NeutralEnglish.Ok;
    public string Cancel => StaticShellStrings.NeutralEnglish.Cancel;
    public string ErrorTitle => StaticShellStrings.NeutralEnglish.ErrorTitle;
    public string WarningTitle => StaticShellStrings.NeutralEnglish.WarningTitle;
    public string InformationTitle => StaticShellStrings.NeutralEnglish.InformationTitle;
    public string ConfirmTitle => StaticShellStrings.NeutralEnglish.ConfirmTitle;

    public string CreateAutomationName(string textWithAccessKey) =>
        StaticShellStrings.NeutralEnglish.CreateAutomationName(textWithAccessKey);
}

/// <summary>
/// Ambient provider for shell strings. An app sets <see cref="Current"/> once at startup
/// (before any shell dialog is shown). Defaults to <see cref="DefaultShellStrings"/>.
/// </summary>
public static class ShellStrings
{
    public static IShellStrings Current { get; set; } = DefaultShellStrings.Instance;
}
