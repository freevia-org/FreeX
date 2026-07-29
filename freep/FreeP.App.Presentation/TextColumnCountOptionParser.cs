namespace FreeP.App.Compositor;

/// <summary>Maps the text-columns ribbon choices to a valid DrawingML column count.</summary>
public static class TextColumnCountOptionParser
{
    public static bool TryParse(string? value, out int count)
    {
        count = 1;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var setting = value.Trim();
        if (setting.StartsWith("columns", StringComparison.OrdinalIgnoreCase))
            setting = setting["columns".Length..].Trim();

        if (!int.TryParse(setting, out var parsed) || parsed < 1 || parsed > 32)
            return false;

        count = parsed;
        return true;
    }
}
