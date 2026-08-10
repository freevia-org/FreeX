using System.Globalization;
using FreeX.Core.Model;

namespace FreeX.App.Services;

public static class GoalSeekRequestParser
{
    public static GoalSeekRequestParseResult Parse(
        SheetId sheetId,
        string? setCellText,
        string? targetValueText,
        string? changingCellText)
    {
        var setCellInput = NormalizeInput(setCellText);
        if (string.IsNullOrWhiteSpace(setCellInput))
            return GoalSeekRequestParseResult.Invalid(GoalSeekRequestParseError.SetCellRequired);

        if (!CellAddress.TryParse(setCellInput, sheetId, out var setCell))
            return GoalSeekRequestParseResult.Invalid(
                GoalSeekRequestParseError.InvalidSetCellAddress,
                setCellInput);

        var targetInput = NormalizeInput(targetValueText);
        if (!TryParseTargetValue(targetInput, out var targetValue))
            return GoalSeekRequestParseResult.Invalid(
                GoalSeekRequestParseError.InvalidTargetValue,
                targetInput);

        var changingCellInput = NormalizeInput(changingCellText);
        if (string.IsNullOrWhiteSpace(changingCellInput))
            return GoalSeekRequestParseResult.Invalid(GoalSeekRequestParseError.ChangingCellRequired);

        if (!CellAddress.TryParse(changingCellInput, sheetId, out var changingCell))
            return GoalSeekRequestParseResult.Invalid(
                GoalSeekRequestParseError.InvalidChangingCellAddress,
                changingCellInput);

        if (setCell == changingCell)
            return GoalSeekRequestParseResult.Invalid(GoalSeekRequestParseError.CellsMustDiffer);

        return GoalSeekRequestParseResult.Valid(new GoalSeekRequest(setCell, targetValue, changingCell));
    }

    public static bool TryParse(
        SheetId sheetId,
        string? setCellText,
        string? targetValueText,
        string? changingCellText,
        out GoalSeekRequest request,
        out GoalSeekRequestParseResult result)
    {
        result = Parse(sheetId, setCellText, targetValueText, changingCellText);
        if (result.Request is { } parsedRequest)
        {
            request = parsedRequest;
            return true;
        }

        request = default!;
        return false;
    }

    private static bool TryParseTargetValue(string targetInput, out double targetValue) =>
        (TryParseFiniteNumber(targetInput, CultureInfo.CurrentCulture, out targetValue) ||
         TryParseFiniteNumber(targetInput, CultureInfo.InvariantCulture, out targetValue)) &&
        double.IsFinite(targetValue);

    private static bool TryParseFiniteNumber(string targetInput, IFormatProvider formatProvider, out double value) =>
        double.TryParse(targetInput, NumberStyles.Any, formatProvider, out value) &&
        HasValidGroupingShape(targetInput, formatProvider);

    // .NET's NumberStyles.AllowThousands parsing does not validate that group separators actually
    // fall on 3-digit boundaries — e.g. under de-DE (whose group separator is '.'),
    // double.TryParse("1.5", NumberStyles.Any, ...) happily returns 15, silently treating the
    // fractional ".5" as a malformed trailing group instead of falling through to try
    // InvariantCulture. Reject that shape here so the caller tries the next culture instead of
    // accepting a bogus parse. Mirrors DelimitedTextWorkbookReader's HasValidGroupingShape guard
    // for the identical locale grouping-vs-decimal ambiguity.
    private static bool HasValidGroupingShape(ReadOnlySpan<char> field, IFormatProvider formatProvider)
    {
        var numberFormat = NumberFormatInfo.GetInstance(formatProvider);
        var groupSeparator = numberFormat.NumberGroupSeparator;
        if (string.IsNullOrEmpty(groupSeparator) ||
            field.IndexOf(groupSeparator, StringComparison.Ordinal) < 0)
            return true; // No grouping separator present — nothing to validate.

        var decimalSeparator = numberFormat.NumberDecimalSeparator;
        var decimalIndex = string.IsNullOrEmpty(decimalSeparator)
            ? -1
            : field.IndexOf(decimalSeparator, StringComparison.Ordinal);

        var integerPart = decimalIndex >= 0 ? field[..decimalIndex] : field;
        if (integerPart.Length > 0 && (integerPart[0] == '+' || integerPart[0] == '-'))
            integerPart = integerPart[1..];

        var isFirstGroup = true;
        var currentGroupDigits = 0;
        var index = 0;
        while (index < integerPart.Length)
        {
            if (integerPart[index..].StartsWith(groupSeparator, StringComparison.Ordinal))
            {
                if (isFirstGroup ? currentGroupDigits is < 1 or > 3 : currentGroupDigits != 3)
                    return false;

                isFirstGroup = false;
                currentGroupDigits = 0;
                index += groupSeparator.Length;
                continue;
            }

            if (!char.IsDigit(integerPart[index]))
                return true; // Not a plain grouped-digit shape (e.g. currency symbols) — let NumberStyles decide.

            currentGroupDigits++;
            index++;
        }

        // Valid Excel/.NET-style grouping: every group except the first has exactly 3 digits, and
        // the first (or only, if never grouped) group has 1-3 digits.
        return isFirstGroup ? currentGroupDigits is >= 1 and <= 3 : currentGroupDigits == 3;
    }

    private static string NormalizeInput(string? input) => input?.Trim() ?? "";
}
