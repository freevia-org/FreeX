using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Markup;

namespace Free.Shared.Shell;

/// <summary>
/// Applies a product-selected UI culture to the current process and installs WPF's language
/// metadata once. Product localization assemblies retain ownership of culture resolution.
/// </summary>
public static class WpfLocalizationCultureBootstrap
{
    private static int _wpfLanguageMetadataApplied;

    public static void ApplyUiCulture(
        string? cultureName,
        Func<string?, CultureInfo> resolveCulture,
        CultureInfo fallbackCulture)
    {
        ArgumentNullException.ThrowIfNull(resolveCulture);
        ArgumentNullException.ThrowIfNull(fallbackCulture);

        var uiCulture = resolveCulture(cultureName) ?? throw new InvalidOperationException(
            "The product localization catalog returned a null culture.");

        CultureInfo.DefaultThreadCurrentUICulture = uiCulture;
        Thread.CurrentThread.CurrentUICulture = uiCulture;
    }

    public static void ApplyCurrentCultureToWpf()
    {
        if (Interlocked.Exchange(ref _wpfLanguageMetadataApplied, 1) == 1)
            return;

        FrameworkElement.LanguageProperty.OverrideMetadata(
            typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag)));
    }
}
