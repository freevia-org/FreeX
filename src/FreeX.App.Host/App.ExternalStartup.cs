namespace FreeX.App.Host;

public partial class App
{
    partial void TryRunExternalStartup(
        IReadOnlyList<string> startupArguments,
        ref bool handled);
}
