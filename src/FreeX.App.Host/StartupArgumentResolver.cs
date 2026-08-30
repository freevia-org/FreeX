namespace FreeX.App.Host;

/// <summary>
/// Resolves file arguments supplied at process launch without depending on WPF retaining them.
/// </summary>
internal static class StartupArgumentResolver
{
    public static IReadOnlyList<string> Resolve(
        IReadOnlyList<string> startupEventArguments,
        IReadOnlyList<string> launchArguments,
        IReadOnlyList<string> commandLineArguments)
    {
        ArgumentNullException.ThrowIfNull(startupEventArguments);
        ArgumentNullException.ThrowIfNull(launchArguments);
        ArgumentNullException.ThrowIfNull(commandLineArguments);

        if (startupEventArguments.Count > 0)
            return startupEventArguments.ToArray();

        if (launchArguments.Count > 0)
            return launchArguments.ToArray();

        return commandLineArguments.Skip(1).ToArray();
    }
}
