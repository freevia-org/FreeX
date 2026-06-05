using System.Globalization;

namespace FreeX.Core.Calc.Tests;

internal sealed class TestCultureScope : IDisposable
{
    private readonly CultureInfo _previousCulture = CultureInfo.CurrentCulture;
    private readonly CultureInfo _previousUICulture = CultureInfo.CurrentUICulture;

    private TestCultureScope(CultureInfo currentCulture, CultureInfo currentUICulture)
    {
        CultureInfo.CurrentCulture = currentCulture;
        CultureInfo.CurrentUICulture = currentUICulture;
    }

    public static TestCultureScope CurrentCultureAndUICulture(string cultureName)
    {
        var culture = CultureInfo.GetCultureInfo(cultureName);
        return new(culture, culture);
    }

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _previousCulture;
        CultureInfo.CurrentUICulture = _previousUICulture;
    }
}
