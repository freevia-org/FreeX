namespace FreeP.App.Compositor;

public enum HeaderFooterDialogField
{
    DateTime,
    DateFormat,
    FixedDateTime,
    FixedDateTimeText,
    Footer,
    FooterText,
    SlideNumber,
    SuppressOnTitleSlide,
}

public enum HeaderFooterDialogAction
{
    Apply,
    ApplyToAll,
    Cancel,
}

public static class HeaderFooterDialogSurfaceCatalog
{
    public static PresentationDialogSurfacePlan<HeaderFooterDialogField, HeaderFooterDialogAction> Surface { get; } = new(
        "Header and Footer",
        "Header and Footer",
        "FreeP.HeaderFooter.Dialog",
        [
            Field(HeaderFooterDialogField.DateTime, PresentationDialogControlKind.Toggle,
                "Date and time", "Show date and time"),
            Field(HeaderFooterDialogField.DateFormat, PresentationDialogControlKind.Choice,
                "Date and time format", "Date and time format"),
            Field(HeaderFooterDialogField.FixedDateTime, PresentationDialogControlKind.Toggle,
                "Fixed", "Use fixed date and time"),
            Field(HeaderFooterDialogField.FixedDateTimeText, PresentationDialogControlKind.Text,
                "Fixed date and time", "Fixed date and time text"),
            Field(HeaderFooterDialogField.Footer, PresentationDialogControlKind.Toggle,
                "Footer", "Show footer"),
            Field(HeaderFooterDialogField.FooterText, PresentationDialogControlKind.Text,
                "Footer text", "Footer text"),
            Field(HeaderFooterDialogField.SlideNumber, PresentationDialogControlKind.Toggle,
                "Slide number", "Show slide number"),
            Field(HeaderFooterDialogField.SuppressOnTitleSlide, PresentationDialogControlKind.Toggle,
                "Don't show on title slide", "Don't show header and footer on title slide"),
        ],
        [
            Action(HeaderFooterDialogAction.Apply, "Apply", "Apply to current slide", isDefault: true),
            Action(HeaderFooterDialogAction.ApplyToAll, "Apply to All", "Apply to all slides"),
            Action(HeaderFooterDialogAction.Cancel, "Cancel", "Cancel header and footer changes", isCancel: true),
        ]);

    private static PresentationDialogFieldPlan<HeaderFooterDialogField> Field(
        HeaderFooterDialogField id,
        PresentationDialogControlKind kind,
        string label,
        string accessibleName) =>
        new(id, kind, label, accessibleName, $"FreeP.HeaderFooter.{id}");

    private static PresentationDialogActionPlan<HeaderFooterDialogAction> Action(
        HeaderFooterDialogAction id,
        string label,
        string accessibleName,
        bool isDefault = false,
        bool isCancel = false) =>
        new(id, label, accessibleName, $"FreeP.HeaderFooter.{id}", isDefault, isCancel);
}

public sealed record HeaderFooterDialogInputState(
    bool ShowDateTime,
    bool ShowFooter,
    bool ShowSlideNumber,
    string FooterText,
    bool SuppressOnTitleSlide,
    bool UseFixedDateTime,
    int DateFormatIndex,
    string FixedDateTimeText);

public sealed record HeaderFooterDialogEnabledState(
    bool IsDateFormatEnabled,
    bool IsDateTimeModeEnabled,
    bool IsFixedDateTimeTextEnabled,
    bool IsFooterTextEnabled);

public sealed record HeaderFooterDialogViewState(
    HeaderFooterDialogInputState Input,
    HeaderFooterDialogEnabledState Enabled,
    IReadOnlyList<HeaderFooterDateFormatOption> DateFormatOptions);

public sealed record HeaderFooterDialogFocusPlan(
    HeaderFooterDialogField Field,
    bool SelectAllText = false);

public sealed class HeaderFooterDialogInputProjection
{
    private readonly IReadOnlyDictionary<HeaderFooterDialogField, PresentationDialogFieldValue> _fields;

    public HeaderFooterDialogInputProjection(
        IReadOnlyDictionary<HeaderFooterDialogField, PresentationDialogFieldValue> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        _fields = new Dictionary<HeaderFooterDialogField, PresentationDialogFieldValue>(fields);
    }

