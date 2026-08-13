namespace Free.Shared.AppServices.Printing;

/// <summary>
/// Selects the platform print backend while leaving concrete service construction to the product host.
/// </summary>
public static class PlatformPrintServiceSelector
{
    public static IPlatformPrintService Select(
        Func<IPlatformPrintService>? windowsFactory,
        Func<IPlatformPrintService> cupsFactory) =>
        Select(OperatingSystem.IsWindows(), windowsFactory, cupsFactory);

    internal static IPlatformPrintService Select(
        bool isWindows,
        Func<IPlatformPrintService>? windowsFactory,
        Func<IPlatformPrintService> cupsFactory)
    {
        ArgumentNullException.ThrowIfNull(cupsFactory);

        var factory = isWindows && windowsFactory is not null
            ? windowsFactory
            : cupsFactory;
        return factory() ?? throw new InvalidOperationException(
            "The selected platform print-service factory returned null.");
    }
}
