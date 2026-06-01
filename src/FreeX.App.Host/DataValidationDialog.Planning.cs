using System.Globalization;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class DataValidationDialog
{
    public static bool TryValidateCriteriaInputs(
        string typeTag,
        string operatorTag,
        string? formula1,
        string? formula2,
        out string? error)
    {
        error = null;
        if (string.Equals(typeTag, "Any", StringComparison.Ordinal))
            return true;

        var first = formula1?.Trim() ?? "";
        var second = formula2?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(first))
        {
            error = typeTag switch
            {
                "List" => UiText.Get("DataValidation_SourceRequired"),
                "Custom" => UiText.Get("DataValidation_FormulaRequired"),
                _ => UiText.Get("DataValidation_ValueRequired")
            };
            return false;
        }

        if (RequiresSecondFormula(typeTag, operatorTag) && string.IsNullOrWhiteSpace(second))
        {
            error = UiText.Get("DataValidation_MaximumRequired");
            return false;
        }

        if (!TryValidateTypeSpecificCriteria(typeTag, operatorTag, first, second, out error))
            return false;

        return true;
    }

    private static bool RequiresSecondFormula(string typeTag, string operatorTag) =>
        typeTag is not "Any" and not "List" and not "Custom"
        && operatorTag is "Between" or "NotBetween";

    private static bool ShouldFocusSecondCriteriaInput(
        string typeTag,
        string operatorTag,
        string? formula1,
        string? formula2)
    {
        if (!RequiresSecondFormula(typeTag, operatorTag))
            return false;

        var first = formula1?.Trim() ?? "";
        var second = formula2?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(second))
            return true;

        return TryValidateSingleCriteria(typeTag, first, out _) &&
               !TryValidateSingleCriteria(typeTag, second, out _);
    }

    private static bool TryValidateTypeSpecificCriteria(
        string typeTag,
        string operatorTag,
        string first,
        string second,
        out string? error)
    {
        error = null;
        if (!TryValidateSingleCriteria(typeTag, first, out error))
            return false;

        return !RequiresSecondFormula(typeTag, operatorTag) ||
               TryValidateSingleCriteria(typeTag, second, out error);
    }

    private static bool TryValidateSingleCriteria(string typeTag, string text, out string? error)
    {
        error = null;
        return typeTag switch
        {
            "WholeNumber" => TryValidateWholeNumberCriteria(text, out error),
            "Decimal" => TryValidateDecimalCriteria(text, out error),
            "Date" => TryValidateDateCriteria(text, out error),
            "Time" => TryValidateTimeCriteria(text, out error),
            "TextLength" => TryValidateTextLengthCriteria(text, out error),
            "List" => TryValidateListCriteria(text, out error),
            "Custom" => TryValidateCustomCriteria(text, out error),
            _ => true
        };
    }

    private static bool TryValidateWholeNumberCriteria(string text, out string? error)
    {
        if (IsFormulaCriteria(text) ||
            (TryParseNumber(text, out var value) && IsWholeNumber(value)))
        {
            error = null;
            return true;
        }

        error = UiText.Get("DataValidation_InvalidWholeNumberCriteria");
        return false;
    }

    private static bool TryValidateDecimalCriteria(string text, out string? error)
    {
        if (IsFormulaCriteria(text) || TryParseNumber(text, out _))
        {
            error = null;
            return true;
        }

        error = UiText.Get("DataValidation_InvalidDecimalCriteria");
        return false;
    }

    private static bool TryValidateDateCriteria(string text, out string? error)
    {
        if (IsFormulaCriteria(text) ||
            TryParseNumber(text, out _) ||
            DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out _) ||
            DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            error = null;
            return true;
        }

        error = UiText.Get("DataValidation_InvalidDateCriteria");
        return false;
    }

    private static bool TryValidateTimeCriteria(string text, out string? error)
    {
        if (IsFormulaCriteria(text))
        {
            error = null;
            return true;
        }

        if (TryParseNumber(text, out var numericTime) && IsValidTimeFraction(numericTime))
        {
            error = null;
            return true;
        }

        if (TimeSpan.TryParse(text, CultureInfo.CurrentCulture, out var currentCultureTime) ||
            TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out currentCultureTime))
        {
            if (currentCultureTime >= TimeSpan.Zero && currentCultureTime < TimeSpan.FromDays(1))
            {
                error = null;
                return true;
            }
        }

        if (DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out var currentCultureDateTime) ||
            DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out currentCultureDateTime))
        {
            if (currentCultureDateTime.TimeOfDay >= TimeSpan.Zero &&
                currentCultureDateTime.TimeOfDay < TimeSpan.FromDays(1))
            {
                error = null;
                return true;
            }
        }

        error = UiText.Get("DataValidation_InvalidTimeCriteria");
        return false;
    }

    private static bool TryValidateTextLengthCriteria(string text, out string? error)
    {
        if (IsFormulaCriteria(text) ||
            (TryParseNumber(text, out var value) && IsWholeNumber(value) && value >= 0))
        {
            error = null;
            return true;
        }

        error = UiText.Get("DataValidation_InvalidTextLengthCriteria");
        return false;
    }

    private static bool TryValidateListCriteria(string text, out string? error)
    {
        if (IsFormulaCriteria(text) || TryParseInlineListCriteria(text))
        {
            error = null;
            return true;
        }

        error = UiText.Get("DataValidation_InvalidListCriteria");
        return false;
    }

    private static bool TryValidateCustomCriteria(string text, out string? error)
    {
        if (TryParseFormulaCriteria(text, allowImplicitFormula: true))
        {
            error = null;
            return true;
        }

        error = UiText.Get("DataValidation_InvalidCustomCriteria");
        return false;
    }

    private static bool IsFormulaCriteria(string text) =>
        text.TrimStart().StartsWith('=') &&
        TryParseFormulaCriteria(text, allowImplicitFormula: false);

    private static bool TryParseFormulaCriteria(string text, bool allowImplicitFormula)
    {
        var formulaText = text.Trim();
        if (formulaText.Length == 0)
            return false;

        if (!formulaText.StartsWith('='))
        {
            if (!allowImplicitFormula)
                return false;

            formulaText = "=" + formulaText;
        }

        try
        {
            _ = new Parser(new Lexer(formulaText).Tokenize()).Parse();
            return true;
        }
        catch (FormulaParseException)
        {
            return false;
        }
    }

    private static bool TryParseInlineListCriteria(string text)
    {
        var hasItemText = false;
        var currentHasText = false;
        var inQuotes = false;

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch == '"')
            {
                if (inQuotes && i + 1 < text.Length && text[i + 1] == '"')
                {
                    currentHasText = true;
                    i++;
                    continue;
                }

                inQuotes = !inQuotes;
                continue;
            }

            if (ch == ',' && !inQuotes)
            {
                hasItemText |= currentHasText;
                currentHasText = false;
                continue;
            }

            currentHasText |= !char.IsWhiteSpace(ch);
        }

        return !inQuotes && (hasItemText || currentHasText);
    }

    private static bool TryParseNumber(string text, out double value) =>
        double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out value);

    private static bool IsWholeNumber(double value) =>
        double.IsFinite(value) && Math.Abs(value - Math.Round(value)) <= double.Epsilon;

    private static bool IsValidTimeFraction(double value) =>
        double.IsFinite(value) && value >= 0 && value < 1;

    public static DataValidationRangeSelectionRequest CreateRangeSelectionRequest(
        DataValidationRangeSelectionTarget target,
        string currentText) =>
        new(target, currentText.Trim(), CollapseDialog: true);

    private static string TypeTag(DvType type) => type switch
    {
        DvType.List => "List",
        DvType.WholeNumber => "WholeNumber",
        DvType.Decimal => "Decimal",
        DvType.Date => "Date",
        DvType.Time => "Time",
        DvType.TextLength => "TextLength",
        DvType.Custom => "Custom",
        _ => "Any"
    };

    private static string OperatorTag(DvOperator op) => op switch
    {
        DvOperator.NotBetween => "NotBetween",
        DvOperator.Equal => "Equal",
        DvOperator.NotEqual => "NotEqual",
        DvOperator.GreaterThan => "GreaterThan",
        DvOperator.LessThan => "LessThan",
        DvOperator.GreaterThanOrEqual => "GreaterThanOrEqual",
        DvOperator.LessThanOrEqual => "LessThanOrEqual",
        _ => "Between"
    };

    private static string AlertStyleTag(DvAlertStyle style) => style switch
    {
        DvAlertStyle.Warning => "Warning",
        DvAlertStyle.Information => "Information",
        _ => "Stop"
    };
}
