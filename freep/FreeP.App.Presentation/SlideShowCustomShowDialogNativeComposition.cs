namespace FreeP.App.Compositor;

/// <summary>Composes the portable Custom Shows controller with renderer-owned controls.</summary>
public sealed class SlideShowCustomShowDialogNativeComposition<TControl, TRow>
    where TControl : class
    where TRow : class
{
    private readonly Func<
        PresentationDialogActionPlan<SlideShowCustomShowDialogAction>,
        Action,
        TControl> _createButton;

    public SlideShowCustomShowDialogNativeComposition(
        SlideShowCustomShowDialogSession session,
        TControl showList,
        TControl orderedSlideList,
        TControl nameInput,
        TControl validationText,
        Action<TControl, object?> setItemsSource,
        Action<TControl, int> setSelectedIndex,
        Func<TControl, int> getSelectedIndex,
        Func<TControl, object?> getSelectedItem,
        Action<TControl, string> setText,
        Action<TControl, bool> setChecked,
        Func<TControl, bool> getChecked,
        Action<TControl, bool> setEnabled,
        Func<string?> getName,
        Action close,
        Action clearAvailableSlideRows,
        Func<SlideShowCustomShowSlideOption, SlideShowCustomShowAvailableSlideNativeRow<TControl, TRow>> createAvailableSlideRow,
        Action<TRow> addAvailableSlideRow,
        Func<PresentationDialogActionPlan<SlideShowCustomShowDialogAction>, Action, TControl> createButton)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(getName);
        ArgumentNullException.ThrowIfNull(close);
        _createButton = createButton ?? throw new ArgumentNullException(nameof(createButton));

        Form = new(
            showList,
            orderedSlideList,
            nameInput,
            validationText,
            setItemsSource,
            setSelectedIndex,
            getSelectedIndex,
            getSelectedItem,
            setText,
            setChecked,
            getChecked,
            setEnabled);
        Controller = new(
            session,
            new SlideShowCustomShowDialogViewAdapter<TControl>(
                Form,
                getName,
                RenderAvailableSlides,
                close));
        Actions = new(Controller, close);
        Buttons = new((action, handler) => CreateButton(action, handler), Actions);
        AvailableSlides = new(
            Form,
            clearAvailableSlideRows,
            createAvailableSlideRow,
            addAvailableSlideRow);
    }

    public SlideShowCustomShowDialogFormSession<TControl> Form { get; }

    public SlideShowCustomShowDialogController Controller { get; }

    public SlideShowCustomShowDialogActionDispatcher Actions { get; }

    public SlideShowCustomShowDialogButtonSet<TControl> Buttons { get; }

    public SlideShowCustomShowAvailableSlideRendererSession<TControl, TRow> AvailableSlides { get; }

    public PresentationDialogSurfacePlan<
        SlideShowCustomShowDialogField,
        SlideShowCustomShowDialogAction> Surface => Controller.Surface;

    public TControl CreateButton(
        SlideShowCustomShowDialogAction action,
        Action handler,
        string? automationSuffix = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var button = _createButton(Surface.Action(action, automationSuffix), handler);
        if (automationSuffix is null)
            Form.RegisterAction(action, button);
        return button;
    }

    public void RenderAvailableSlides(IReadOnlyList<SlideShowCustomShowSlideOption> slides) =>
        AvailableSlides.Render(slides);
}
