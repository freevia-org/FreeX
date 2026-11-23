using System.Runtime.CompilerServices;
using FreeX.Core.Model;
using FreeX.Core.Formula;

namespace FreeX.Core.Commands;

public enum DataValidationInvalidEntryAction { Allow, Block, AskToContinue }

/// <summary>
/// Stateless service that evaluates data validation rules against cell values.
/// </summary>
public static partial class DataValidationService
{
    private static readonly ConditionalWeakTable<Sheet, DataValidationLookupCache> LookupCaches = new();

    public readonly record struct InputPrompt(string Title, string Message);

    /// <summary>
    /// Returns null if the value is valid according to the rule, or an error message if it is not.
    /// </summary>
    public static string? Validate(DataValidation dv, ScalarValue value)
    {
        // Blanks always pass when AllowBlank is true
        if (value is BlankValue)
            return dv.AllowBlank ? null : ValidateBlankNotIgnored(dv, sheet: null, address: null, workbook: null);

        return dv.Type switch
        {
            DvType.Any     => null,
            DvType.List    => ValidateList(dv, value),
            DvType.WholeNumber => ValidateNumeric(dv, value, requireInteger: true),
            DvType.Decimal => ValidateNumeric(dv, value),
            DvType.TextLength => ValidateTextLength(dv, value),
            DvType.Date    => ValidateDate(dv, value),
            DvType.Time    => ValidateTime(dv, value),
            DvType.Custom  => null,                         // formula-based — Phase 5
            _              => null
        };
    }

    public static string? Validate(
        DataValidation dv,
        ScalarValue value,
        Sheet sheet,
        CellAddress address,
        Workbook? workbook = null)
    {
        if (value is BlankValue)
            return dv.AllowBlank ? null : ValidateBlankNotIgnored(dv, sheet, address, workbook);

        return dv.Type switch
        {
            DvType.List => ValidateList(dv, value, sheet, address, workbook),
            DvType.WholeNumber => ValidateNumeric(dv, value, sheet, address, workbook, requireInteger: true),
            DvType.Decimal => ValidateNumeric(dv, value, sheet, address, workbook),
            DvType.TextLength => ValidateTextLength(dv, value, sheet, address, workbook),
            DvType.Date => ValidateDate(dv, value, sheet, address, workbook),
            DvType.Time => ValidateTime(dv, value, sheet, address, workbook),
            DvType.Custom => ValidateCustom(dv, value, sheet, address, workbook),
            _ => Validate(dv, value)
        };
    }

    /// <summary>
    /// "Ignore blank" unchecked: Excel does not reject an empty cell outright, it validates the empty
    /// cell as the value 0 (Whole/Decimal/Date/Time), as the empty string (Text length) or by evaluating
    /// the custom formula against the empty cell -- so e.g. a "not equal to 5" or "less than 10" rule
    /// still accepts a blank. A List rule has no empty item, so a blank stays invalid there.
    /// Measured against Range.Validation.Value (Excel-parity round 6).
    /// </summary>
    private static string? ValidateBlankNotIgnored(DataValidation dv, Sheet? sheet, CellAddress? address, Workbook? workbook)
    {
        const string required = "A value is required.";
        switch (dv.Type)
        {
            case DvType.Any:
                return null;
            case DvType.WholeNumber:
            case DvType.Decimal:
                return ValidateNumeric(dv, new NumberValue(0), sheet, address, workbook, requireInteger: dv.Type == DvType.WholeNumber) is null ? null : required;
            case DvType.Date:
                return ValidateDate(dv, new NumberValue(0), sheet, address, workbook) is null ? null : required;
            case DvType.Time:
                return ValidateTime(dv, new NumberValue(0), sheet, address, workbook) is null ? null : required;
            case DvType.TextLength:
                return ValidateTextLength(dv, new TextValue(string.Empty), sheet, address, workbook) is null ? null : required;
            case DvType.Custom:
                return sheet is null || address is null
                    ? null
                    : ValidateCustom(dv, new BlankValue(), sheet, address.Value, workbook) is null ? null : required;
            default:
                return required;
        }
    }

    /// <summary>
    /// Returns all validation rules that apply to the given cell address.
    /// </summary>
    public static IEnumerable<DataValidation> GetApplicable(Sheet sheet, CellAddress addr) =>
        GetLookupCache(sheet).GetApplicable(addr);

