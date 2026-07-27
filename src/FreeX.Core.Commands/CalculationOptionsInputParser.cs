using System.Globalization;

namespace FreeX.Core.Commands;

public enum CalculationOptionsInputError
{
    None,
    InvalidMaxIterations,
    InvalidMaxChange,
}

/// <summary>Parses the invariant-culture numeric fields shared by the calculation-options hosts.</summary>
public static class CalculationOptionsInputParser
{
    public static bool TryParseBounds(
        bool iterativeCalculationEnabled,
        string? maxIterationsText,
        string? maxChangeText,
        int fallbackMaxIterations,
        double fallbackMaxChange,
        out int maxIterations,
        out double maxChange,
        out CalculationOptionsInputError error)
    {
        var iterationsValid = TryParseMaxIterations(maxIterationsText, out maxIterations);
        var changeValid = TryParseMaxChange(maxChangeText, out maxChange);

        if (!iterativeCalculationEnabled)
        {
            if (!iterationsValid)
                maxIterations = fallbackMaxIterations;
            if (!changeValid)
                maxChange = fallbackMaxChange;

            error = CalculationOptionsInputError.None;
            return true;
        }

        if (!iterationsValid)
        {
            error = CalculationOptionsInputError.InvalidMaxIterations;
            return false;
        }

        if (!changeValid)
        {
            error = CalculationOptionsInputError.InvalidMaxChange;
            return false;
        }

        error = CalculationOptionsInputError.None;
        return true;
    }

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
