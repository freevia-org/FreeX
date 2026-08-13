using System.Globalization;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Presentation.Tests;

public sealed class ParagraphNoteDialogPlannerTests
{
    [Fact]
    public void FootnoteEndnoteOptions_ExposeOneCrossHostLayoutPolicy()
    {
        var surface = FootnoteEndnoteOptionsDialogPlanner.Surface;

        FootnoteEndnoteOptionsDialogPlanner.DialogWidth.Should().Be(380);
        FootnoteEndnoteOptionsDialogPlanner.OuterMargin.Should().Be(14);
        FootnoteEndnoteOptionsDialogPlanner.FieldVerticalMargin.Should().Be(4);
        FootnoteEndnoteOptionsDialogPlanner.LabelFieldGap.Should().Be(8);
        FootnoteEndnoteOptionsDialogPlanner.ButtonWidth.Should().Be(72);
        FootnoteEndnoteOptionsDialogPlanner.StartAtMinWidth.Should().Be(60);
        surface.Sections.Select(section => section.Kind)
            .Should().Equal(FootnoteEndnoteNoteKind.Footnote, FootnoteEndnoteNoteKind.Endnote);
        surface.Sections.Should().OnlyContain(section =>
            section.Fields.Select(field => field.Kind).SequenceEqual(Enum.GetValues<FootnoteEndnoteFieldKind>()));
        surface.Sections.SelectMany(section => section.Fields).Select(field => field.AutomationId)
            .Should().OnlyHaveUniqueItems();
        surface.Section(FootnoteEndnoteNoteKind.Endnote).StartAtValidationField
            .Should().Be(FootnoteEndnoteOptionsDialogField.EndnoteStartAt);
    }

    [Fact]
    public void FootnoteEndnoteOptions_ExposeMirroredCatalogsAndInitialState()
    {
        FootnoteEndnoteOptionsDialogPlanner.FormatItems.Select(item => item.Label)
            .Should().Equal(
                "1, 2, 3, \u2026",
                "i, ii, iii, \u2026",
                "I, II, III, \u2026",
                "a, b, c, \u2026",
                "A, B, C, \u2026",
                "*, \u2020, \u2021, \u2026");
        FootnoteEndnoteOptionsDialogPlanner.FootnoteRestartItems.Select(item => item.Value)
            .Should().Equal(NoteNumberRestart.Continuous, NoteNumberRestart.EachSection, NoteNumberRestart.EachPage);
        FootnoteEndnoteOptionsDialogPlanner.EndnoteRestartItems.Select(item => item.Value)
            .Should().Equal(NoteNumberRestart.Continuous, NoteNumberRestart.EachSection);

        var state = FootnoteEndnoteOptionsDialogPlanner.BuildInitialState(
            new NoteNumberingOptions
            {
                NumberFormat = NoteNumberFormat.LowerRoman,
                StartAt = 4,
                NumberRestart = NoteNumberRestart.EachPage
            },
            new NoteNumberingOptions
            {
                NumberFormat = NoteNumberFormat.UpperLetter,
                StartAt = 12,
                NumberRestart = NoteNumberRestart.EachSection
            },
            CultureInfo.InvariantCulture);

        state.FootnoteFormatIndex.Should().Be(1);
        state.FootnoteStartAtText.Should().Be("4");
        state.FootnoteRestartIndex.Should().Be(2);
        state.EndnoteFormatIndex.Should().Be(4);
        state.EndnoteStartAtText.Should().Be("12");
        state.EndnoteRestartIndex.Should().Be(1);
        state.FormatIndex(FootnoteEndnoteNoteKind.Footnote).Should().Be(1);
        state.StartAtText(FootnoteEndnoteNoteKind.Endnote).Should().Be("12");
        state.RestartIndex(FootnoteEndnoteNoteKind.Endnote).Should().Be(1);
    }

