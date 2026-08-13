using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>Maps stable ribbon choices and compatible external labels to DrawingML modes.</summary>
public static class TextAutoFitOptionParser
{
    public static bool TryParse(object? value, out TextAutoFitKind kind)
    {
        kind = TextAutoFitKind.None;
        return FreePRibbonChoiceCatalog.TryResolve(
                value,
                FreePRibbonChoiceCatalog.TextAutoFitChoices,
                out kind) &&
            Enum.IsDefined(kind);
    }
}
