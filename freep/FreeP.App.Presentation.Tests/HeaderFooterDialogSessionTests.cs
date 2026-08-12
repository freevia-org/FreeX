using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class HeaderFooterDialogSessionTests
{
    [Fact]
    public void Constructor_ProjectsFocusedDefaultsAndDateFormatSelection()
    {
        var editor = MakeEditor();
        editor.Presentation.Slides[0].HfVisibility = new HfFlags
        {
            ShowDate = false,
            ShowFooter = false,
            ShowSlideNum = true,
        };

        var session = new HeaderFooterDialogSession(
            editor,
            HeaderFooterCommandFocus.DateTime);

        session.RequestedFocus.Should().Be(HeaderFooterCommandFocus.DateTime);
        session.InitialState.ShowDateTime.Should().BeFalse();
        session.InitialInput.ShowDateTime.Should().BeTrue();
        session.InitialInput.ShowSlideNumber.Should().BeTrue();
        session.InitialInput.DateFormatIndex.Should().Be(0);
    }

    [Fact]
    public void CreateInput_NormalizesNullableTextAndUnknownDateFormat()
    {
        var input = HeaderFooterDialogSession.CreateInput(
            showDateTime: true,
            showFooter: true,
            showSlideNumber: false,
            footerText: null,
            suppressOnTitleSlide: false,
            dateTimeMode: HeaderFooterDateTimeMode.Fixed,
            dateTimeFieldType: "unknown",
            fixedDateTimeText: null);

        input.FooterText.Should().BeEmpty();
        input.FixedDateTimeText.Should().BeEmpty();
        input.UseFixedDateTime.Should().BeTrue();
        input.DateFormatIndex.Should().Be(0);
        HeaderFooterDialogSession.DateFormatOption(-1).FieldType.Should().Be("datetime1");
        HeaderFooterDialogSession.DateFormatIndex(" DATETIME3 ").Should().Be(2);
    }

    [Theory]
    [InlineData(false, false, false, false, false, false)]
    [InlineData(true, false, false, true, true, false)]
    [InlineData(true, true, true, false, true, true)]
    public void BuildEnabledState_ProjectsControlAvailability(
        bool showDateTime,
        bool useFixedDateTime,
        bool showFooter,
        bool dateFormatEnabled,
        bool dateTimeModeEnabled,
        bool textFieldsEnabled)
    {
        var input = HeaderFooterDialogSession.CreateInput(
            showDateTime,
            showFooter,
            showSlideNumber: false,
            footerText: string.Empty,
            suppressOnTitleSlide: false,
            useFixedDateTime,
            dateFormatIndex: 0,
            fixedDateTimeText: string.Empty);

        var enabled = HeaderFooterDialogSession.BuildEnabledState(input);

        enabled.IsDateFormatEnabled.Should().Be(dateFormatEnabled);
        enabled.IsDateTimeModeEnabled.Should().Be(dateTimeModeEnabled);
        enabled.IsFixedDateTimeTextEnabled.Should().Be(showDateTime && useFixedDateTime);
        enabled.IsFooterTextEnabled.Should().Be(showFooter);
        (enabled.IsFixedDateTimeTextEnabled || enabled.IsFooterTextEnabled)
            .Should().Be(textFieldsEnabled);
    }

    [Fact]
    public void TryApply_CreatesNormalizedPortableResultAndUpdatesPresentation()
    {
        var editor = MakeEditor();
        var session = new HeaderFooterDialogSession(
            editor,
            HeaderFooterCommandFocus.HeaderFooter);
        var input = HeaderFooterDialogSession.CreateInput(
            showDateTime: true,
            showFooter: true,
            showSlideNumber: true,
            footerText: "Layout footer",
            suppressOnTitleSlide: false,
            useFixedDateTime: false,
            dateFormatIndex: 2,
            fixedDateTimeText: string.Empty);

        var state = session.SetInput(input);
        var plan = session.BuildCommitPlan(HeaderFooterApplyScope.CurrentSlide);

        state.Input.Should().Be(input);
        plan.Options.DateTimeFieldType.Should().Be("datetime3");
        (editor.Presentation.Slides[0].HfVisibility?.ShowFooter ?? false).Should().BeFalse();
        session.TryCommit(HeaderFooterApplyScope.CurrentSlide).Should().BeTrue();

        session.LastApplyPlan.Should().NotBeNull();
        session.LastApplyPlan!.Options.DateTimeFieldType.Should().Be("datetime3");
        session.LastApplyPlan.Options.FooterText.Should().Be("Layout footer");
        editor.Presentation.Slides[0].HfVisibility!.ShowFooter.Should().BeTrue();
    }

    [Fact]
    public void SetInput_NormalizesUnknownOptionsAndOwnsEnabledStateTransitions()
    {
        var session = new HeaderFooterDialogSession(
            MakeEditor(),
            HeaderFooterCommandFocus.HeaderFooter);
        var input = new HeaderFooterDialogInputState(
            ShowDateTime: true,
            ShowFooter: true,
            ShowSlideNumber: false,
            FooterText: "Imported footer",
            SuppressOnTitleSlide: false,
            UseFixedDateTime: true,
            DateFormatIndex: 99,
            FixedDateTimeText: "Imported fixed date");

        var state = session.SetInput(input);

        state.Input.DateFormatIndex.Should().Be(0);
        state.Input.FooterText.Should().Be("Imported footer");
        state.Enabled.IsDateFormatEnabled.Should().BeFalse();
        state.Enabled.IsDateTimeModeEnabled.Should().BeTrue();
        state.Enabled.IsFixedDateTimeTextEnabled.Should().BeTrue();
        state.Enabled.IsFooterTextEnabled.Should().BeTrue();
        state.DateFormatOptions.Should().BeSameAs(HeaderFooterDialogSession.DateFormatOptions);
    }

    [Fact]
    public void InputProjection_RoundTripsEveryFlagTokenAndTextField()
    {
        var input = new HeaderFooterDialogInputState(
            ShowDateTime: true,
            ShowFooter: false,
            ShowSlideNumber: true,
            FooterText: "Quarterly footer",
            SuppressOnTitleSlide: true,
            UseFixedDateTime: true,
            DateFormatIndex: 3,
            FixedDateTimeText: "10 August 2026");

        var projection = HeaderFooterDialogInputProjection.FromInput(input);

        projection.Fields.Should().HaveCount(8);
        projection.Fields[HeaderFooterDialogField.DateTime].IsChecked.Should().BeTrue();
        projection.Fields[HeaderFooterDialogField.DateFormat].SelectedIndex.Should().Be(3);
        projection.Fields[HeaderFooterDialogField.FixedDateTimeText].Text.Should().Be("10 August 2026");
        projection.Fields[HeaderFooterDialogField.FooterText].Text.Should().Be("Quarterly footer");
        projection.Fields[HeaderFooterDialogField.SuppressOnTitleSlide].IsChecked.Should().BeTrue();
        projection.ToInput().Should().Be(input);
    }

    [Fact]
    public void FormSession_CapturesAppliesEnabledStateAndFocusedTextSelection()
    {
        var controls = Enum.GetValues<HeaderFooterDialogField>()
            .ToDictionary(field => field, _ => new FakeControl());
        var form = new HeaderFooterDialogFormSession<FakeControl>(
            control => control.Value,
            (control, value) => control.Value = value,
            (control, enabled) => control.IsEnabled = enabled,
            control => control.IsFocused = true,
            control => control.IsTextSelected = true);
        foreach (var (field, control) in controls)
            form.Register(field, control);

        controls[HeaderFooterDialogField.DateTime].Value = new(IsChecked: true);
        controls[HeaderFooterDialogField.DateFormat].Value = new(SelectedIndex: 2);
        controls[HeaderFooterDialogField.FixedDateTime].Value = new(IsChecked: false);
        controls[HeaderFooterDialogField.FixedDateTimeText].Value = new(Text: "Fixed");
        controls[HeaderFooterDialogField.Footer].Value = new(IsChecked: true);
        controls[HeaderFooterDialogField.FooterText].Value = new(Text: "Footer");
        controls[HeaderFooterDialogField.SlideNumber].Value = new(IsChecked: true);
        controls[HeaderFooterDialogField.SuppressOnTitleSlide].Value = new(IsChecked: true);

        var captured = form.CaptureInput();
        var state = new HeaderFooterDialogViewState(
            captured with
            {
                ShowDateTime = false,
                UseFixedDateTime = true,
                FooterText = "Applied footer",
            },
            new HeaderFooterDialogEnabledState(
                IsDateFormatEnabled: false,
                IsDateTimeModeEnabled: false,
                IsFixedDateTimeTextEnabled: false,
                IsFooterTextEnabled: true),
            HeaderFooterDialogSession.DateFormatOptions);

        form.ApplyState(state);
        form.Focus(new(HeaderFooterDialogField.FooterText, SelectAllText: true));

        captured.ShowDateTime.Should().BeTrue();
        captured.ShowFooter.Should().BeTrue();
        captured.ShowSlideNumber.Should().BeTrue();
        captured.FooterText.Should().Be("Footer");
        captured.SuppressOnTitleSlide.Should().BeTrue();
        captured.DateFormatIndex.Should().Be(2);
        controls[HeaderFooterDialogField.DateTime].Value.IsChecked.Should().BeFalse();
        controls[HeaderFooterDialogField.FixedDateTime].Value.IsChecked.Should().BeTrue();
        controls[HeaderFooterDialogField.FooterText].Value.Text.Should().Be("Applied footer");
        controls[HeaderFooterDialogField.DateFormat].IsEnabled.Should().BeFalse();
        controls[HeaderFooterDialogField.FixedDateTime].IsEnabled.Should().BeFalse();
        controls[HeaderFooterDialogField.FixedDateTimeText].IsEnabled.Should().BeFalse();
        controls[HeaderFooterDialogField.FooterText].IsEnabled.Should().BeTrue();
        controls[HeaderFooterDialogField.FooterText].IsFocused.Should().BeTrue();
        controls[HeaderFooterDialogField.FooterText].IsTextSelected.Should().BeTrue();
        form.IsApplyingState.Should().BeFalse();
    }

    [Theory]
    [InlineData(HeaderFooterCommandFocus.DateTime, HeaderFooterDialogField.DateTime, false)]
    [InlineData(HeaderFooterCommandFocus.Footer, HeaderFooterDialogField.FooterText, true)]
    [InlineData(HeaderFooterCommandFocus.SlideNumber, HeaderFooterDialogField.SlideNumber, false)]
    public void RequestedFocusPlan_PreservesFieldAndTextSelectionBehavior(
        HeaderFooterCommandFocus focus,
        HeaderFooterDialogField expectedField,
        bool selectAllText)
    {
        var session = new HeaderFooterDialogSession(MakeEditor(), focus);

        session.RequestedFocusPlan.Should().Be(new HeaderFooterDialogFocusPlan(expectedField, selectAllText));
        session.RequestedFocusField.Should().Be(expectedField);
    }

    private static EditingSession MakeEditor()
    {
        var presentation = Presentation.CreateEmpty();
        return new EditingSession(
            presentation,
            new PresentationCommandBus(presentation));
    }

    private sealed class FakeControl
    {
        public PresentationDialogFieldValue Value { get; set; } = new();
        public bool IsEnabled { get; set; } = true;
        public bool IsFocused { get; set; }
        public bool IsTextSelected { get; set; }
    }
}
