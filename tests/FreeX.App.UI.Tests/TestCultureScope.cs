using System.Globalization;

namespace FreeX.App.UI.Tests;

internal sealed class TestCultureScope : IDisposable
{
    private readonly CultureInfo _previousCulture = CultureInfo.CurrentCulture;
    private readonly CultureInfo _previousUICulture = CultureInfo.CurrentUICulture;

    private TestCultureScope(CultureInfo? currentCulture = null, CultureInfo? currentUICulture = null)
    {
        if (currentCulture is not null)
            CultureInfo.CurrentCulture = currentCulture;

        if (currentUICulture is not null)
            CultureInfo.CurrentUICulture = currentUICulture;
    }

    public static TestCultureScope CurrentCulture(string cultureName) =>
        new(currentCulture: CultureInfo.GetCultureInfo(cultureName));

    public static TestCultureScope CurrentCultureAndUICulture(string cultureName)
    {
        var culture = CultureInfo.GetCultureInfo(cultureName);
        return new(currentCulture: culture, currentUICulture: culture);
    }

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _previousCulture;
        CultureInfo.CurrentUICulture = _previousUICulture;
    }
}
