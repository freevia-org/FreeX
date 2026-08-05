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

        session.TryApply(input, HeaderFooterApplyScope.CurrentSlide).Should().BeTrue();

        session.LastApplyPlan.Should().NotBeNull();
        session.LastApplyPlan!.Options.DateTimeFieldType.Should().Be("datetime3");
        session.LastApplyPlan.Options.FooterText.Should().Be("Layout footer");
        editor.Presentation.Slides[0].HfVisibility!.ShowFooter.Should().BeTrue();
    }

    private static EditingSession MakeEditor()
    {
        var presentation = Presentation.CreateEmpty();
        return new EditingSession(
            presentation,
            new PresentationCommandBus(presentation));
    }
}