    public static bool AppliesTo(DataValidation dv, CellAddress addr) =>
        dv.AppliesTo.Contains(addr) || AdditionalRangeContains(dv.AdditionalRanges, addr);

    public static InputPrompt? GetInputPrompt(Sheet sheet, CellAddress addr) =>
        GetLookupCache(sheet).GetInputPrompt(addr);

    private static bool AdditionalRangeContains(IReadOnlyList<GridRange> ranges, CellAddress addr)
    {
        for (var i = 0; i < ranges.Count; i++)
        {
            if (ranges[i].Contains(addr))
                return true;
        }

        return false;
    }

    private static DataValidationLookupCache GetLookupCache(Sheet sheet)
    {
        var cache = LookupCaches.GetValue(sheet, static _ => new DataValidationLookupCache());
        cache.RefreshIfNeeded(sheet.DataValidations);
        return cache;
    }

    private sealed class DataValidationLookupCache
    {
        private int _version = -1;
        private int _count = -1;
        private DataValidation[] _rules = [];
        private Dictionary<CellAddress, List<int>> _exactRuleIndexes = [];
        private List<int> _fallbackRuleIndexes = [];

        public void RefreshIfNeeded(DataValidationCollection validations)
        {
            if (_version == validations.Version && _count == validations.Count)
                return;

            _version = validations.Version;
            _count = validations.Count;
            _rules = validations.ToArray();
            _exactRuleIndexes = new Dictionary<CellAddress, List<int>>(Math.Min(_rules.Length, 1024));
            _fallbackRuleIndexes = [];

            for (var i = 0; i < _rules.Length; i++)
            {
                var rule = _rules[i];
                var hasFallbackRange = AddLookupRange(rule.AppliesTo, i);
                for (var r = 0; r < rule.AdditionalRanges.Count; r++)
                    hasFallbackRange |= AddLookupRange(rule.AdditionalRanges[r], i);

                if (hasFallbackRange)
                    _fallbackRuleIndexes.Add(i);
            }
        }

        public IEnumerable<DataValidation> GetApplicable(CellAddress addr)
        {
            _exactRuleIndexes.TryGetValue(addr, out var exactIndexes);
            var exactPosition = 0;
            var fallbackPosition = 0;
            var lastYieldedIndex = -1;

            while ((exactIndexes is not null && exactPosition < exactIndexes.Count) ||
                   fallbackPosition < _fallbackRuleIndexes.Count)
            {
                var exactIndex = exactIndexes is not null && exactPosition < exactIndexes.Count
                    ? exactIndexes[exactPosition]
                    : int.MaxValue;
                var fallbackIndex = fallbackPosition < _fallbackRuleIndexes.Count
                    ? _fallbackRuleIndexes[fallbackPosition]
                    : int.MaxValue;

                if (exactIndex <= fallbackIndex)
                {
                    if (exactIndex != lastYieldedIndex && AppliesTo(_rules[exactIndex], addr))
                    {
                        yield return _rules[exactIndex];
                        lastYieldedIndex = exactIndex;
                    }

                    exactPosition++;
                }
                else
                {
                    if (fallbackIndex != lastYieldedIndex && AppliesTo(_rules[fallbackIndex], addr))
                    {
                        yield return _rules[fallbackIndex];
                        lastYieldedIndex = fallbackIndex;
                    }

                    fallbackPosition++;
                }
            }
        }

        public InputPrompt? GetInputPrompt(CellAddress addr)
        {
            _exactRuleIndexes.TryGetValue(addr, out var exactIndexes);
            var exactPosition = 0;
            var fallbackPosition = 0;
            var lastCheckedIndex = -1;

            while ((exactIndexes is not null && exactPosition < exactIndexes.Count) ||
                   fallbackPosition < _fallbackRuleIndexes.Count)
            {
                var exactIndex = exactIndexes is not null && exactPosition < exactIndexes.Count
                    ? exactIndexes[exactPosition]
                    : int.MaxValue;
                var fallbackIndex = fallbackPosition < _fallbackRuleIndexes.Count
                    ? _fallbackRuleIndexes[fallbackPosition]
                    : int.MaxValue;

                if (exactIndex <= fallbackIndex)
                {
                    if (exactIndex != lastCheckedIndex && AppliesTo(_rules[exactIndex], addr))
                    {
                        if (TryCreateInputPrompt(_rules[exactIndex]) is { } prompt)
                            return prompt;

                        lastCheckedIndex = exactIndex;
                    }

                    exactPosition++;
                }
                else
                {
                    if (fallbackIndex != lastCheckedIndex && AppliesTo(_rules[fallbackIndex], addr))
                    {
                        if (TryCreateInputPrompt(_rules[fallbackIndex]) is { } prompt)
                            return prompt;

                        lastCheckedIndex = fallbackIndex;
                    }

                    fallbackPosition++;
                }
            }

            return null;
        }

