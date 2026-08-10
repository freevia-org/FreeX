using System.Diagnostics;

namespace Free.Shared.AppServices;

/// <summary>Desktop shell adapter for the shared external-URI allowlist.</summary>
public static class DesktopExternalUriLauncher
{
    public static ExternalUriLaunchResult Open(string target) =>
        ExternalUriLauncher.Open(
            target,
            uri => Process.Start(new ProcessStartInfo(uri.AbsoluteUri)
            {
                UseShellExecute = true
            }));
}
