using System.Globalization;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Dialogs;

/// <summary>
/// The discrete input controls a data-validation rule editor can surface. A validation type maps to the
/// subset it needs (see <see cref="DataValidationDialogModel"/>).
/// </summary>
public enum DvInputField
{
    /// <summary>The comparison operator (hidden for Any / List / Custom).</summary>
    Operator,

    /// <summary>The first criteria box (Minimum / Value / Source / Formula depending on type).</summary>
    Formula1,

    /// <summary>The second criteria box, only for Between / NotBetween operators.</summary>
    Formula2,

    /// <summary>The "ignore blank" toggle.</summary>
    AllowBlank,

    /// <summary>The in-cell dropdown toggle (List only).</summary>
    ShowDropdown
}

/// <summary>
/// The dynamic label shown above <see cref="DvInputField.Formula1"/>. The desktop dialog relabels this
/// box by validation type and operator, so a renderer can bind the label without re-deriving it.
/// </summary>
public enum DvFormula1Label
{
    /// <summary>Box is hidden (Any).</summary>
    None,

    /// <summary>"Minimum" — a Between / NotBetween operator with a scalar type.</summary>
    Minimum,

    /// <summary>"Value" — any other scalar operator.</summary>
    Value,

    /// <summary>"Source" — the List type.</summary>
    Source,

    /// <summary>"Formula" — the Custom type.</summary>
    Formula
}

/// <summary>The field a <see cref="DvValidationError"/> refers to.</summary>
public enum DvValidationTarget
{
    Formula1,
    Formula2
}

/// <summary>The stable reason code behind a data-validation editor failure.</summary>
public enum DvValidationErrorKind
{
    None,
    SourceRequired,
    FormulaRequired,
    ValueRequired,
    MaximumRequired,
    InvalidWholeNumberCriteria,
    InvalidDecimalCriteria,
    InvalidDateCriteria,
    InvalidTimeCriteria,
    InvalidTextLengthCriteria,
    InvalidListCriteria,
    InvalidCustomCriteria
}

/// <summary>A single validation failure produced by <see cref="DataValidationDialogModel.Validate"/>.</summary>
public sealed record DvValidationError(DvValidationTarget Target, string Message)
{
    public DvValidationErrorKind Kind { get; init; } = DvValidationErrorKind.None;
}

/// <summary>The outcome of validating candidate criteria against a schema.</summary>
public sealed record DvValidationResult(IReadOnlyList<DvValidationError> Errors)
{
    public bool IsValid => Errors.Count == 0;

    public static DvValidationResult Valid { get; } = new([]);

    /// <summary>The first failure, or null when the input is valid.</summary>
    public DvValidationError? FirstError => Errors.Count == 0 ? null : Errors[0];
}

/// <summary>
/// The visibility/enable state of the Input-message and Error-message editor groups, plus the alert
/// style. Mirrors the desktop dialog's "Input Message" / "Error Alert" tabs: each group's editors are
/// enabled only when its show-checkbox is ticked, and the alert-style picker tracks the error group.
/// </summary>
public sealed record DvMessageVisibility(
    bool ShowInputMessage,
    bool ShowErrorMessage,
    DvAlertStyle AlertStyle)
{
    /// <summary>Input title/body editors are enabled only when the input message is shown.</summary>
    public bool InputEditorsEnabled => ShowInputMessage;

    /// <summary>Error title/body editors are enabled only when the error alert is shown.</summary>
    public bool ErrorEditorsEnabled => ShowErrorMessage;

    /// <summary>The alert-style picker is enabled only when the error alert is shown.</summary>
    public bool AlertStyleEnabled => ShowErrorMessage;

    /// <summary>Derives the message-visibility state from a rule.</summary>
    public static DvMessageVisibility FromRule(DataValidation rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        return new DvMessageVisibility(rule.ShowInputMessage, rule.ShowErrorMessage, rule.AlertStyle);
    }

    /// <summary>The default state for a new rule: both messages shown, Stop alert.</summary>
    public static DvMessageVisibility Default { get; } = new(true, true, DvAlertStyle.Stop);
}

