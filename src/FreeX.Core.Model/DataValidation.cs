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