    [Fact]
    public void FootnoteEndnoteOptions_BuildsResultAndIdentifiesInvalidStartField()
    {
        var input = new FootnoteEndnoteOptionsDialogInput(
            FootnoteFormatIndex: 2,
            FootnoteStartAtText: "7",
            FootnoteRestartIndex: 1,
            EndnoteFormatIndex: 3,
            EndnoteStartAtText: "9",
            EndnoteRestartIndex: 1);

        FootnoteEndnoteOptionsDialogPlanner.TryBuildResult(
                input,
                CultureInfo.InvariantCulture,
                out var result,
                out var validation)
            .Should().BeTrue();

        validation.Should().BeNull();
        result.Should().Be(new FootnoteEndnoteOptionsDialogResult(
            NoteNumberFormat.UpperRoman,
            7,
            NoteNumberRestart.EachSection,
            NoteNumberFormat.LowerLetter,
            9,
            NoteNumberRestart.EachSection));

        FootnoteEndnoteOptionsDialogPlanner.TryBuildResult(
                input with { EndnoteStartAtText = "0" },
                CultureInfo.InvariantCulture,
                out _,
                out validation)
            .Should().BeFalse();

        validation.Should().Be(new FootnoteEndnoteOptionsValidation(
            FootnoteEndnoteOptionsDialogField.EndnoteStartAt,
            FootnoteEndnoteOptionsDialogPlanner.PositiveStartAtMessage));
    }

    [Fact]
    public void ParagraphIndent_FormatsSignedFirstLineAndBuildsSignedResult()
    {
        var state = ParagraphIndentDialogPlanner.BuildInitialState(
            leftPt: 18,
            rightPt: 6.5,
            firstLinePt: -12.25,
            CultureInfo.InvariantCulture);

        state.LeftText.Should().Be("18");
        state.RightText.Should().Be("6.5");
        state.SpecialIndex.Should().Be((int)ParagraphIndentSpecialKind.Hanging);
        state.SpecialAmountText.Should().Be("12.25");
        state.SpecialAmountEnabled.Should().BeTrue();

        ParagraphIndentDialogPlanner.TryBuildResult(
                new ParagraphIndentDialogInput("20", "10", (int)ParagraphIndentSpecialKind.FirstLine, "5"),
                CultureInfo.InvariantCulture,
                out var result,
                out var validation)
            .Should().BeTrue();

        validation.Should().BeNull();
        result.Should().Be(new ParagraphIndentDialogResult(20, 10, 5));

        ParagraphIndentDialogPlanner.TryBuildResult(
                new ParagraphIndentDialogInput("20", "-1", (int)ParagraphIndentSpecialKind.Hanging, "5"),
                CultureInfo.InvariantCulture,
                out _,
                out validation)
            .Should().BeFalse();

        validation.Should().Be(new ParagraphIndentValidation(
            ParagraphIndentDialogField.Right,
            ParagraphIndentDialogPlanner.ValidationMessage));
    }

    [Fact]
    public void CustomParagraphSpacing_DefaultsFormatsValidatesAndConstructsSpacingSet()
    {
        var state = CustomParagraphSpacingDialogPlanner.BuildInitialState(null, CultureInfo.InvariantCulture);

        state.SpaceBeforeText.Should().Be("0");
        state.SpaceAfterText.Should().Be("6");
        state.LineSpacingText.Should().Be("1.15");

        CustomParagraphSpacingDialogPlanner.TryBuildResult(
                new CustomParagraphSpacingDialogInput("2.5", "8", "1.5"),
                CultureInfo.InvariantCulture,
                out var result,
                out var validation)
            .Should().BeTrue();

        validation.Should().BeNull();
        result.Should().Be(new DocumentParagraphSpacingSet("Custom", 2.5, 8, 1.5));

        CustomParagraphSpacingDialogPlanner.TryBuildResult(
                new CustomParagraphSpacingDialogInput("2.5", "201", "1.5"),
                CultureInfo.InvariantCulture,
                out _,
                out validation)
            .Should().BeFalse();

        validation.Should().Be(new CustomParagraphSpacingValidation(
            CustomParagraphSpacingDialogField.SpaceAfter,
            CustomParagraphSpacingDialogPlanner.SpaceAfterValidationMessage));
    }

