namespace FreeX.Core.Model;

/// <summary>The type of data validation rule.</summary>
public enum DvType { Any, WholeNumber, Decimal, List, Date, Time, TextLength, Custom }

/// <summary>Comparison operator for data validation rules.</summary>
public enum DvOperator { Between, NotBetween, Equal, NotEqual, GreaterThan, LessThan, GreaterThanOrEqual, LessThanOrEqual }

/// <summary>Alert style shown when a validation rule rejects input.</summary>
public enum DvAlertStyle { Stop, Warning, Information }

/// <summary>
/// A data validation rule applied to a rectangular range of cells.
/// </summary>
public sealed class DataValidation
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>The range on the sheet this rule covers.</summary>
    public GridRange AppliesTo { get; set; }

    /// <summary>Additional discontiguous ranges covered by the same Excel validation rule.</summary>
    public List<GridRange> AdditionalRanges { get; } = [];

    public DvType Type { get; set; } = DvType.Any;
    public DvOperator Operator { get; set; } = DvOperator.Between;

    /// <summary>Value1, or comma-separated list items for List type.</summary>
    public string? Formula1 { get; set; }

    /// <summary>Value2 — used only for Between / NotBetween operators.</summary>
    public string? Formula2 { get; set; }

    public bool AllowBlank { get; set; } = true;
    public bool ShowDropdown { get; set; } = true;
    public DvAlertStyle AlertStyle { get; set; } = DvAlertStyle.Stop;
    public bool ShowInputMessage { get; set; } = true;
    public bool ShowErrorMessage { get; set; } = true;

    public string? ErrorTitle { get; set; }
    public string? ErrorMessage { get; set; }
    public string? PromptTitle { get; set; }
    public string? PromptMessage { get; set; }
    public IReadOnlyDictionary<string, string>? NativeAttributes { get; set; }
    public IReadOnlyList<string>? NativeChildXmls { get; set; }
    public IReadOnlyDictionary<string, string>? NativeContainerAttributes { get; set; }
    public IReadOnlyList<string>? NativeContainerChildXmls { get; set; }

    /// <summary>
    /// When true, the validation originated from (or must be written to) the worksheet x14 extLst
    /// block (<x14:dataValidation> with <xm:f>/<xm:sqref>). Excel 2010+ uses this path for List
    /// validations whose source formula references another sheet or is otherwise too long for the
    /// legacy &lt;dataValidation&gt; element. The legacy element is kept with an empty formula1 so
    /// older readers can still open the file; the x14 block carries the real formula.
    /// </summary>
    public bool IsX14 { get; set; }

    public DataValidation Clone() =>
        CloneForRanges(AppliesTo, AdditionalRanges, Id);

    /// <summary>
    /// Clones the rule onto new range(s) under a FRESH <see cref="Id"/> -- the shape every COPY of a
    /// rule uses (Format Painter, Paste Validation, the subtract-and-replace loops that split a rule
    /// around a cleared footprint, sheet duplication, grouped-sheet fan-out).
    /// <para>
    /// r256 asked whether the minting is worth its cost and decided to KEEP it. The cost is small
    /// and bounded: <see cref="Id"/> is a purely in-memory identity -- it appears in no writer and no
    /// reader (the OOXML <c>dataValidation</c> element has no such attribute, and
    /// <c>NativeJsonAdapter</c>'s DTO carries no Id field), so re-copying a rule cannot change a
    /// saved byte. What it would buy is not worth it: a copy that kept its source's Id lands in the
    /// SAME <c>Sheet.DataValidations</c> list as its source in the ordinary same-sheet paint or
    /// paste, and two rules there under one Id collide in every consumer that resolves a rule by it
    /// -- <c>SetDataValidationCommand.FindDataValidationIndex</c> and
    /// <c>FindDataValidationReplacement</c> take the first match, so editing the copy would rewrite
    /// the source; <c>RowColumnShiftHelpers.Rules</c> keys its formula snapshot on
    /// <c>(rule.Id, slot)</c>, so the second rule would overwrite the first's entry and undo of a
    /// row insert would restore the wrong formula into one of them.
    /// </para>
    /// <para>
    /// The churn's one real consequence -- that a re-copy could never be recognised as a no-op --
    /// is addressed instead by <see cref="SameAs(DataValidation, bool)"/>'s <c>ignoreIdentity</c>
    /// option, which lets a command compare CONTENT without either side pretending the two rules are
    /// the same object. A SNAPSHOT, by contrast, must preserve identity and uses
    /// <see cref="Clone"/>: restoring an undo under fresh Ids would make undo an edit of its own.
    /// </para>
    /// </summary>
    public DataValidation CloneWithNewIdentity(
        GridRange appliesTo,
        IEnumerable<GridRange>? additionalRanges = null) =>
        CloneForRanges(appliesTo, additionalRanges, Guid.NewGuid());

    public DataValidation CloneForRanges(
        GridRange appliesTo,
        IEnumerable<GridRange>? additionalRanges,
        Guid id)
    {
        var clone = new DataValidation
        {
            Id = id,
            AppliesTo = appliesTo,
            Type = Type,
            Operator = Operator,
            Formula1 = Formula1,
            Formula2 = Formula2,
            AllowBlank = AllowBlank,
            ShowDropdown = ShowDropdown,
            AlertStyle = AlertStyle,
            ShowInputMessage = ShowInputMessage,
            ShowErrorMessage = ShowErrorMessage,
            ErrorTitle = ErrorTitle,
            ErrorMessage = ErrorMessage,
            PromptTitle = PromptTitle,
            PromptMessage = PromptMessage,
            NativeAttributes = NativeAttributes,
            NativeChildXmls = NativeChildXmls,
            NativeContainerAttributes = NativeContainerAttributes,
            NativeContainerChildXmls = NativeContainerChildXmls,
            IsX14 = IsX14
        };
        clone.AdditionalRanges.AddRange(additionalRanges ?? []);
        return clone;
    }

    /// <summary>
    /// r250: content comparison, checked against <see cref="CloneForRanges"/> by
    /// R250_DataValidationComparisonCoverageContractTests -- the r249 shape, where the type's
    /// own clone is the field list because it has to be complete or cloning loses data.
    /// <para>
    /// The four Native* members and AdditionalRanges are collections. The clone assigns the
    /// first four BY REFERENCE, so a clone compares equal to its source on them -- but two
    /// independently built rules with identical content do not, which is the case a no-op
    /// decision actually faces. They are compared by content here for that reason.
    /// </para>
    /// <para>
    /// r256: <paramref name="ignoreIdentity"/> compares everything EXCEPT <see cref="Id"/>, for the
    /// callers that must decide "is this the same rule content" across a COPY. Copying mints a fresh
    /// Id by design (see <see cref="CloneWithNewIdentity"/>), so for Format Painter and Paste
    /// Validation the default form can never fire. It is opt-in rather than the default because the
    /// Id is a real distinction everywhere else: <c>SetDataValidationCommand</c>'s equal-rule check
    /// is deciding whether ONE rule was edited, and there the identity has to match.
    /// </para>
    /// </summary>
    public bool SameAs(DataValidation other, bool ignoreIdentity = false)
    {
        ArgumentNullException.ThrowIfNull(other);

        return (ignoreIdentity || Id == other.Id)
            && Equals(AppliesTo, other.AppliesTo)
            && Type == other.Type
            && Operator == other.Operator
            && string.Equals(Formula1, other.Formula1, StringComparison.Ordinal)
            && string.Equals(Formula2, other.Formula2, StringComparison.Ordinal)
            && AllowBlank == other.AllowBlank
            && ShowDropdown == other.ShowDropdown
            && AlertStyle == other.AlertStyle
            && ShowInputMessage == other.ShowInputMessage
            && ShowErrorMessage == other.ShowErrorMessage
            && string.Equals(ErrorTitle, other.ErrorTitle, StringComparison.Ordinal)
            && string.Equals(ErrorMessage, other.ErrorMessage, StringComparison.Ordinal)
            && string.Equals(PromptTitle, other.PromptTitle, StringComparison.Ordinal)
            && string.Equals(PromptMessage, other.PromptMessage, StringComparison.Ordinal)
            && IsX14 == other.IsX14
            && SameMap(NativeAttributes, other.NativeAttributes)
            && SameMap(NativeContainerAttributes, other.NativeContainerAttributes)
            && SameList(NativeChildXmls, other.NativeChildXmls)
            && SameList(NativeContainerChildXmls, other.NativeContainerChildXmls)
            && AdditionalRanges.Count == other.AdditionalRanges.Count
            && AdditionalRanges.SequenceEqual(other.AdditionalRanges);
    }

    private static bool SameList(IReadOnlyList<string>? left, IReadOnlyList<string>? right)
    {
        if (left is null || right is null)
            return left is null && right is null;

        return left.Count == right.Count && left.SequenceEqual(right, StringComparer.Ordinal);
    }

    private static bool SameMap(
        IReadOnlyDictionary<string, string>? left,
        IReadOnlyDictionary<string, string>? right)
    {
        if (left is null || right is null)
            return left is null && right is null;

        return left.Count == right.Count
            && left.All(entry => right.TryGetValue(entry.Key, out var value)
                && string.Equals(entry.Value, value, StringComparison.Ordinal));
    }

    public bool Overlaps(GridRange range)
    {
        if (AppliesTo.Overlaps(range))
            return true;

        foreach (var additionalRange in AdditionalRanges)
        {
            if (additionalRange.Overlaps(range))
                return true;
        }

        return false;
    }

    public bool Overlaps(DataValidation other)
    {
        ArgumentNullException.ThrowIfNull(other);

        if (other.Overlaps(AppliesTo))
            return true;

        foreach (var additionalRange in AdditionalRanges)
        {
            if (other.Overlaps(additionalRange))
                return true;
        }

        return false;
    }

    public bool HasSameSettings(DataValidation? other, bool includeNativeMetadata = false)
    {
        if (other is null ||
            Type != other.Type ||
            Operator != other.Operator ||
            !string.Equals(Formula1, other.Formula1, StringComparison.Ordinal) ||
            !string.Equals(Formula2, other.Formula2, StringComparison.Ordinal) ||
            AllowBlank != other.AllowBlank ||
            ShowDropdown != other.ShowDropdown ||
            AlertStyle != other.AlertStyle ||
            ShowInputMessage != other.ShowInputMessage ||
            ShowErrorMessage != other.ShowErrorMessage ||
            !string.Equals(ErrorTitle, other.ErrorTitle, StringComparison.Ordinal) ||
            !string.Equals(ErrorMessage, other.ErrorMessage, StringComparison.Ordinal) ||
            !string.Equals(PromptTitle, other.PromptTitle, StringComparison.Ordinal) ||
            !string.Equals(PromptMessage, other.PromptMessage, StringComparison.Ordinal))
        {
            return false;
        }

        return !includeNativeMetadata ||
            IsX14 == other.IsX14 &&
            DictionaryEquals(NativeAttributes, other.NativeAttributes) &&
            SequenceEquals(NativeChildXmls, other.NativeChildXmls) &&
            DictionaryEquals(NativeContainerAttributes, other.NativeContainerAttributes) &&
            SequenceEquals(NativeContainerChildXmls, other.NativeContainerChildXmls);
    }

    public bool HasSameDefinition(DataValidation? other, bool includeNativeMetadata = true) =>
        other is not null &&
        AppliesTo == other.AppliesTo &&
        AdditionalRanges.SequenceEqual(other.AdditionalRanges) &&
        HasSameSettings(other, includeNativeMetadata);

    private static bool DictionaryEquals(
        IReadOnlyDictionary<string, string>? left,
        IReadOnlyDictionary<string, string>? right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left is null || right is null || left.Count != right.Count)
            return false;

        foreach (var (key, value) in left)
        {
            if (!right.TryGetValue(key, out var rightValue) ||
                !string.Equals(value, rightValue, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SequenceEquals(
        IReadOnlyList<string>? left,
        IReadOnlyList<string>? right) =>
        ReferenceEquals(left, right) ||
        left is not null && right is not null && left.SequenceEqual(right, StringComparer.Ordinal);
}
