using System.Reflection;
using Free.Shared.AppServices;

namespace FreeP.App.Compositor;

/// <summary>Privacy-safe product and support endpoints shared by the FreeP renderers.</summary>
public static class FreePProductInfo
{
    public const string ProductName = "FreeP";
    public const string HelpUrl = "https://github.com/tony-xmelon/FreeX/tree/main/freep";

    public static string CreateFeedbackUrl(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var metadata = AssemblyVersionMetadata.FromAssembly(assembly);
        var version = AppVersionFormatter.FormatBuildVersionText(
            metadata.InformationalVersion,
            metadata.AssemblyVersion);
        return AppFeedbackReporter.CreateIssueUrl(
            ProductName,
            AppDiagnosticsMetadata.Create(version));
    }
}
