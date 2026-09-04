using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace FreeX.App.Host;

public partial class App
{
    partial void TryRunExternalStartup(
        IReadOnlyList<string> startupArguments,
        ref bool handled)
    {
        if (ParityCapture.TryGetOutputDirectory(startupArguments) is not { } outputDirectory)
            return;

        handled = true;
        try
        {
            ParityCapture.Run(
                outputDirectory,
                () => Services.GetRequiredService<MainWindow>(),
                ParityCapture.TryGetTargetSurfaceId(startupArguments));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Parity capture failed");
            Environment.ExitCode = 1;
        }

        // r329: carry the capture's outcome into the process exit code. Shutdown() defaults to 0, so
        // both a thrown capture and a target that matched nothing used to exit green.
        Shutdown(Environment.ExitCode);
    }
}
