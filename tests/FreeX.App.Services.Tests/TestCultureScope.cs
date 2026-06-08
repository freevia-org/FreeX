using System.Globalization;

namespace FreeX.App.Services.Tests;

internal sealed class TestCultureScope : IDisposable
{
    private readonly CultureInfo _previousCulture = CultureInfo.CurrentCulture;
    private bool _disposed;

    private TestCultureScope(CultureInfo currentCulture)
    {
        CultureInfo.CurrentCulture = currentCulture;
    }

    public static TestCultureScope CurrentCulture(string cultureName) =>
        new(CultureInfo.GetCultureInfo(cultureName));

    public void Dispose()
    {
        if (_disposed)
            return;

        CultureInfo.CurrentCulture = _previousCulture;
        _disposed = true;
    }
}
