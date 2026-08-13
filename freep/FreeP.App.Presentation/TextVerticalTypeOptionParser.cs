using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>Maps stable Text Direction choices and compatible external labels to DrawingML values.</summary>
public static class TextVerticalTypeOptionParser
{
    public static bool TryParse(object? value, out TextVerticalType verticalType)
    {
        verticalType = TextVerticalType.Horizontal;
        return FreePRibbonChoiceCatalog.TryResolve(
                value,
                FreePRibbonChoiceCatalog.TextVerticalTypeChoices,
                out verticalType) &&
            Enum.IsDefined(verticalType);
    }
}
