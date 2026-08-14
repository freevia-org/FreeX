namespace FreeP.App.Compositor;

/// <summary>Routes renderer actions for the paired native Custom Shows dialogs.</summary>
public sealed class SlideShowCustomShowDialogActionDispatcher
{
    private readonly SlideShowCustomShowDialogController _controller;
    private readonly Action _close;

    public SlideShowCustomShowDialogActionDispatcher(
        SlideShowCustomShowDialogController controller,
        Action close)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _close = close ?? throw new ArgumentNullException(nameof(close));
    }

    public void Execute(SlideShowCustomShowDialogAction action, string? slideId = null)
    {
        switch (action)
        {
            case SlideShowCustomShowDialogAction.Create:
                _controller.Create();
                break;
            case SlideShowCustomShowDialogAction.Rename:
                _controller.Rename();
                break;
            case SlideShowCustomShowDialogAction.UpdateSlides:
                _controller.UpdateSlides();
                break;
            case SlideShowCustomShowDialogAction.Delete:
                _controller.Delete();
                break;
            case SlideShowCustomShowDialogAction.StartShow:
                _controller.StartShow();
                break;
            case SlideShowCustomShowDialogAction.MoveUp:
                _controller.MoveSelectedSlide(-1);
                break;
            case SlideShowCustomShowDialogAction.MoveDown:
                _controller.MoveSelectedSlide(1);
                break;
            case SlideShowCustomShowDialogAction.Remove:
                _controller.RemoveSelectedSlide();
                break;
            case SlideShowCustomShowDialogAction.AddSlide when !string.IsNullOrEmpty(slideId):
                _controller.AddSlideOccurrence(slideId);
                break;
            case SlideShowCustomShowDialogAction.Close:
                _close();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(action), action, null);
        }
    }
}

/// <summary>Creates and exposes the standard renderer-owned Custom Shows action controls.</summary>
public sealed class SlideShowCustomShowDialogButtonSet<TButton>
    where TButton : class
{
    public SlideShowCustomShowDialogButtonSet(
        Func<SlideShowCustomShowDialogAction, Action, TButton> create,
        SlideShowCustomShowDialogActionDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(create);
        ArgumentNullException.ThrowIfNull(dispatcher);
        Rename = Create(SlideShowCustomShowDialogAction.Rename);
        Update = Create(SlideShowCustomShowDialogAction.UpdateSlides);
        Delete = Create(SlideShowCustomShowDialogAction.Delete);
        Start = Create(SlideShowCustomShowDialogAction.StartShow);
        MoveUp = Create(SlideShowCustomShowDialogAction.MoveUp);
        MoveDown = Create(SlideShowCustomShowDialogAction.MoveDown);
        Remove = Create(SlideShowCustomShowDialogAction.Remove);
        return;

        TButton Create(SlideShowCustomShowDialogAction action) =>
            create(action, () => dispatcher.Execute(action));
    }

    public TButton Rename { get; }

    public TButton Update { get; }

    public TButton Delete { get; }

    public TButton Start { get; }

    public TButton MoveUp { get; }

    public TButton MoveDown { get; }

    public TButton Remove { get; }
}

public sealed record SlideShowCustomShowAvailableSlideNativeRow<TControl, TRow>(
    string SlideId,
    TControl Control,
    TRow Row)
    where TControl : class
    where TRow : class;

/// <summary>Owns available-slide row replacement and form registration.</summary>
public sealed class SlideShowCustomShowAvailableSlideRendererSession<TControl, TRow>
    where TControl : class
    where TRow : class
{
    private readonly SlideShowCustomShowDialogFormSession<TControl> _form;
    private readonly Action _clearRows;
    private readonly Func<SlideShowCustomShowSlideOption, SlideShowCustomShowAvailableSlideNativeRow<TControl, TRow>> _createRow;
    private readonly Action<TRow> _addRow;
    private readonly List<TControl> _controls = [];

    public SlideShowCustomShowAvailableSlideRendererSession(
        SlideShowCustomShowDialogFormSession<TControl> form,
        Action clearRows,
        Func<SlideShowCustomShowSlideOption, SlideShowCustomShowAvailableSlideNativeRow<TControl, TRow>> createRow,
        Action<TRow> addRow)
    {
        _form = form ?? throw new ArgumentNullException(nameof(form));
        _clearRows = clearRows ?? throw new ArgumentNullException(nameof(clearRows));
        _createRow = createRow ?? throw new ArgumentNullException(nameof(createRow));
        _addRow = addRow ?? throw new ArgumentNullException(nameof(addRow));
    }

    public IReadOnlyList<TControl> Controls => _controls;

    public void Render(IReadOnlyList<SlideShowCustomShowSlideOption> slides)
    {
        ArgumentNullException.ThrowIfNull(slides);
        _clearRows();
        _controls.Clear();
        _form.ClearAvailableSlides();
        foreach (var slide in slides)
        {
            var native = _createRow(slide);
            _controls.Add(native.Control);
            _form.RegisterAvailableSlide(native.SlideId, native.Control);
            _addRow(native.Row);
        }
    }
}
