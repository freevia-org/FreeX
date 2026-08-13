namespace Free.Shared.AppServices;

/// <summary>
/// Portable completion states for application operations. Product-specific result types can map
/// richer reasons onto validation or error details without redefining the common state machine.
/// </summary>
public enum OperationStatus
{
    Completed,
    Cancelled,
    Declined,
    Unavailable,
    ValidationFailed,
    Failed,
}

public sealed record OperationValidation<TDetail>
{
    public OperationValidation(TDetail detail)
    {
        ArgumentNullException.ThrowIfNull(detail);
        Detail = detail;
    }

    public TDetail Detail { get; }
}

public sealed record OperationError<TDetail>
{
    public OperationError(TDetail detail, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(detail);
        ArgumentNullException.ThrowIfNull(exception);
        Detail = detail;
        Exception = exception;
    }

    public TDetail Detail { get; }
    public Exception Exception { get; }
}

/// <summary>
/// Renderer- and product-neutral operation result. The generic details preserve domain-specific
/// validation and failure information while the shared factories own status and payload defaults.
/// </summary>
public sealed record OperationOutcome<TValue, TValidationDetail, TErrorDetail>
{
    private OperationOutcome(
        OperationStatus status,
        TValue? value,
        string? path,
        OperationValidation<TValidationDetail>? validation,
        OperationError<TErrorDetail>? error,
        Exception? exception)
    {
        Status = status;
        Value = value;
        Path = path;
        Validation = validation;
        Error = error;
        Exception = error?.Exception ?? exception;
    }

    public OperationStatus Status { get; }
    public TValue? Value { get; }
    public string? Path { get; }
    public OperationValidation<TValidationDetail>? Validation { get; }
    public OperationError<TErrorDetail>? Error { get; }
    public Exception? Exception { get; }

    public bool Succeeded => Status == OperationStatus.Completed;
    public bool Cancelled => Status == OperationStatus.Cancelled;

    public static OperationOutcome<TValue, TValidationDetail, TErrorDetail> Completed(
        TValue? value = default,
        string? path = null) =>
        new(OperationStatus.Completed, value, path, validation: null, error: null, exception: null);

    public static OperationOutcome<TValue, TValidationDetail, TErrorDetail> Cancel(
        TValue? value = default,
        string? path = null,
        Exception? exception = null) =>
        new(OperationStatus.Cancelled, value, path, validation: null, error: null, exception);

    public static OperationOutcome<TValue, TValidationDetail, TErrorDetail> Decline(
        TValue? value = default,
        string? path = null) =>
        new(OperationStatus.Declined, value, path, validation: null, error: null, exception: null);

    public static OperationOutcome<TValue, TValidationDetail, TErrorDetail> Unavailable(
        TValue? value = default,
        string? path = null) =>
        new(OperationStatus.Unavailable, value, path, validation: null, error: null, exception: null);

    public static OperationOutcome<TValue, TValidationDetail, TErrorDetail> ValidationFailure(
        TValidationDetail validationDetail,
        TValue? value = default,
        string? path = null) =>
        new(
            OperationStatus.ValidationFailed,
            value,
            path,
            new OperationValidation<TValidationDetail>(validationDetail),
            error: null,
            exception: null);

    public static OperationOutcome<TValue, TValidationDetail, TErrorDetail> ValidationFailure(
        TValidationDetail validationDetail,
        TErrorDetail errorDetail,
        Exception exception,
        TValue? value = default,
        string? path = null) =>
        new(
            OperationStatus.ValidationFailed,
            value,
            path,
            new OperationValidation<TValidationDetail>(validationDetail),
            new OperationError<TErrorDetail>(errorDetail, exception),
            exception);

    public static OperationOutcome<TValue, TValidationDetail, TErrorDetail> Failure(
        TErrorDetail errorDetail,
        Exception exception,
        TValue? value = default,
        string? path = null) =>
        new(
            OperationStatus.Failed,
            value,
            path,
            validation: null,
            new OperationError<TErrorDetail>(errorDetail, exception),
            exception);

    public static OperationOutcome<TValue, TValidationDetail, TErrorDetail> Failure(
        TErrorDetail errorDetail,
        Exception exception,
        TValidationDetail validationDetail,
        TValue? value = default,
        string? path = null) =>
        new(
            OperationStatus.Failed,
            value,
            path,
            new OperationValidation<TValidationDetail>(validationDetail),
            new OperationError<TErrorDetail>(errorDetail, exception),
            exception);
}