    public IReadOnlyDictionary<HeaderFooterDialogField, PresentationDialogFieldValue> Fields => _fields;

    public static HeaderFooterDialogInputProjection FromInput(HeaderFooterDialogInputState input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return new(new Dictionary<HeaderFooterDialogField, PresentationDialogFieldValue>
        {
            [HeaderFooterDialogField.DateTime] = new(IsChecked: input.ShowDateTime),
            [HeaderFooterDialogField.DateFormat] = new(SelectedIndex: input.DateFormatIndex),
            [HeaderFooterDialogField.FixedDateTime] = new(IsChecked: input.UseFixedDateTime),
            [HeaderFooterDialogField.FixedDateTimeText] = new(Text: input.FixedDateTimeText ?? string.Empty),
            [HeaderFooterDialogField.Footer] = new(IsChecked: input.ShowFooter),
            [HeaderFooterDialogField.FooterText] = new(Text: input.FooterText ?? string.Empty),
            [HeaderFooterDialogField.SlideNumber] = new(IsChecked: input.ShowSlideNumber),
            [HeaderFooterDialogField.SuppressOnTitleSlide] = new(IsChecked: input.SuppressOnTitleSlide),
        });
    }

    public HeaderFooterDialogInputState ToInput() => new(
        IsChecked(HeaderFooterDialogField.DateTime),
        IsChecked(HeaderFooterDialogField.Footer),
        IsChecked(HeaderFooterDialogField.SlideNumber),
        Text(HeaderFooterDialogField.FooterText),
        IsChecked(HeaderFooterDialogField.SuppressOnTitleSlide),
        IsChecked(HeaderFooterDialogField.FixedDateTime),
        SelectedIndex(HeaderFooterDialogField.DateFormat),
        Text(HeaderFooterDialogField.FixedDateTimeText));

    private PresentationDialogFieldValue Value(HeaderFooterDialogField field) =>
        _fields.TryGetValue(field, out var value)
            ? value
            : throw new KeyNotFoundException($"The header/footer projection does not contain {field}.");

    private bool IsChecked(HeaderFooterDialogField field) => Value(field).IsChecked == true;

    private int SelectedIndex(HeaderFooterDialogField field) => Value(field).SelectedIndex;

    private string Text(HeaderFooterDialogField field) => Value(field).Text ?? string.Empty;
}

/// <summary>
/// Owns renderer-neutral header/footer form projection while native hosts retain control access.
/// </summary>
public sealed class HeaderFooterDialogFormSession<TControl>
    where TControl : class
{
    private readonly Dictionary<HeaderFooterDialogField, TControl> _controls = [];
    private readonly Func<TControl, PresentationDialogFieldValue> _captureValue;
    private readonly Action<TControl, PresentationDialogFieldValue> _applyValue;
    private readonly Action<TControl, bool> _setEnabled;
    private readonly Action<TControl> _focus;
    private readonly Action<TControl> _selectAllText;

    public HeaderFooterDialogFormSession(
        Func<TControl, PresentationDialogFieldValue> captureValue,
        Action<TControl, PresentationDialogFieldValue> applyValue,
        Action<TControl, bool> setEnabled,
        Action<TControl> focus,
        Action<TControl> selectAllText)
    {
        _captureValue = captureValue ?? throw new ArgumentNullException(nameof(captureValue));
        _applyValue = applyValue ?? throw new ArgumentNullException(nameof(applyValue));
        _setEnabled = setEnabled ?? throw new ArgumentNullException(nameof(setEnabled));
        _focus = focus ?? throw new ArgumentNullException(nameof(focus));
        _selectAllText = selectAllText ?? throw new ArgumentNullException(nameof(selectAllText));
    }

    public bool IsApplyingState { get; private set; }

    public void Register(HeaderFooterDialogField field, TControl control)
    {
        ArgumentNullException.ThrowIfNull(control);
        _controls.Add(field, control);
    }

    public HeaderFooterDialogInputState CaptureInput() =>
        new HeaderFooterDialogInputProjection(
            _controls.ToDictionary(pair => pair.Key, pair => _captureValue(pair.Value)))
        .ToInput();

    public void ApplyState(HeaderFooterDialogViewState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        IsApplyingState = true;
        try
        {
            foreach (var (field, value) in HeaderFooterDialogInputProjection.FromInput(state.Input).Fields)
                _applyValue(Control(field), value);

            ApplyEnabledState(state.Enabled);
        }
        finally
        {
            IsApplyingState = false;
        }
    }

    public void ApplyEnabledState(HeaderFooterDialogEnabledState enabled)
    {
        ArgumentNullException.ThrowIfNull(enabled);
        _setEnabled(Control(HeaderFooterDialogField.DateFormat), enabled.IsDateFormatEnabled);
        _setEnabled(Control(HeaderFooterDialogField.FixedDateTime), enabled.IsDateTimeModeEnabled);
        _setEnabled(Control(HeaderFooterDialogField.FixedDateTimeText), enabled.IsFixedDateTimeTextEnabled);
        _setEnabled(Control(HeaderFooterDialogField.FooterText), enabled.IsFooterTextEnabled);
    }

    public void Focus(HeaderFooterDialogFocusPlan? plan)
    {
        if (plan is null || !_controls.TryGetValue(plan.Field, out var control))
            return;

        _focus(control);
        if (plan.SelectAllText)
            _selectAllText(control);
    }

    private TControl Control(HeaderFooterDialogField field) =>
        _controls.TryGetValue(field, out var control)
            ? control
            : throw new KeyNotFoundException($"No native control is registered for {field}.");
}

