namespace Free.Shared.AppServices;

/// <summary>
/// Product- and renderer-neutral result for a picker that either supplies a selection or ends
/// before selection. Product ports can retain domain-specific request and selection types while
/// sharing one status, validation, and message contract.
/// </summary>
public sealed record PickerOutcome<TSelection>
{
    private PickerOutcome(
        OperationOutcome<TSelection, string, string> operation,
        string? message)
    {
        Operation = operation;
        Message = message;
    }

    public OperationOutcome<TSelection, string, string> Operation { get; }
    public OperationStatus Status => Operation.Status;
    public TSelection? Selection => Operation.Value;
    public string? Message { get; }
    public bool IsSelected => Status == OperationStatus.Completed;

    public static PickerOutcome<TSelection> Selected(TSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        return new(OperationOutcome<TSelection, string, string>.Completed(selection), message: null);
    }

    public static PickerOutcome<TSelection> Cancelled { get; } =
        new(OperationOutcome<TSelection, string, string>.Cancel(), message: null);

    public static PickerOutcome<TSelection> Unavailable(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return new(OperationOutcome<TSelection, string, string>.Unavailable(), message);
    }

    public static PickerOutcome<TSelection> Invalid(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return new(OperationOutcome<TSelection, string, string>.ValidationFailure(message), message);
    }
}
