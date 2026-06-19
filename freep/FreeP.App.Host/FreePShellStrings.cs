using System.Globalization;
using Free.Shared.Shell;

namespace FreeP.App.Host;

/// <summary>
/// FreeP's <see cref="IShellStrings"/> — the button/title strings the shared WPF shell dialogs need. A thin
/// English implementation for the scaffold (FreeP has no localized catalog yet); the next session can swap in
/// a resx-backed one, exactly as FreeX delegates to its UiText. Underscores denote WPF access keys.
/// </summary>
internal sealed class FreePShellStrings : IShellStrings
{
    public string Ok => "_OK";
    public string Cancel => "_Cancel";
    public string ErrorTitle => "FreeP";
    public string WarningTitle => "FreeP";
    public string InformationTitle => "FreeP";
    public string ConfirmTitle => "FreeP";

    public string CreateAutomationName(string textWithAccessKey) =>
        textWithAccessKey?.Replace("_", string.Empty, StringComparison.Ordinal) ?? string.Empty;
}

/// <summary>
/// FreeP's <see cref="IBackstageStrings"/>. The scaffold's backstage doesn't drive the localized greeting /
/// recent-file planners yet, so this simply echoes keys (the neutral fallback behaviour) — present so the
/// ambient provider is FreeP-owned and ready for a real catalog later.
/// </summary>
internal sealed class FreePBackstageStrings : IBackstageStrings
{
    public string Get(string key) => key;

    public string Format(string key, params object?[] args) =>
        args is { Length: > 0 } ? string.Format(CultureInfo.CurrentCulture, key, args) : key;
}