public sealed class HeaderFooterDialogSession
{
    private readonly EditingSession _editor;

    public HeaderFooterDialogSession(
        EditingSession editor,
        HeaderFooterCommandFocus requestedFocus)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        RequestedFocus = requestedFocus;
        InitialState = HeaderFooterCommandPlanner.BuildState(editor);
        InitialInput = FromOptions(
            HeaderFooterCommandPlanner.BuildDefaultOptions(InitialState, requestedFocus));
        State = BuildViewState(InitialInput);
    }

    public HeaderFooterState InitialState { get; }

    public HeaderFooterDialogInputState InitialInput { get; }

    public HeaderFooterDialogViewState State { get; private set; }

    public PresentationDialogSurfacePlan<HeaderFooterDialogField, HeaderFooterDialogAction> Surface =>
        HeaderFooterDialogSurfaceCatalog.Surface;

    public HeaderFooterCommandFocus RequestedFocus { get; }

    public HeaderFooterDialogFocusPlan? RequestedFocusPlan => RequestedFocus switch
    {
        HeaderFooterCommandFocus.DateTime => new(HeaderFooterDialogField.DateTime),
        HeaderFooterCommandFocus.Footer => new(HeaderFooterDialogField.FooterText, SelectAllText: true),
        HeaderFooterCommandFocus.SlideNumber => new(HeaderFooterDialogField.SlideNumber),
        _ => null,
    };

    public HeaderFooterDialogField? RequestedFocusField => RequestedFocusPlan?.Field;

    public HeaderFooterApplyPlan? LastApplyPlan { get; private set; }

    public static IReadOnlyList<HeaderFooterDateFormatOption> DateFormatOptions
        => HeaderFooterCommandPlanner.DateFormatOptions;

    public static HeaderFooterDialogInputState CreateInput(
        bool showDateTime,
        bool showFooter,
        bool showSlideNumber,
        string? footerText,
        bool suppressOnTitleSlide,
        bool useFixedDateTime,
        int dateFormatIndex,
        string? fixedDateTimeText) =>
        new(
            showDateTime,
            showFooter,
            showSlideNumber,
            footerText ?? string.Empty,
            suppressOnTitleSlide,
            useFixedDateTime,
            NormalizeDateFormatIndex(dateFormatIndex),
            fixedDateTimeText ?? string.Empty);

    public static HeaderFooterDialogInputState CreateInput(
        bool showDateTime,
        bool showFooter,
        bool showSlideNumber,
        string? footerText,
        bool suppressOnTitleSlide,
        HeaderFooterDateTimeMode dateTimeMode,
        string? dateTimeFieldType,
        string? fixedDateTimeText) =>
        CreateInput(
            showDateTime,
            showFooter,
            showSlideNumber,
            footerText,
            suppressOnTitleSlide,
            dateTimeMode == HeaderFooterDateTimeMode.Fixed,
            DateFormatIndex(dateTimeFieldType),
            fixedDateTimeText);

    public static HeaderFooterDialogEnabledState BuildEnabledState(
        HeaderFooterDialogInputState input) =>
        new(
            IsDateFormatEnabled: input.ShowDateTime && !input.UseFixedDateTime,
            IsDateTimeModeEnabled: input.ShowDateTime,
            IsFixedDateTimeTextEnabled: input.ShowDateTime && input.UseFixedDateTime,
            IsFooterTextEnabled: input.ShowFooter);

    public static int DateFormatIndex(string? fieldType)
    {
        for (var index = 0; index < DateFormatOptions.Count; index++)
        {
            if (StringComparer.OrdinalIgnoreCase.Equals(
                    DateFormatOptions[index].FieldType,
                    fieldType?.Trim()))
            {
                return index;
            }
        }

        return 0;
    }

    public static HeaderFooterDateFormatOption DateFormatOption(int selectedIndex)
        => DateFormatOptions[NormalizeDateFormatIndex(selectedIndex)];

    public HeaderFooterDialogViewState SetInput(HeaderFooterDialogInputState input)
    {
        ArgumentNullException.ThrowIfNull(input);
        State = BuildViewState(CreateInput(
            input.ShowDateTime,
            input.ShowFooter,
            input.ShowSlideNumber,
            input.FooterText,
            input.SuppressOnTitleSlide,
            input.UseFixedDateTime,
            input.DateFormatIndex,
            input.FixedDateTimeText));
        return State;
    }

    public HeaderFooterDialogViewState SetInput(
        bool showDateTime,
        bool showFooter,
        bool showSlideNumber,
        string? footerText,
        bool suppressOnTitleSlide,
        HeaderFooterDateTimeMode dateTimeMode,
        string? dateTimeFieldType,
        string? fixedDateTimeText) =>
        SetInput(CreateInput(
            showDateTime,
            showFooter,
            showSlideNumber,
            footerText,
            suppressOnTitleSlide,
            dateTimeMode,
            dateTimeFieldType,
            fixedDateTimeText));

    public HeaderFooterApplyOptions BuildApplyOptions(
        HeaderFooterDialogInputState input,
        HeaderFooterApplyScope scope)
    {
        var dateFormat = DateFormatOption(input.DateFormatIndex);
        return new(
            input.ShowDateTime,
            input.ShowFooter,
            input.ShowSlideNumber,
            input.FooterText ?? string.Empty,
            scope,
            input.SuppressOnTitleSlide,
            input.UseFixedDateTime
                ? HeaderFooterDateTimeMode.Fixed
                : HeaderFooterDateTimeMode.AutoUpdate,
            dateFormat.FieldType,
            input.FixedDateTimeText ?? string.Empty);
    }

    public HeaderFooterApplyPlan BuildCommitPlan(HeaderFooterApplyScope scope)
        => HeaderFooterCommandPlanner.BuildApplyPlan(
            _editor.Presentation,
            _editor.CurrentSlideIndex,
            BuildApplyOptions(State.Input, scope));

    public bool TryCommit(HeaderFooterApplyScope scope)
    {
        var plan = BuildCommitPlan(scope);
        if (!HeaderFooterCommandPlanner.TryApply(_editor, plan))
        {
            return false;
        }

        LastApplyPlan = plan;
        return true;
    }

    private static HeaderFooterDialogInputState FromOptions(
        HeaderFooterApplyOptions options) =>
        CreateInput(
            options.ShowDateTime,
            options.ShowFooter,
            options.ShowSlideNumber,
            options.FooterText,
            options.SuppressOnTitleSlide,
            options.DateTimeMode,
            options.DateTimeFieldType,
            options.FixedDateTimeText);

    private static HeaderFooterDialogViewState BuildViewState(
        HeaderFooterDialogInputState input) =>
        new(
            input,
            BuildEnabledState(input),
            DateFormatOptions);

    private static int NormalizeDateFormatIndex(int index)
        => index >= 0 && index < DateFormatOptions.Count ? index : 0;
}
