using System.Reflection;

namespace Free.Shared.AppServices;

/// <summary>
/// Assembly-driven version, feedback, and diagnostics text shared by the sister apps' product-info
/// classes. Products pass their name; the assembly supplies the version metadata.
/// </summary>
public static class ProductBuildInfo
{
    /// <summary>
    /// "Version x (Tester Release)". <paramref name="dropTrailingZeroPatch"/> is FreeX's display
    /// convention ("0.5" for "0.5.0"); FreeW and FreeP leave it off.
    /// </summary>
    public static string GetVersionText(Assembly assembly, bool dropTrailingZeroPatch = false)
    {
        var version = AssemblyVersionMetadata.FromAssembly(assembly);
        return AppVersionFormatter.FormatVersionText(
            version.InformationalVersion,
            dropTrailingZeroPatch);
    }

    public static string GetBuildVersionText(Assembly assembly)
    {
        var version = AssemblyVersionMetadata.FromAssembly(assembly);
        return AppVersionFormatter.FormatBuildVersionText(
            version.InformationalVersion,
            version.AssemblyVersion);
    }

    public static string CreateFeedbackUrl(string productName, Assembly assembly) =>
        AppFeedbackReporter.CreateIssueUrl(
            productName,
            AppDiagnosticsMetadata.Create(GetBuildVersionText(assembly)));

    public static string CreateDiagnosticsText(string productName, Assembly assembly) =>
        AppFeedbackReporter.CreateDiagnosticsText(
            productName,
            AppDiagnosticsMetadata.Create(GetBuildVersionText(assembly)));
}
