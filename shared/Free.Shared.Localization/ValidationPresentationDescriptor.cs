namespace Free.Shared.Localization;

/// <summary>A renderer-neutral localized validation message paired with its semantic focus target.</summary>
public record ValidationPresentationDescriptor<TFocusTarget>(
    LocalizedTextDescriptor Message,
    TFocusTarget FocusTarget)
    where TFocusTarget : struct, Enum;
