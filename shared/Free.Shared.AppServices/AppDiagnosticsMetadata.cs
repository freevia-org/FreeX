using System.Runtime.InteropServices;

namespace Free.Shared.AppServices;

public sealed record AppDiagnosticsMetadata(
    string AppVersion,
    string SessionId,
    string RuntimeDescription,
    string OperatingSystemDescription,
    string ProcessArchitecture)
{
    public static AppDiagnosticsMetadata Create(string appVersion) =>
        new(
            appVersion,
            Guid.NewGuid().ToString("N"),
            RuntimeInformation.FrameworkDescription,
            RuntimeInformation.OSDescription,
            RuntimeInformation.ProcessArchitecture.ToString());
}
