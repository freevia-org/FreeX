using System.Globalization;

namespace FreeX.Core.Commands;

/// <summary>Parses the invariant-culture numeric fields shared by the calculation-options hosts.</summary>
public static class CalculationOptionsInputParser
{
    public static bool TryParseMaxIterations(string? text, out int maxIterations)
    {
        maxIterations = 0;
        if (!int.TryParse(
                (text ?? string.Empty).Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var parsed) ||
            parsed <= 0)
        {
            return false;
        }

        maxIterations = parsed;
        return true;
    }

    public static bool TryParseMaxChange(string? text, out double maxChange)
    {
        maxChange = 0;
        if (!double.TryParse(
                (text ?? string.Empty).Trim(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var parsed) ||
            parsed < 0)
        {
            return false;
        }

        maxChange = parsed;
        return true;
    }
}
