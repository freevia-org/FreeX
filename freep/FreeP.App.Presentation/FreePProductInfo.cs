using System.Reflection;
using Free.Shared.AppServices;

namespace FreeP.App.Compositor;

/// <summary>Privacy-safe product and support endpoints shared by the FreeP renderers.</summary>
public static class FreePProductInfo
{
    public const string ProductName = "FreeP";
    public const string HelpUrl = "https://github.com/freevia-org/FreeX/tree/main/freep";

    public static string CreateFeedbackUrl(Assembly assembly) =>
        ProductBuildInfo.CreateFeedbackUrl(ProductName, assembly);

    public static string CreateDiagnosticsText(Assembly assembly) =>
        ProductBuildInfo.CreateDiagnosticsText(ProductName, assembly);
}
