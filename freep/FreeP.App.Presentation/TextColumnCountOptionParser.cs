namespace FreeP.App.Compositor;

/// <summary>Maps the text-columns ribbon choices to a valid DrawingML column count.</summary>
public static class TextColumnCountOptionParser
{
    public static bool TryParse(object? value, out int count)
    {
        count = 1;
        if (FreePRibbonChoiceCatalog.TryResolve(
                value,
                FreePRibbonChoiceCatalog.TextColumnCountChoices,
                out count))
            return count is >= 1 and <= 32;

        if (value is not string text || string.IsNullOrWhiteSpace(text))
            return false;

        var setting = text.Trim();
        if (setting.StartsWith("columns", StringComparison.OrdinalIgnoreCase))
            setting = setting["columns".Length..].Trim();

        if (!int.TryParse(setting, out var parsed) || parsed < 1 || parsed > 32)
            return false;

        count = parsed;
        return true;
    }
}