/// <summary>The candidate criteria a data-validation editor collects before committing a rule.</summary>
public sealed record DvCriteriaInput
{
    public DvType Type { get; init; } = DvType.Any;
    public DvOperator Operator { get; init; } = DvOperator.Between;
    public string? Formula1 { get; init; }
    public string? Formula2 { get; init; }
}

/// <summary>
/// Portable model describing, per data-validation type, which controls a rule editor surfaces, the
/// dynamic Formula1 label, which operators are valid, the allow-blank / in-cell-dropdown defaults, and a
/// validator for candidate criteria. This mirrors the field layout and per-type criteria validation the
/// desktop data-validation dialog enforces, with the rendering left to a renderer.
/// </summary>
/// <remarks>
/// The per-type "shape" facts (ShowsOperator / ShowsDropdown / RequiresFormula) and the
/// <see cref="DataValidation.RequiresSecondFormula"/> rule are also computed by the existing portable
/// rule-type-metadata planner in the app-services layer; that planner lives outside this layer's
/// dependency set, so the small shape mapping is restated here. The dynamic Formula1 label, the
/// per-type operator list, the message-visibility state, and the candidate-criteria validator are new
/// here — the services planner does not provide them.
/// </remarks>
public sealed record DataValidationDialogModel(
    DvType Type,
    IReadOnlyList<DvInputField> Fields,
    IReadOnlyList<DvOperator> Operators,
    bool AllowBlankDefault,
    bool ShowDropdownDefault)
{
    /// <summary>The full operator set, in the order the desktop dialog lists them.</summary>
    public static readonly IReadOnlyList<DvOperator> AllOperators =
    [
        DvOperator.Between,
        DvOperator.NotBetween,
        DvOperator.Equal,
        DvOperator.NotEqual,
        DvOperator.GreaterThan,
        DvOperator.LessThan,
        DvOperator.GreaterThanOrEqual,
        DvOperator.LessThanOrEqual
    ];

    private static readonly IReadOnlyList<DvOperator> NoOperators = [];

    /// <summary>True when the schema surfaces the given control.</summary>
    public bool HasField(DvInputField field) => Fields.Contains(field);

    /// <summary>True when the operator picker is shown (scalar types only).</summary>
    public bool ShowsOperator => HasField(DvInputField.Operator);

    /// <summary>True when the in-cell-dropdown toggle is shown (List only).</summary>
    public bool ShowsDropdown => HasField(DvInputField.ShowDropdown);

    /// <summary>Resolves the schema for a validation type, describing the controls its editor surfaces.</summary>
    public static DataValidationDialogModel ForType(DvType type)
    {
        var (fields, operators) = type switch
        {
            DvType.Any => (
                Array.Empty<DvInputField>(),
                NoOperators),

            DvType.List => (
                new[] { DvInputField.Formula1, DvInputField.AllowBlank, DvInputField.ShowDropdown },
                NoOperators),

            DvType.Custom => (
                new[] { DvInputField.Formula1, DvInputField.AllowBlank },
                NoOperators),

            // Whole number / Decimal / Date / Time / Text length: a scalar comparison.
            _ => (
                new[]
                {
                    DvInputField.Operator,
                    DvInputField.Formula1,
                    DvInputField.Formula2,
                    DvInputField.AllowBlank
                },
                AllOperators)
        };

        return new DataValidationDialogModel(
            type,
            fields,
            operators,
            AllowBlankDefault: true,
            // The desktop dialog defaults the in-cell dropdown on, and only persists it for List rules.
            ShowDropdownDefault: type == DvType.List);
    }

    /// <summary>True when this type accepts the given operator.</summary>
    public bool SupportsOperator(DvOperator op) => Operators.Contains(op);

    /// <summary>
    /// Whether the second criteria box is shown for the given operator. Only scalar types with a
    /// Between / NotBetween operator use Formula2; matches the desktop dialog.
    /// </summary>
    public bool ShowsFormula2(DvOperator op) =>
        HasField(DvInputField.Formula2) && op is DvOperator.Between or DvOperator.NotBetween;

    /// <summary>The dynamic label for the Formula1 box, given the chosen operator.</summary>
    public DvFormula1Label Formula1LabelFor(DvOperator op) => Type switch
    {
        DvType.Any => DvFormula1Label.None,
        DvType.List => DvFormula1Label.Source,
        DvType.Custom => DvFormula1Label.Formula,
        _ => op is DvOperator.Between or DvOperator.NotBetween
            ? DvFormula1Label.Minimum
            : DvFormula1Label.Value
    };

    /// <summary>
    /// Validates candidate criteria against this type's rules, returning every failure found (or
    /// <see cref="DvValidationResult.Valid"/> when the criteria are complete and well-formed). Mirrors
    /// the desktop dialog's per-type criteria checks (numbers, dates, times, text length, list, custom
    /// formula) including the implicit-equals and formula-reference allowances.
    /// </summary>
    public DvValidationResult Validate(DvCriteriaInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (Type == DvType.Any)
            return DvValidationResult.Valid;

        var first = input.Formula1?.Trim() ?? "";
        if (first.Length == 0)
        {
            var (kind, message) = Type switch
            {
                DvType.List => (DvValidationErrorKind.SourceRequired, "A list source is required."),
                DvType.Custom => (DvValidationErrorKind.FormulaRequired, "A formula is required."),
                _ => (DvValidationErrorKind.ValueRequired, "A value is required.")
            };
            return Fail(DvValidationTarget.Formula1, kind, message);
        }

        if (!TryValidateSingleCriteria(first, out var firstError))
            return Fail(DvValidationTarget.Formula1, InvalidCriteriaKindForType(Type), firstError!);

        if (!ShowsFormula2(input.Operator))
            return DvValidationResult.Valid;

        var second = input.Formula2?.Trim() ?? "";
        if (second.Length == 0)
            return Fail(DvValidationTarget.Formula2, DvValidationErrorKind.MaximumRequired, "A maximum value is required.");

        return TryValidateSingleCriteria(second, out var secondError)
            ? DvValidationResult.Valid
            : Fail(DvValidationTarget.Formula2, InvalidCriteriaKindForType(Type), secondError!);
    }

    private static DvValidationResult Fail(DvValidationTarget target, DvValidationErrorKind kind, string message) =>
        new([new DvValidationError(target, message) { Kind = kind }]);

    private static DvValidationErrorKind InvalidCriteriaKindForType(DvType type) => type switch
    {
        DvType.WholeNumber => DvValidationErrorKind.InvalidWholeNumberCriteria,
        DvType.Decimal => DvValidationErrorKind.InvalidDecimalCriteria,
        DvType.Date => DvValidationErrorKind.InvalidDateCriteria,
        DvType.Time => DvValidationErrorKind.InvalidTimeCriteria,
        DvType.TextLength => DvValidationErrorKind.InvalidTextLengthCriteria,
        DvType.List => DvValidationErrorKind.InvalidListCriteria,
        DvType.Custom => DvValidationErrorKind.InvalidCustomCriteria,
        _ => DvValidationErrorKind.None
    };

    private bool TryValidateSingleCriteria(string text, out string? error)
    {
        error = null;
        return Type switch
        {
            DvType.WholeNumber => Check(IsWholeNumberCriteria(text), "Enter a whole number or a formula.", out error),
            DvType.Decimal => Check(IsDecimalCriteria(text), "Enter a number or a formula.", out error),
            DvType.Date => Check(IsDateCriteria(text), "Enter a valid date or a formula.", out error),
            DvType.Time => Check(IsTimeCriteria(text), "Enter a valid time or a formula.", out error),
            DvType.TextLength => Check(IsTextLengthCriteria(text), "Enter a non-negative whole number or a formula.", out error),
            DvType.List => Check(IsListCriteria(text), "Enter a list of items or a range reference.", out error),
            DvType.Custom => Check(IsCustomCriteria(text), "Enter a valid formula.", out error),
            _ => true
        };
    }

    private static bool Check(bool valid, string message, out string? error)
    {
        error = valid ? null : message;
        return valid;
    }

    private static bool IsWholeNumberCriteria(string text) =>
        IsFormulaCriteria(text) || (TryParseNumber(text, out var value) && IsWholeNumber(value));

    private static bool IsDecimalCriteria(string text) =>
        IsFormulaCriteria(text) || TryParseNumber(text, out _);

    private static bool IsDateCriteria(string text) =>
        IsFormulaCriteria(text) ||
        TryParseNumber(text, out _) ||
        DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out _) ||
        DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    private static bool IsTimeCriteria(string text)
    {
        if (IsFormulaCriteria(text))
            return true;

        if (TryParseNumber(text, out var fraction) && fraction is >= 0 and < 1)
            return true;

        if ((TimeSpan.TryParse(text, CultureInfo.CurrentCulture, out var span) ||
             TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out span)) &&
            span >= TimeSpan.Zero && span < TimeSpan.FromDays(1))
        {
            return true;
        }

        return (DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out var dt) ||
                DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt)) &&
               dt.TimeOfDay >= TimeSpan.Zero && dt.TimeOfDay < TimeSpan.FromDays(1);
    }

    private static bool IsTextLengthCriteria(string text) =>
        IsFormulaCriteria(text) ||
        (TryParseNumber(text, out var value) && IsWholeNumber(value) && value >= 0);

    private static bool IsListCriteria(string text)
    {
        // Excel places no upper bound on the size of a range referenced as a List validation
        // source (a full-column reference is a legal source), so any well-formed formula
        // reference is accepted regardless of how many cells it spans.
        return IsFormulaCriteria(text) || HasInlineListItem(text);
    }

    private static bool IsCustomCriteria(string text) =>
        TryParseFormula(text, allowImplicitFormula: true);

    private static bool IsFormulaCriteria(string text) =>
        text.TrimStart().StartsWith('=') && TryParseFormula(text, allowImplicitFormula: false);

    private static bool TryParseFormula(string text, bool allowImplicitFormula)
    {
        var formula = text.Trim();
        if (formula.Length == 0)
            return false;

        if (!formula.StartsWith('='))
        {
            if (!allowImplicitFormula)
                return false;

            formula = "=" + formula;
        }

        try
        {
            _ = new Parser(new Lexer(formula).Tokenize()).Parse();
            return true;
        }
        catch (FormulaParseException)
        {
            return false;
        }
    }

    private static bool HasInlineListItem(string text)
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

    // Delegates to the ONE shared parse (FreeX.Core.Model.DataValidationNumericBoundText) also used
    // by live enforcement while the session runs (DataValidationBoundsParser) and by save-time
    // canonicalization (XlsxDataValidationClosedXmlMapper). Before this was unified, this
    // dialog-entry gate used NumberStyles.Float -- no thousands grouping at all -- so a
    // legitimately thousands-grouped bound like "1,234" was rejected here as invalid input even
    // though live-eval and save both accepted and parsed it; a value that got past this gate any
    // other way (e.g. loaded from a file) could then enforce one number in-session and a different
    // one after save/reload, because the three call sites disagreed on what the text even meant.
    private static bool TryParseNumber(string text, out double value) =>
        DataValidationNumericBoundText.TryParse(text, out value);

    private static bool IsWholeNumber(double value) =>
        double.IsFinite(value) && Math.Abs(value - Math.Round(value)) <= double.Epsilon;
}