        private bool AddLookupRange(GridRange range, int ruleIndex)
        {
            if (!IsSingleCellRange(range))
                return true;

            var address = range.Start;
            if (!_exactRuleIndexes.TryGetValue(address, out var indexes))
            {
                indexes = [];
                _exactRuleIndexes.Add(address, indexes);
            }

            if (indexes.Count == 0 || indexes[^1] != ruleIndex)
                indexes.Add(ruleIndex);

            return false;
        }

        private static bool IsSingleCellRange(GridRange range) =>
            range.Start == range.End;
    }

    private static InputPrompt? TryCreateInputPrompt(DataValidation rule)
    {
        if (!rule.ShowInputMessage)
            return null;

        var title = rule.PromptTitle?.Trim() ?? "";
        var message = rule.PromptMessage?.Trim() ?? "";
        return title.Length == 0 && message.Length == 0
            ? null
            : new InputPrompt(title, message);
    }

    public static IReadOnlyList<string> GetListItems(DataValidation dv, Sheet sheet, Workbook? workbook = null) =>
        GetListItems(dv, sheet, dv.AppliesTo.Start, workbook);

    /// <summary>
    /// Returns the resolvable list items for <paramref name="dv"/> as they would appear for
    /// <paramref name="address"/>. A list source formula is authored as if the rule's anchor
    /// cell (<c>dv.AppliesTo.Start</c>) were active, so relative references (e.g. an
    /// <c>=INDIRECT($A2)</c> cascading-dropdown source) are shifted from that anchor to
    /// <paramref name="address"/> before evaluation, matching <see cref="ValidateList"/>.
    /// </summary>
    public static IReadOnlyList<string> GetListItems(DataValidation dv, Sheet sheet, CellAddress address, Workbook? workbook = null)
    {
        if (dv.Type != DvType.List || !dv.ShowDropdown || string.IsNullOrWhiteSpace(dv.Formula1))
            return Array.Empty<string>();

        // forDisplay: true -- these items are shown verbatim to the user (in-cell dropdown
        // ItemsSource, rule preview text), not compared against an entered value, so a
        // date-sourced item must render as a date ("2024-01-02") rather than the raw OADate
        // serial ValidateList's own matching path uses (R163-DV-F1).
        return ResolveListValues(dv.Formula1, sheet, dv.AppliesTo.Start, address, workbook, forDisplay: true);
    }

    public static string FormatListSourceRange(GridRange range, string? sheetName = null)
        => FormatListSourceRange(range, sheetName, hostSheetName: null);

    public static string FormatListSourceRange(GridRange range, string? sourceSheetName, string? hostSheetName)
    {
        var start = FormatAbsoluteCell(range.Start);
        var end = FormatAbsoluteCell(range.End);
        var reference = start == end ? start : $"{start}:{end}";
        if (string.IsNullOrWhiteSpace(sourceSheetName) ||
            string.Equals(sourceSheetName, hostSheetName, StringComparison.OrdinalIgnoreCase))
            return "=" + reference;

        return $"={SheetNameFormatter.QuoteIfNeeded(sourceSheetName)}!{reference}";
    }

