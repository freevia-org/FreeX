using System.Globalization;

namespace FreeX.Core.IO.Tests;

internal sealed class TestCultureScope : IDisposable
{
    private readonly CultureInfo _previousCulture = CultureInfo.CurrentCulture;

    private TestCultureScope(CultureInfo culture)
    {
        CultureInfo.CurrentCulture = culture;
    }

    public static TestCultureScope CurrentCulture(string cultureName) =>
        new(CultureInfo.GetCultureInfo(cultureName));

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _previousCulture;
    }
}
