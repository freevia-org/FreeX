namespace FreeP.App.Compositor;

/// <summary>
/// Shared view contract implementation for native Custom Shows dialogs. Native hosts provide
/// control transport and available-slide construction; render ordering remains shared here.
/// </summary>
public sealed class SlideShowCustomShowDialogViewAdapter<TControl> :
    ISlideShowCustomShowDialogView
    where TControl : class
{
    private readonly SlideShowCustomShowDialogFormSession<TControl> _form;
    private readonly Func<string?> _readName;
    private readonly Action<IReadOnlyList<SlideShowCustomShowSlideOption>> _rebuildSlides;
    private readonly Action _close;

    public SlideShowCustomShowDialogViewAdapter(
        SlideShowCustomShowDialogFormSession<TControl> form,
        Func<string?> readName,
        Action<IReadOnlyList<SlideShowCustomShowSlideOption>> rebuildSlides,
        Action close)
    {
        _form = form ?? throw new ArgumentNullException(nameof(form));
        _readName = readName ?? throw new ArgumentNullException(nameof(readName));
        _rebuildSlides = rebuildSlides ?? throw new ArgumentNullException(nameof(rebuildSlides));
        _close = close ?? throw new ArgumentNullException(nameof(close));
    }

    public SlideShowCustomShowDialogViewState CaptureState() =>
        new(
            _readName() ?? string.Empty,
            _form.SelectedSlideIds(),
            _form.SelectedShowIndex,
            _form.SelectedSlideIndex);

    public void RenderFullPlan(SlideShowCustomShowSessionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        _rebuildSlides(plan.AvailableSlides);
        _form.ApplyFullPlan(plan);
    }

    public void RenderSelectedShowPlan(SlideShowCustomShowSessionPlan plan) =>
        _form.ApplySelectedShowPlan(plan);

    public void ApplySlideSelection(SlideShowCustomShowSessionPlan plan) =>
        _form.ApplySlideSelection(plan);

    public void SetValidation(string? message) => _form.SetValidation(message);

    public void CloseDialog() => _close();
}