    public static DataValidationInvalidEntryAction GetInvalidEntryAction(DataValidation dv)
    {
        if (!dv.ShowErrorMessage)
            return DataValidationInvalidEntryAction.Allow;

        return dv.AlertStyle == DvAlertStyle.Stop
            ? DataValidationInvalidEntryAction.Block
            : DataValidationInvalidEntryAction.AskToContinue;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static string FormatAbsoluteCell(CellAddress address) =>
        $"${CellAddress.NumberToColumnName(address.Col)}${address.Row}";

    // Excel treats a formula result as a "whole number" for WholeNumber data validation
    // even when ordinary floating-point noise means it isn't bit-exact (e.g. 5 + 0.1 - 0.1
    // can evaluate to 5.000000000000001). Use a small absolute/relative tolerance instead
    // of double.Epsilon, which only tolerates a difference of a few ULPs near zero and
    // rejects any noise that accumulates from real arithmetic.
    private const double WholeNumberTolerance = 1e-9;

    private static bool IsEffectivelyWholeNumber(double value)
    {
        double rounded = Math.Round(value);
        double diff = Math.Abs(value - rounded);
        double scale = Math.Max(1.0, Math.Abs(value));
        return diff <= WholeNumberTolerance * scale;
    }

    // Same tolerance as IsEffectivelyWholeNumber, generalized to two arbitrary values. A
    // formula result that has already been accepted as "effectively" a whole/decimal number
    // by the checks above must not then be bit-exact-compared against its Equal/NotEqual
    // bound — Between/NotBetween/GreaterThan/etc. are inherently insensitive to the same
    // ordinary FP noise via >=/<=, so Equal/NotEqual need an equivalent tolerance to match.
    private static bool IsEffectivelyEqual(double a, double b)
    {
        double diff = Math.Abs(a - b);
        double scale = Math.Max(1.0, Math.Max(Math.Abs(a), Math.Abs(b)));
        return diff <= WholeNumberTolerance * scale;
    }

    // Three-way compare that treats values within IsEffectivelyEqual's tolerance as equal.
    // Between/NotBetween/GreaterThan/LessThan/GreaterOrEqual/LessOrEqual bound checks must use
    // this instead of raw double comparisons, otherwise a value already accepted as
    // "effectively" a whole/decimal number (e.g. 10.000000000000002 ~ 10) can still be rejected
    // at the bound itself (10.000000000000002 <= 10 is false with a raw comparison).
    private static int CompareTolerant(double a, double b) =>
        IsEffectivelyEqual(a, b) ? 0 : a.CompareTo(b);

    private static string? ValidateNumeric(
        DataValidation dv,
        ScalarValue value,
        bool requireInteger = false) =>
        ValidateNumeric(dv, value, sheet: null, address: null, workbook: null, requireInteger);

    private static string? ValidateNumeric(
        DataValidation dv,
        ScalarValue value,
        Sheet? sheet,
        CellAddress? address,
        Workbook? workbook,
        bool requireInteger = false)
    {
        double numericValue;
        if (value is NumberValue nv)
            numericValue = nv.Value;
        else if (value is DateTimeValue dtv)
            numericValue = dtv.Value;
        else if (value is BoolValue boolValue)
            return ValidateBooleanAgainstNumericBounds(dv, sheet, address, workbook, DataValidationBoundsParser.TryParseNumberBound);
        else
            return dv.ErrorMessage ?? "Value must be a number.";

        if (requireInteger && !IsEffectivelyWholeNumber(numericValue))
            return dv.ErrorMessage ?? "Value must be a whole number.";

        if (!DataValidationBoundsParser.TryParseNumberBound(dv.Formula1, sheet, address, dv.AppliesTo.Start, workbook, out var v1))
            return null; // can't evaluate — treat as valid

        double v2 = 0;
        if (dv.Operator is DvOperator.Between or DvOperator.NotBetween)
        {
            if (!DataValidationBoundsParser.TryParseNumberBound(dv.Formula2, sheet, address, dv.AppliesTo.Start, workbook, out v2))
                return null;
        }

        return ValidateResolvedNumericBounds(dv, numericValue, v1, v2);
    }

    /// <summary>
    /// A logical value in a Whole/Decimal/Date/Time rule: Excel ranks TRUE/FALSE above every number, so
    /// the comparison operators resolve as if the entry were larger than any bound: greater-than,
    /// greater-or-equal, not-equal and not-between accept it; between, equal, less-than and
    /// less-or-equal reject it. The whole-number check is not applied to it.
    /// </summary>
    private delegate bool BoundParser(string? formula, Sheet? sheet, CellAddress? address, CellAddress? anchor, Workbook? workbook, out double value);

    private static string? ValidateBooleanAgainstNumericBounds(
        DataValidation dv, Sheet? sheet, CellAddress? address, Workbook? workbook, BoundParser parse)
    {
        if (!parse(dv.Formula1, sheet, address, dv.AppliesTo.Start, workbook, out _))
            return null;
        if (dv.Operator is DvOperator.Between or DvOperator.NotBetween &&
            !parse(dv.Formula2, sheet, address, dv.AppliesTo.Start, workbook, out _))
            return null;

        var passes = dv.Operator is DvOperator.NotBetween or DvOperator.NotEqual or DvOperator.GreaterThan or DvOperator.GreaterThanOrEqual;
        return passes ? null : dv.ErrorMessage ?? "Value must be a number.";
    }

    private static string? ValidateResolvedNumericBounds(
        DataValidation dv,
        double numericValue,
        double v1,
        double v2)
    {
        bool passes = dv.Operator switch
        {
            DvOperator.Between             => CompareTolerant(numericValue, v1) >= 0 && CompareTolerant(numericValue, v2) <= 0,
            DvOperator.NotBetween          => CompareTolerant(numericValue, v1) < 0 || CompareTolerant(numericValue, v2) > 0,
            DvOperator.Equal               => IsEffectivelyEqual(numericValue, v1),
            DvOperator.NotEqual            => !IsEffectivelyEqual(numericValue, v1),
            DvOperator.GreaterThan         => CompareTolerant(numericValue, v1) > 0,
            DvOperator.LessThan            => CompareTolerant(numericValue, v1) < 0,
            DvOperator.GreaterThanOrEqual  => CompareTolerant(numericValue, v1) >= 0,
            DvOperator.LessThanOrEqual     => CompareTolerant(numericValue, v1) <= 0,
            _                              => true
        };

        return passes ? null : dv.ErrorMessage ?? DataValidationErrorMessages.BuildNumericErrorMessage(dv, v1, v2);
    }

    private static string? ValidateTextLength(DataValidation dv, ScalarValue value) =>
        ValidateTextLength(dv, value, sheet: null, address: null, workbook: null);

    private static string? ValidateTextLength(
        DataValidation dv,
        ScalarValue value,
        Sheet? sheet,
        CellAddress? address,
        Workbook? workbook)
    {
        // Excel's Text Length rule validates the length of whatever was entered, regardless of
        // its type — LEN() applied to a number/date/bool renders its display text first. Only
        // an outright non-scalar/unsupported value (handled by the null fallthrough below) is
        // rejected as "must be text".
        var rendered = RenderValueForLengthCheck(value);
        if (rendered is null)
            return dv.ErrorMessage ?? "Value must be text.";

        double length = rendered.Length;

        if (!DataValidationBoundsParser.TryParseNumberBound(dv.Formula1, sheet, address, dv.AppliesTo.Start, workbook, out var v1))
            return null;

        double v2 = 0;
        if (dv.Operator is DvOperator.Between or DvOperator.NotBetween)
        {
            if (!DataValidationBoundsParser.TryParseNumberBound(dv.Formula2, sheet, address, dv.AppliesTo.Start, workbook, out v2))
                return null;
        }

        bool passes = dv.Operator switch
        {
            DvOperator.Between             => length >= v1 && length <= v2,
            DvOperator.NotBetween          => length < v1 || length > v2,
            DvOperator.Equal               => length == v1,
            DvOperator.NotEqual            => length != v1,
            DvOperator.GreaterThan         => length > v1,
            DvOperator.LessThan            => length < v1,
            DvOperator.GreaterThanOrEqual  => length >= v1,
            DvOperator.LessThanOrEqual     => length <= v1,
            _                              => true
        };

        return passes ? null : dv.ErrorMessage ?? $"Text length must satisfy the rule (length {(int)length}).";
    }

    /// <summary>
    /// Renders a scalar value the way it would appear if typed into the cell, for LEN()-style
    /// text-length validation. Returns null only for values that have no meaningful entry text
    /// (used to fall back to the "must be text" error).
    /// </summary>
    private static string? RenderValueForLengthCheck(ScalarValue value) => value switch
    {
        TextValue tv      => tv.Value,
        NumberValue nv    => nv.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        DateTimeValue dtv => dtv.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        BoolValue b       => b.Value ? "TRUE" : "FALSE",
        _                 => null
    };

    private static string? ValidateDate(DataValidation dv, ScalarValue value) =>
        ValidateDate(dv, value, sheet: null, address: null, workbook: null);

    private static string? ValidateDate(
        DataValidation dv,
        ScalarValue value,
        Sheet? sheet,
        CellAddress? address,
        Workbook? workbook)
    {
        // Dates are stored as OADate numbers or DateTimeValue
        double oaDate;
        if (value is NumberValue nv)
            oaDate = nv.Value;
        else if (value is DateTimeValue dtv)
            oaDate = dtv.Value;
        else if (value is BoolValue)
            return ValidateBooleanAgainstNumericBounds(dv, sheet, address, workbook, DataValidationBoundsParser.TryParseDateBound);
        else
            return dv.ErrorMessage ?? "Value must be a date.";

        if (!DataValidationBoundsParser.TryParseDateBound(dv.Formula1, sheet, address, dv.AppliesTo.Start, workbook, out var v1))
            return null;

        double v2 = 0;
        if (dv.Operator is DvOperator.Between or DvOperator.NotBetween)
        {
            if (!DataValidationBoundsParser.TryParseDateBound(dv.Formula2, sheet, address, dv.AppliesTo.Start, workbook, out v2))
                return null;
        }

        return ValidateResolvedNumericBounds(dv, oaDate, v1, v2);
    }

    private static string? ValidateTime(DataValidation dv, ScalarValue value) =>
        ValidateTime(dv, value, sheet: null, address: null, workbook: null);

    private static string? ValidateTime(
        DataValidation dv,
        ScalarValue value,
        Sheet? sheet,
        CellAddress? address,
        Workbook? workbook)
    {
        // Excel compares the raw serial (date part included) against the time bounds -- a value of
        // 1.5 is NOT "12:00" for a between-0.25-and-0.75 rule (Excel-parity round 6).
        double timeValue;
        if (value is NumberValue nv)
            timeValue = nv.Value;
        else if (value is DateTimeValue dtv)
            timeValue = dtv.Value;
        else if (value is BoolValue)
            return ValidateBooleanAgainstNumericBounds(dv, sheet, address, workbook, DataValidationBoundsParser.TryParseTimeBound);
        else
            return dv.ErrorMessage ?? "Value must be a time.";

        if (!DataValidationBoundsParser.TryParseTimeBound(dv.Formula1, sheet, address, dv.AppliesTo.Start, workbook, out var v1))
            return null;

        double v2 = 0;
        if (dv.Operator is DvOperator.Between or DvOperator.NotBetween)
        {
            if (!DataValidationBoundsParser.TryParseTimeBound(dv.Formula2, sheet, address, dv.AppliesTo.Start, workbook, out v2))
                return null;
        }

        return ValidateResolvedNumericBounds(dv, timeValue, v1, v2);
    }

    private static string? ValidateCustom(
        DataValidation dv,
        ScalarValue value,
        Sheet sheet,
        CellAddress address,
        Workbook? workbook)
    {
        if (string.IsNullOrWhiteSpace(dv.Formula1))
            return null;

        var original = sheet.GetCell(address)?.Clone();
        // Sheet.SetCell always tears down any live spill rooted at this address as a side effect
        // (ClearSpillRange), so capture the spill payload BEFORE staging the candidate value and
        // replay it in the finally block, mirroring CaptureSpillForRelocate's documented contract.
        // Otherwise, validating a spill anchor cell (e.g. Data Validation > Circle Invalid Data,
        // which validates every value-bearing cell including spill members) permanently blanks the
        // spilled members, since restoring only the anchor Cell object does not resurrect them.
        var capturedSpill = sheet.CaptureSpillForRelocate(address);
        try
        {
            // Write the candidate value into the cell so Formula1 can read it via its cell reference.
            sheet.SetCell(address, value);

            var formulaText = dv.Formula1.TrimStart();
            if (!formulaText.StartsWith('='))
                formulaText = "=" + formulaText;

            // Parse once so we can shift relative references from the rule's anchor cell
            // (AppliesTo.Start) to the cell actually being validated, mirroring the way
            // Excel evaluates conditional-format formula rules across a multi-cell range.
            var ast = FormulaEvaluator.ParseFormula(formulaText);
            var anchor = dv.AppliesTo.Start;
            if (anchor != address)
                ast = FormulaEvaluator.ShiftFormulaForCell(ast, anchor, address);

            // The validation formula is not stored in the cell it validates, so INDIRECT back to that cell is a plain read.
            var result = new FormulaEvaluator().Evaluate(ast, sheet, workbook, currentCell: address, isIterativeCalculationPass: true);

            // Excel treats FALSE, 0, and any error/blank as invalid.
            var passes = result switch
            {
                BoolValue b   => b.Value,
                NumberValue n => Math.Abs(n.Value) > double.Epsilon,
                _             => false
            };

            return passes ? null : dv.ErrorMessage ?? "Value does not satisfy the custom validation rule.";
        }
        finally
        {
            if (original is null)
                sheet.ClearCell(address);
            else
                sheet.SetCell(address, original);

            if (capturedSpill is not null)
                sheet.SetSpillRange(address, capturedSpill);
        }
    }

}
