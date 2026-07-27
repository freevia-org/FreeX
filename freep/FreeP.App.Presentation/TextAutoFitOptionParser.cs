using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>Maps the shared ribbon's text autofit choices to their DrawingML modes.</summary>
public static class TextAutoFitOptionParser
{
    public static bool TryParse(string? value, out TextAutoFitKind kind)
    {
        kind = TextAutoFitKind.None;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var setting = value.Trim();
        if (setting.Equals("Do not autofit", StringComparison.OrdinalIgnoreCase) ||
            setting.Equals("No autofit", StringComparison.OrdinalIgnoreCase) ||
            setting.Equals("None", StringComparison.OrdinalIgnoreCase))
            return true;

        if (setting.Equals("Shrink text on overflow", StringComparison.OrdinalIgnoreCase) ||
            setting.Equals("Normal", StringComparison.OrdinalIgnoreCase))
        {
            kind = TextAutoFitKind.Normal;
            return true;
        }

        if (setting.Equals("Resize shape to fit text", StringComparison.OrdinalIgnoreCase) ||
            setting.Equals("Shape", StringComparison.OrdinalIgnoreCase))
        {
            kind = TextAutoFitKind.Shape;
            return true;
        }

        return false;
    }
}
