using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Markup;

namespace FreeX.App.Host;

internal static class AppLocalization
{
    private static int _wpfLanguageMetadataApplied;

    public static void ApplyCurrentCultureToWpf()
    {
        if (Interlocked.Exchange(ref _wpfLanguageMetadataApplied, 1) == 1)
            return;

        FrameworkElement.LanguageProperty.OverrideMetadata(
            typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag)));
    }
}
