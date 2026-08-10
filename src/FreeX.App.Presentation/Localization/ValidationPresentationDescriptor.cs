namespace FreeX.App.Presentation.Localization;

/// <summary>FreeX-compatible facade over the shared validation presentation contract.</summary>
public sealed record ValidationPresentationDescriptor<TFocusTarget>
    : Free.Shared.Localization.ValidationPresentationDescriptor<TFocusTarget>
    where TFocusTarget : struct, Enum
{
    public ValidationPresentationDescriptor(
        LocalizedTextDescriptor Message,
        TFocusTarget FocusTarget)
        : base(Message, FocusTarget)
    {
    }

    public new LocalizedTextDescriptor Message =>
        (LocalizedTextDescriptor)base.Message;

    public void Deconstruct(
        out LocalizedTextDescriptor Message,
        out TFocusTarget FocusTarget)
    {
        Message = this.Message;
        FocusTarget = this.FocusTarget;
    }
}
