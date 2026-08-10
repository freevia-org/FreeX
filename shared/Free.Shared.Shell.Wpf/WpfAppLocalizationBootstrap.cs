using System.Globalization;

namespace Free.Shared.Shell;

/// <summary>
/// Ordered localization bootstrap for WPF apps. Products provide their own resource delegates
/// and culture resolver; the shared shell only owns adapter installation and WPF culture setup.
/// </summary>
public sealed class WpfAppLocalizationBootstrap
{
    private readonly Func<string, string> _get;
    private readonly Func<string, object?[], string> _format;
    private readonly Func<string?, CultureInfo, CultureInfo> _resolveCulture;
    private readonly CultureInfo _startupUiCulture = CultureInfo.CurrentUICulture;

    public WpfAppLocalizationBootstrap(
        Func<string, string> get,
        Func<string, object?[], string> format,
        Func<string?, CultureInfo, CultureInfo> resolveCulture)
    {
        _get = get ?? throw new ArgumentNullException(nameof(get));
        _format = format ?? throw new ArgumentNullException(nameof(format));
        _resolveCulture = resolveCulture ?? throw new ArgumentNullException(nameof(resolveCulture));
    }

    public void InstallSharedSeams() =>
        ApplicationLocalizationSeamInstaller.Install(_get, _format);

    public void ApplyAppLanguage(string? cultureName) =>
        WpfLocalizationCultureBootstrap.ApplyUiCulture(
            cultureName,
            name => _resolveCulture(name, _startupUiCulture),
            _startupUiCulture);

    public void ApplyCurrentCultureToWpf() =>
        WpfLocalizationCultureBootstrap.ApplyCurrentCultureToWpf();
}
