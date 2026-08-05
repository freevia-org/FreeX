namespace FreeP.App.Compositor;

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
    }

    public HeaderFooterState InitialState { get; }

    public HeaderFooterDialogInputState InitialInput { get; }

    public HeaderFooterCommandFocus RequestedFocus { get; }

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

    public bool TryApply(
        HeaderFooterDialogInputState input,
        HeaderFooterApplyScope scope)
    {
        if (!HeaderFooterCommandPlanner.TryApply(
                _editor,
                BuildApplyOptions(input, scope),
                out var plan))
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

    private static int NormalizeDateFormatIndex(int index)
        => index >= 0 && index < DateFormatOptions.Count ? index : 0;
}
