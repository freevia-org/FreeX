using FreeX.App.Services;

namespace FreeX.App.Avalonia;

/// <summary>
/// Avalonia's compatibility-shaped diagnostics handle. Policy and persistence live in the shared
/// <see cref="LocalAppDiagnostics"/> service; this type only preserves the existing host startup API.
/// </summary>
internal sealed class AvaloniaAppDiagnostics : LocalAppDiagnostics
{
    private AvaloniaAppDiagnostics(LocalAppDiagnostics local)
        : base(local)
    {
    }

    public static AvaloniaAppDiagnostics Create(string? diagnosticsDirectory = null) =>
        new(LocalAppDiagnostics.Create(
            AppHelpInfo.GetVersionText(typeof(AvaloniaAppDiagnostics).Assembly),
            diagnosticsDirectory));

    public void RegisterUnhandledExceptionHandlers() => RegisterCrashHandlers();
}
