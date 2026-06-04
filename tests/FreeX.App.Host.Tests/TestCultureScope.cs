using System.Globalization;

namespace FreeX.App.Host.Tests;

internal sealed class TestCultureScope : IDisposable
{
    private readonly CultureInfo _previousCulture = CultureInfo.CurrentCulture;
    private readonly CultureInfo _previousUICulture = CultureInfo.CurrentUICulture;
    private readonly CultureInfo? _previousDefaultCulture = CultureInfo.DefaultThreadCurrentCulture;
    private readonly CultureInfo? _previousDefaultUICulture = CultureInfo.DefaultThreadCurrentUICulture;
    private bool _disposed;

    private TestCultureScope(
        CultureInfo? currentCulture = null,
        CultureInfo? currentUICulture = null,
        CultureInfo? defaultThreadCurrentCulture = null,
        CultureInfo? defaultThreadCurrentUICulture = null)
    {
        if (currentCulture is not null)
        {
            CultureInfo.CurrentCulture = currentCulture;
        }

        if (currentUICulture is not null)
        {
            CultureInfo.CurrentUICulture = currentUICulture;
        }

        if (defaultThreadCurrentCulture is not null)
        {
            CultureInfo.DefaultThreadCurrentCulture = defaultThreadCurrentCulture;
        }

        if (defaultThreadCurrentUICulture is not null)
        {
            CultureInfo.DefaultThreadCurrentUICulture = defaultThreadCurrentUICulture;
        }
    }

    public static TestCultureScope CurrentCulture(string cultureName) =>
        CurrentCulture(CultureInfo.GetCultureInfo(cultureName));

    public static TestCultureScope CurrentCulture(CultureInfo culture) =>
        new(currentCulture: culture);

    public static TestCultureScope InvariantCurrentCulture() =>
        CurrentCulture(CultureInfo.InvariantCulture);

    public static TestCultureScope CurrentCultureAndUICulture(string currentCulture, string currentUICulture) =>
        new(
            currentCulture: CultureInfo.GetCultureInfo(currentCulture),
            currentUICulture: CultureInfo.GetCultureInfo(currentUICulture));

    public static TestCultureScope CurrentUICultureAndDefaultThreadUICulture(string cultureName)
    {
        var culture = CultureInfo.GetCultureInfo(cultureName);
        return new(currentUICulture: culture, defaultThreadCurrentUICulture: culture);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        CultureInfo.CurrentCulture = _previousCulture;
        CultureInfo.CurrentUICulture = _previousUICulture;
        CultureInfo.DefaultThreadCurrentCulture = _previousDefaultCulture;
        CultureInfo.DefaultThreadCurrentUICulture = _previousDefaultUICulture;
        _disposed = true;
    }
}