    [Fact]
    public void ParagraphBreaks_ProjectsFormattingAndConstructsFullResult()
    {
        var current = ParagraphFormatting.Default with
        {
            IndentLeftPt = 12,
            IndentRightPt = 24,
            FirstLineIndentPt = -18,
            SpaceBeforePt = 3,
            SpaceAfterPt = 9,
            LineSpacing = 1.5,
            KeepWithNext = true,
            KeepLinesTogether = true,
            WidowControl = true,
            PageBreakBefore = true,
            SuppressAutoHyphens = true,
            SuppressLineNumbers = true,
            ContextualSpacing = true
        };

        var state = ParagraphBreaksDialogPlanner.BuildInitialState(current, CultureInfo.InvariantCulture);

        state.LeftText.Should().Be("12");
        state.RightText.Should().Be("24");
        state.SpecialIndex.Should().Be((int)ParagraphIndentSpecialKind.Hanging);
        state.SpecialAmountText.Should().Be("18");
        state.SpaceBeforeText.Should().Be("3");
        state.SpaceAfterText.Should().Be("9");
        state.LineSpacingText.Should().Be("1.5");
        state.KeepWithNext.Should().BeTrue();
        state.KeepLinesTogether.Should().BeTrue();
        state.WidowControl.Should().BeTrue();
        state.PageBreakBefore.Should().BeTrue();
        state.SuppressAutoHyphens.Should().BeTrue();
        state.SuppressLineNumbers.Should().BeTrue();
        state.ContextualSpacing.Should().BeTrue();

        ParagraphBreaksDialogPlanner.TryBuildResult(
                new ParagraphBreaksDialogInput(
                    LeftText: "1",
                    RightText: "2",
                    SpecialIndex: (int)ParagraphIndentSpecialKind.Hanging,
                    SpecialAmountText: "3",
                    SpaceBeforeText: "4",
                    SpaceAfterText: "5",
                    LineSpacingText: "1.15",
                    KeepWithNext: true,
                    KeepLinesTogether: false,
                    WidowControl: true,
                    PageBreakBefore: false,
                    SuppressAutoHyphens: true,
                    SuppressLineNumbers: true,
                    ContextualSpacing: true),
                CultureInfo.InvariantCulture,
                out var result,
                out var validation)
            .Should().BeTrue();

        validation.Should().BeNull();
        result.Should().Be(new ParagraphBreaksDialogResult(
            LeftPt: 1,
            RightPt: 2,
            FirstLinePt: -3,
            SpaceBeforePt: 4,
            SpaceAfterPt: 5,
            LineSpacing: 1.15,
            KeepWithNext: true,
            KeepLinesTogether: false,
            WidowControl: true,
            PageBreakBefore: false,
            SuppressAutoHyphens: true,
            SuppressLineNumbers: true,
            ContextualSpacing: true));
    }

    [Fact]
    public void ParagraphBreaks_RejectsFirstInvalidNumericFieldWithSharedMessage()
    {
        ParagraphBreaksDialogPlanner.TryBuildResult(
                new ParagraphBreaksDialogInput(
                    LeftText: "1",
                    RightText: "2",
                    SpecialIndex: (int)ParagraphIndentSpecialKind.None,
                    SpecialAmountText: "0",
                    SpaceBeforeText: "4",
                    SpaceAfterText: "5",
                    LineSpacingText: "0",
                    KeepWithNext: false,
                    KeepLinesTogether: false,
                    WidowControl: false,
                    PageBreakBefore: false,
                    SuppressAutoHyphens: false,
                    SuppressLineNumbers: false,
                    ContextualSpacing: false),
                CultureInfo.InvariantCulture,
                out var result,
                out var validation)
            .Should().BeFalse();

        result.Should().BeNull();
        validation.Should().Be(new ParagraphBreaksValidation(
            ParagraphBreaksDialogField.LineSpacing,
            ParagraphBreaksDialogPlanner.ValidationMessage));
    }
}
