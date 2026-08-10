namespace FreeX.App.Presentation.Localization;

/// <summary>A renderer-neutral validation message paired with its semantic focus target.</summary>
public sealed record ValidationPresentationDescriptor<TFocusTarget>(
    LocalizedTextDescriptor Message,
    TFocusTarget FocusTarget)
    where TFocusTarget : struct, Enum;
