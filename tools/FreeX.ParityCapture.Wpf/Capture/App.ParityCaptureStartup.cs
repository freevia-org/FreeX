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
        }

        Shutdown();
    }
}
