using System.Globalization;

namespace FreeP.App.Compositor;

public static class SlideShowSlideNumberPlanner
{
    public const int MaxDigits = 4;

    public static bool TryGetDigit(string? keyName, out char digit)
    {
        digit = keyName?.Trim().ToUpperInvariant() switch
        {
            "D0" or "NUMPAD0" or "DIGIT0" => '0',
            "D1" or "NUMPAD1" or "DIGIT1" => '1',
            "D2" or "NUMPAD2" or "DIGIT2" => '2',
            "D3" or "NUMPAD3" or "DIGIT3" => '3',
            "D4" or "NUMPAD4" or "DIGIT4" => '4',
            "D5" or "NUMPAD5" or "DIGIT5" => '5',
            "D6" or "NUMPAD6" or "DIGIT6" => '6',
            "D7" or "NUMPAD7" or "DIGIT7" => '7',
            "D8" or "NUMPAD8" or "DIGIT8" => '8',
            "D9" or "NUMPAD9" or "DIGIT9" => '9',
            _ => '\0',
        };
        return digit != '\0';
    }

    public static string AppendDigit(string current, char digit)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (digit is < '0' or > '9' || current.Length >= MaxDigits)
            return current;
        return current + digit;
    }

    public static bool TryParseSlideNumber(string? buffer, out int oneBasedSlideNumber)
    {
        if (int.TryParse(
                buffer,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out oneBasedSlideNumber) &&
            oneBasedSlideNumber > 0)
        {
            return true;
        }

        oneBasedSlideNumber = 0;
        return false;
    }
}
