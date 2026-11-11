using Free.Shared.AppServices;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.Dialogs;
using System.Linq;
using FreeW.Core.Model;

namespace FreeW.App.Host.Tests;

/// <summary>
/// End-to-end coverage of the Word-parity Find and Replace features through the real WPF dialog and
/// editing surface: special (caret) codes, find/replace formatting, the extra match options, and
/// search/replace across every story.
/// </summary>
public sealed class FindReplaceWordParityHostTests
{
    private static readonly RunFormatting Bold = RunFormatting.Default with { Bold = true };

    private static DocumentView BuildView(params Paragraph[] paragraphs) => BuildView(null, paragraphs);

    private static DocumentView BuildView(Action<TextDocument>? configure, params Paragraph[] paragraphs)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        foreach (var paragraph in paragraphs)
            document.Blocks.Add(paragraph);
        configure?.Invoke(document);

        var view = new DocumentView();
        view.LoadModel(document);
        return view;
    }

    private static Paragraph Runs(params Run[] runs)
    {
        var paragraph = new Paragraph();
        paragraph.Runs.AddRange(runs);
        return paragraph;
    }

    private static List<string> BodyText(DocumentView view)
    {
        view.CommitToModel();
        return view.Model.Blocks.OfType<Paragraph>().Select(paragraph => paragraph.PlainText).ToList();
    }

    private static string WithDialog(
        DocumentView view,
        string find,
        string replace,
        Action<FindReplaceDialog> act,
        Action<FindReplaceDialog>? configure = null)
    {
        var dialog = new FindReplaceDialog(null!, view, FindReplaceOpenMode.Replace);
        try
        {
            dialog.Show();
            dialog.SetFindTextForTest(find);
            dialog.SetReplaceTextForTest(replace);
            configure?.Invoke(dialog);
            act(dialog);
            return dialog.StatusForTest;
        }
        finally
        {
            dialog.Close();
        }
    }

    private static string ReplaceAll(
        DocumentView view,
        string find,
        string replace,
        Action<FindReplaceDialog>? configure = null) =>
        WithDialog(view, find, replace, dialog => dialog.ReplaceAllForTest(), configure);

    // ---- Special codes -----------------------------------------------------------------------

    [StaFact]
    public void ReplaceAll_TabCode_FindsARenderedTabStop()
    {
        var view = BuildView(new Paragraph("one\ttwo\tthree"));

        var status = ReplaceAll(view, "^t", " - ");

        BodyText(view).Should().Equal("one - two - three");
        status.Should().Contain("2");
    }

    [StaFact]
    public void ReplaceAll_ParagraphMarkCode_JoinsParagraphs()
    {
        var view = BuildView(new Paragraph("alpha"), new Paragraph("beta"), new Paragraph("gamma"));

        ReplaceAll(view, "^p", " / ");

        BodyText(view).Should().Equal("alpha / beta / gamma");
    }

    [StaFact]
    public void ReplaceAll_DoubleParagraphMark_RemovesEmptyParagraphs()
    {
        var view = BuildView(new Paragraph("one"), new Paragraph(), new Paragraph("two"), new Paragraph(), new Paragraph("three"));

        ReplaceAll(view, "^p^p", "^p");

        BodyText(view).Should().Equal("one", "two", "three");
    }

    [StaFact]
    public void ReplaceAll_ParagraphMarkInReplacement_SplitsTheParagraph()
    {
        var view = BuildView(new Paragraph("one; two; three"));

        ReplaceAll(view, "; ", "^p");

        BodyText(view).Should().Equal("one", "two", "three");
    }

    [StaFact]
    public void ReplaceAll_TextRunningIntoAParagraphMark_MatchesOnlyAtTheParagraphEnd()
    {
        var view = BuildView(new Paragraph("end here end"), new Paragraph("next end"), new Paragraph("last"));

        ReplaceAll(view, "end^p", "END. ");

        BodyText(view).Should().Equal("end here END. next END. last");
    }

    [StaFact]
    public void ReplaceAll_FindWhatTextCode_ReinsertsTheMatch()
    {
        var view = BuildView(new Paragraph("cat and Cat"));

        ReplaceAll(view, "cat", "[^&]");

        BodyText(view).Should().Equal("[cat] and [Cat]");
    }

    [StaFact]
    public void ReplaceAll_AnyDigitAndAnyLetterCodes_MatchTheirClasses()
    {
        var view = BuildView(new Paragraph("a1 b2 c33"));

        ReplaceAll(view, "^$^#", "#");

        BodyText(view).Should().Equal("# # #3");
    }

    [StaFact]
    public void ReplaceAll_WhiteSpaceAndNonBreakingSpaceCodes()
    {
        var view = BuildView(new Paragraph("a    b c"));

        ReplaceAll(view, "^w", "_");
        BodyText(view).Should().Equal("a_b_c");

        var second = BuildView(new Paragraph("10 km and 5 km"));
        ReplaceAll(second, "^s", " ");
        BodyText(second).Should().Equal("10 km and 5 km");
    }

    [StaFact]
    public void ReplaceAll_WithEmptyReplacement_DeletesEveryMatch()
    {
        var view = BuildView(new Paragraph("keep drop keep drop keep"));

        var status = ReplaceAll(view, " drop", string.Empty);

        BodyText(view).Should().Equal("keep keep keep");
        status.Should().Contain("2");
    }

    [StaFact]
    public void ReplaceAll_FindsAMatchThatSpansDifferentlyFormattedRuns()
    {
        var view = BuildView(Runs(new Run("say hel"), new Run("lo", Bold), new Run(" twice: hello")));

        var status = ReplaceAll(view, "hello", "bye");

        BodyText(view).Should().Equal("say bye twice: bye");
        status.Should().Contain("2");
    }

    [StaFact]
    public void ReplaceAll_IsOneUndoStep_EvenWhenItMergesParagraphs()
    {
        var view = BuildView(new Paragraph("alpha"), new Paragraph("beta"), new Paragraph("gamma"));

        ReplaceAll(view, "^p", " ");
        BodyText(view).Should().Equal("alpha beta gamma");

        view.Undo();

        BodyText(view).Should().Equal("alpha", "beta", "gamma");
        view.Commands.CanUndo.Should().BeFalse();
    }

    [StaFact]
    public void ReplaceAll_ReplacementThatContainsTheTerm_IsNotReFound()
    {
        var view = BuildView(new Paragraph("a a"));

        var status = ReplaceAll(view, "a", "aa");

        BodyText(view).Should().Equal("aa aa");
        status.Should().Contain("2");
    }

    [StaFact]
    public void FindNext_ThenReplace_WalksBodyMatchesOneAtATime()
    {
        var view = BuildView(new Paragraph("one\ttwo"), new Paragraph("three\tfour"));

        WithDialog(view, "^t", ", ", dialog =>
        {
            dialog.FindNextForTest();
            view.Selection.IsEmpty.Should().BeFalse("the tab stop itself is selected");
            dialog.ReplaceForTest();
            BodyText(view).Should().Equal("one, two", "three\tfour");
            dialog.ReplaceForTest();
            BodyText(view).Should().Equal("one, two", "three, four");
        });
    }

    // ---- Options -----------------------------------------------------------------------------

    [StaFact]
    public void ReplaceAll_MatchPrefixAndMatchSuffix()
    {
        var prefix = BuildView(new Paragraph("interact winter inter"));
        ReplaceAll(prefix, "inter", "X", dialog => dialog.SetOptionForTest(FindReplaceOptionKind.MatchPrefix, true));
        BodyText(prefix).Should().Equal("Xact winter X");

        var suffix = BuildView(new Paragraph("walking king kingdom"));
        ReplaceAll(suffix, "king", "X", dialog => dialog.SetOptionForTest(FindReplaceOptionKind.MatchSuffix, true));
        BodyText(suffix).Should().Equal("walX X kingdom");
    }

    [StaFact]
    public void ReplaceAll_IgnorePunctuationAndWhiteSpace()
    {
        var punctuation = BuildView(new Paragraph("it's its"));
        ReplaceAll(punctuation, "its", "X", dialog => dialog.SetOptionForTest(FindReplaceOptionKind.IgnorePunctuation, true));
        BodyText(punctuation).Should().Equal("X X");

        var whiteSpace = BuildView(new Paragraph("data base and database"));
        ReplaceAll(whiteSpace, "database", "DB", dialog => dialog.SetOptionForTest(FindReplaceOptionKind.IgnoreWhiteSpace, true));
        BodyText(whiteSpace).Should().Equal("DB and DB");
    }

    [StaFact]
    public void Dialog_ShowsTheWordOptions_AndWildcardsDisableAllButMatchCase()
    {
        var view = BuildView(new Paragraph("text"));

        WithDialog(view, "x", "y", dialog =>
        {
            dialog.OptionLabelsForTest.Should().Equal(
                "Match case",
                "Whole word",
                "Use wildcards  (* ? [ ] < >)",
                "Match prefix",
                "Match suffix",
                "Ignore punctuation characters",
                "Ignore white-space characters");

            dialog.SetOptionForTest(FindReplaceOptionKind.MatchPrefix, true);
            dialog.SetOptionForTest(FindReplaceOptionKind.UseWildcards, true);

            dialog.IsOptionEnabledForTest(FindReplaceOptionKind.MatchCase).Should().BeTrue();
            dialog.IsOptionEnabledForTest(FindReplaceOptionKind.WholeWord).Should().BeFalse();
            dialog.IsOptionEnabledForTest(FindReplaceOptionKind.MatchPrefix).Should().BeFalse();
            dialog.IsOptionEnabledForTest(FindReplaceOptionKind.IgnoreWhiteSpace).Should().BeFalse();

            dialog.SetOptionForTest(FindReplaceOptionKind.UseWildcards, false);
            dialog.IsOptionEnabledForTest(FindReplaceOptionKind.MatchPrefix).Should().BeTrue();
        });
    }

    [StaFact]
    public void SpecialDropDown_IsPerBox_AndInsertsTheCodeAtTheCaret()
    {
        var view = BuildView(new Paragraph("text"));

        WithDialog(view, "ab", "cd", dialog =>
        {
            dialog.FocusFieldForTest(FindReplaceDialogFieldKind.Find);
            dialog.SpecialItemsForTest().Select(item => item.Insert).Should().Contain(["^p", "^?", "^#", "^$", "^w", "^m"]);
            dialog.SpecialItemsForTest().Select(item => item.Insert).Should().NotContain("^&");
            dialog.InsertSpecialForTest("^p");
            dialog.FindTextForTest.Should().Contain("^p");

            dialog.FocusFieldForTest(FindReplaceDialogFieldKind.Replace);
            dialog.SpecialItemsForTest().Select(item => item.Insert).Should().Contain(["^p", "^t", "^l", "^&"]);
            dialog.SpecialItemsForTest().Select(item => item.Insert).Should().NotContain("^?");
            dialog.InsertSpecialForTest("^&");
            dialog.ReplaceTextForTest.Should().Contain("^&");

            dialog.FocusFieldForTest(FindReplaceDialogFieldKind.Find);
            dialog.SetOptionForTest(FindReplaceOptionKind.UseWildcards, true);
            dialog.SpecialItemsForTest().Select(item => item.Insert).Should().Equal("?", "[0-9]", "[A-Za-z]", "<", ">");
        });
    }

    // ---- Formatting --------------------------------------------------------------------------

    [StaFact]
    public void ReplaceAll_FormattingOnly_FindsBoldText_AndReplacesItsFormatting()
    {
        var view = BuildView(Runs(new Run("plain "), new Run("strong", Bold), new Run(" plain "), new Run("also", Bold)));

        var status = ReplaceAll(view, string.Empty, string.Empty, dialog =>
        {
            dialog.SetFormatForTest(FindReplaceDialogFieldKind.Find, new FindFormat { Bold = true });
            dialog.SetFormatForTest(FindReplaceDialogFieldKind.Replace, new FindFormat { Bold = false, Italic = true });
        });

        view.CommitToModel();
        var paragraph = (Paragraph)view.Model.Blocks[0];
        paragraph.PlainText.Should().Be("plain strong plain also");
        paragraph.Runs.Where(run => run.Text.Contains("strong") || run.Text.Contains("also"))
            .Should().OnlyContain(run => run.Formatting.Italic && !run.Formatting.Bold);
        paragraph.Runs.Where(run => run.Text.Contains("plain"))
            .Should().OnlyContain(run => !run.Formatting.Italic && !run.Formatting.Bold);
        status.Should().Contain("2");
    }

    [StaFact]
    public void ReplaceAll_TextWithFindFormatting_ReplacesOnlyTheFormattedOccurrence()
    {
        var view = BuildView(Runs(new Run("word "), new Run("word", Bold), new Run(" word")));

        var status = ReplaceAll(view, "word", "X", dialog =>
            dialog.SetFormatForTest(FindReplaceDialogFieldKind.Find, new FindFormat { Bold = true }));

        BodyText(view).Should().Equal("word X word");
        status.Should().Contain("1");
    }

    [StaFact]
    public void ReplaceAll_TextWithReplaceFormatting_FormatsOnlyTheNewText()
    {
        var view = BuildView(new Paragraph("see cat here"));

        ReplaceAll(view, "cat", "tiger", dialog =>
            dialog.SetFormatForTest(
                FindReplaceDialogFieldKind.Replace,
                new FindFormat { Bold = true, ColorHex = "FF0000", Highlight = true }));

        view.CommitToModel();
        var paragraph = (Paragraph)view.Model.Blocks[0];
        paragraph.PlainText.Should().Be("see tiger here");
        var tiger = paragraph.Runs.Single(run => run.Text == "tiger");
        tiger.Formatting.Bold.Should().BeTrue();
        FindFormat.SameColor(tiger.Formatting.ColorHex, "#FF0000").Should().BeTrue();
        tiger.Formatting.HighlightColorHex.Should().NotBeNullOrEmpty();
        paragraph.Runs.Where(run => run.Text != "tiger").Should().OnlyContain(run => !run.Formatting.Bold);
    }

    [StaFact]
    public void ReplaceAll_FindByFontColourAndHighlight()
    {
        var red = RunFormatting.Default with { ColorHex = "#FF0000" };
        var marked = RunFormatting.Default with { HighlightColorHex = "#FFFF00" };
        var view = BuildView(Runs(new Run("a "), new Run("red", red), new Run(" b "), new Run("lit", marked), new Run(" c")));

        ReplaceAll(view, string.Empty, "R", dialog =>
            dialog.SetFormatForTest(FindReplaceDialogFieldKind.Find, new FindFormat { ColorHex = "ff0000" }));
        BodyText(view).Should().Equal("a R b lit c");

        ReplaceAll(view, string.Empty, "H", dialog =>
            dialog.SetFormatForTest(FindReplaceDialogFieldKind.Find, new FindFormat { Highlight = true }));
        BodyText(view).Should().Equal("a R b H c");
    }

    [StaFact]
    public void ReplaceAll_ReplacesOneParagraphStyleWithAnother()
    {
        var view = BuildView(
            new Paragraph("Title one") { StyleId = "Heading1" },
            new Paragraph("body text"),
            new Paragraph("Title two") { StyleId = "Heading1" });

        var status = ReplaceAll(view, string.Empty, string.Empty, dialog =>
        {
            dialog.SetFormatForTest(FindReplaceDialogFieldKind.Find, new FindFormat { ParagraphStyleId = "Heading1" });
            dialog.SetFormatForTest(FindReplaceDialogFieldKind.Replace, new FindFormat { ParagraphStyleId = "Heading2" });
        });

        view.CommitToModel();
        view.Model.Blocks.OfType<Paragraph>().Select(paragraph => paragraph.StyleId)
            .Should().Equal("Heading2", null, "Heading2");
        BodyText(view).Should().Equal("Title one", "body text", "Title two");
        status.Should().Contain("2");
    }

    [StaFact]
    public void FindNext_FormattingOnly_SelectsTheFormattedText()
    {
        var view = BuildView(Runs(new Run("plain "), new Run("strong", Bold), new Run(" plain")));

        WithDialog(view, string.Empty, string.Empty, dialog =>
        {
            dialog.SetFormatForTest(FindReplaceDialogFieldKind.Find, new FindFormat { Bold = true });
            dialog.FindNextForTest();
            view.Selection.Text.Should().Be("strong");
            dialog.StatusForTest.Should().BeEmpty();
        });
    }

    [StaFact]
    public void FormatSummaryLines_AppearUnderEachBox_AndNoFormattingClearsThem()
    {
        var view = BuildView(new Paragraph("text"));

        WithDialog(view, string.Empty, string.Empty, dialog =>
        {
            dialog.FormatSummaryForTest(FindReplaceDialogFieldKind.Find).IsVisible.Should().BeFalse();

            dialog.SetFormatForTest(
                FindReplaceDialogFieldKind.Find,
                new FindFormat { Bold = true, FontFamily = "Arial", FontSizePt = 12, ParagraphStyleId = "Heading1" });
            dialog.SetFormatForTest(FindReplaceDialogFieldKind.Replace, new FindFormat { Italic = false });

            dialog.FormatSummaryForTest(FindReplaceDialogFieldKind.Find)
                .Should().Be(("Format: Font: Arial, 12 pt, Bold, Style: Heading 1", true));
            dialog.FormatSummaryForTest(FindReplaceDialogFieldKind.Replace)
                .Should().Be(("Format: Font: Not Italic", true));

            dialog.ClearFormatForTest(FindReplaceDialogFieldKind.Find);
            dialog.FormatSummaryForTest(FindReplaceDialogFieldKind.Find).IsVisible.Should().BeFalse();
            dialog.FormatSummaryForTest(FindReplaceDialogFieldKind.Replace).IsVisible.Should().BeTrue();

            // With the find formatting gone and no text, there is nothing to search for.
            dialog.FindNextForTest();
            dialog.StatusForTest.Should().NotBeEmpty();
        });
    }

    [StaFact]
    public void FormatOnlySearch_WithNothingToReplaceWith_IsRefused()
    {
        var view = BuildView(Runs(new Run("plain "), new Run("strong", Bold)));

        var status = ReplaceAll(view, string.Empty, string.Empty, dialog =>
            dialog.SetFormatForTest(FindReplaceDialogFieldKind.Find, new FindFormat { Bold = true }));

        status.Should().Be("Enter replacement text or choose replacement formatting.");
        BodyText(view).Should().Equal("plain strong");
    }

    [StaFact]
    public void FindFormatDialog_RoundTripsCriteria_AndRejectsABadSize()
    {
        var document = TextDocument.CreateEmpty();
        var current = new FindFormat
        {
            Bold = true,
            Italic = false,
            FontFamily = "Arial",
            FontSizePt = 12,
            ColorHex = "#FF0000",
            Highlight = true,
            ParagraphStyleId = "Heading1",
        };
        var dialog = new FindFormatDialog(
            FindReplaceDialogFieldKind.Find, current, document, FindReplaceDialogText.NeutralEnglish);

        dialog.Title.Should().Be("Find Formatting");
        dialog.TryAcceptForTest().Should().BeTrue();
        dialog.Result.Should().Be(current);

        dialog.ApplyForTest(new FindFormatDialogInput(FontSizeText: "huge"));
        dialog.TryAcceptForTest().Should().BeFalse();
        dialog.StatusForTest.Should().Be("Enter a font size between 1 and 1638.");

        dialog.ApplyForTest(default);
        dialog.TryAcceptForTest().Should().BeTrue();
        dialog.Result!.IsEmpty.Should().BeTrue();
    }

    // ---- Every story -------------------------------------------------------------------------

    private static DocumentView BuildStoryView()
    {
        var firstSection = new Section(new PageSettings());
        firstSection.HeadersFooters.Header = new HeaderFooter("Draft S1 header");

        var noted = Runs(new Run("Draft body"), Run.FootnoteReference(1), Run.EndnoteReference(1));
        noted.SectionBreak = firstSection;
        var commented = Runs(new Run("second ") { CommentId = 7 }, Run.CommentReference(7));
        var boxed = Runs(new Run("third "), Run.FromShape(Shape.TextBoxWith("Draft box", 120, 40)));

        return BuildView(
            document =>
            {
                document.Header = new HeaderFooter("Draft header");
                document.Footer = new HeaderFooter("Draft footer");
                document.FinalSectionHeadersFooters.FirstHeader = new HeaderFooter("Draft first header");
                document.FinalSectionHeadersFooters.EvenFooter = new HeaderFooter("Draft even footer");
                document.Footnotes[1] = new Footnote(1, "Draft footnote");
                document.Endnotes[1] = new Endnote(1, "Draft endnote");
                var comment = new Comment(7, "Draft comment", "Ann");
                comment.AddReply(8, "Draft reply", "Bob");
                document.Comments[7] = comment;
            },
            noted,
            commented,
            boxed);
    }

    private static List<string> StoryText(DocumentView view)
    {
        view.CommitToModel();
        return DocumentStories.Enumerate(view.Model).Select(story => story.Paragraph.PlainText).ToList();
    }

    [StaFact]
    public void ReplaceAll_CoversEverySectionSlotNotesCommentsAndTextBoxes_AndCountsThemAll()
    {
        var view = BuildStoryView();
        view.CommitToModel();
        var expected = FindReplaceDialogPlanner.CountMatches(view.Model, "Draft", new FindReplaceSearchOptions());
        expected.Should().Be(11, "one body match plus ten in the other stories");

        var status = ReplaceAll(view, "Draft", "Final");

        BodyText(view)[0].Should().StartWith("Final body");
        StoryText(view).Should().Equal(
            "Final S1 header",
            "Final header",
            "Final footer",
            "Final first header",
            "Final even footer",
            "Final footnote",
            "Final endnote",
            "Final comment",
            "Final reply",
            "Final box");
        status.Should().Contain(expected.ToString(), "the reported count must match what the match count sees");
        FindReplaceDialogPlanner.CountMatches(view.Model, "Draft", new FindReplaceSearchOptions()).Should().Be(0);
    }

    [StaFact]
    public void ReplaceAll_AcrossStories_IsOneUndoStep()
    {
        var view = BuildStoryView();
        var before = StoryText(view);

        ReplaceAll(view, "Draft", "Final");
        view.Undo();

        StoryText(view).Should().Equal(before);
        BodyText(view)[0].Should().StartWith("Draft body");
        view.Commands.CanUndo.Should().BeFalse();
    }

    [StaFact]
    public void ReplaceAll_RestrictedToASelection_LeavesEveryOtherStoryAlone()
    {
        var view = BuildStoryView();
        var before = StoryText(view);
        view.SetSelectionRangeForTest(0, 0, 0, "Draft body".Length);

        ReplaceAll(view, "Draft", "Final");

        StoryText(view).Should().Equal(before);
        BodyText(view)[0].Should().StartWith("Final body");
    }

    [StaFact]
    public void FindNext_ContinuesFromTheBodyIntoTheOtherStories_AndWrapsBack()
    {
        var view = BuildView(
            document =>
            {
                document.Header = new HeaderFooter("needle in header");
                document.Footnotes[1] = new Footnote(1, "needle in note");
            },
            Runs(new Run("needle in body"), Run.FootnoteReference(1)));

        WithDialog(view, "needle", "x", dialog =>
        {
            dialog.FindNextForTest();
            view.Selection.Text.Should().Be("needle");
            dialog.StatusForTest.Should().BeEmpty();

            dialog.FindNextForTest();
            dialog.StatusForTest.Should().Be("Found in header (section 1): needle in header");

            dialog.FindNextForTest();
            dialog.StatusForTest.Should().Be("Found in footnote 1: needle in note");

            // Stories exhausted: back to the top of the body.
            dialog.FindNextForTest();
            dialog.StatusForTest.Should().BeEmpty();
            view.Selection.Text.Should().Be("needle");
        });
    }

    [StaFact]
    public void Replace_ReplacesTheStoryMatchFindNextLandedOn_ThenMovesToTheNext()
    {
        var view = BuildView(
            document =>
            {
                document.Header = new HeaderFooter("needle one, needle two");
                document.Footer = new HeaderFooter("needle three");
            },
            new Paragraph("nothing here"));

        WithDialog(view, "needle", "pin", dialog =>
        {
            dialog.FindNextForTest();
            dialog.StatusForTest.Should().StartWith("Found in header (section 1)");

            dialog.ReplaceForTest();
            view.Model.Header!.PlainText.Should().Be("pin one, needle two");
            dialog.StatusForTest.Should().StartWith("Found in header (section 1)");

            dialog.ReplaceForTest();
            view.Model.Header!.PlainText.Should().Be("pin one, pin two");
            dialog.StatusForTest.Should().StartWith("Found in footer (section 1)");

            dialog.ReplaceForTest();
            view.Model.Footer!.PlainText.Should().Be("pin three");
            dialog.StatusForTest.Should().Contain("not found");
        });

        view.Undo();
        view.Model.Footer!.PlainText.Should().Be("needle three");
    }

    [StaFact]
    public void ReplaceAll_ByFormatting_ReachesAFootnote()
    {
        var note = new Footnote(1);
        note.Content.Add(Runs(new Run("plain "), new Run("strong", Bold)));
        var view = BuildView(
            document => document.Footnotes[1] = note,
            Runs(new Run("body "), new Run("bold", Bold), Run.FootnoteReference(1)));

        var status = ReplaceAll(view, string.Empty, string.Empty, dialog =>
        {
            dialog.SetFormatForTest(FindReplaceDialogFieldKind.Find, new FindFormat { Bold = true });
            dialog.SetFormatForTest(FindReplaceDialogFieldKind.Replace, new FindFormat { Bold = false, Underline = true });
        });

        view.CommitToModel();
        view.Model.Footnotes[1].Content[0].Runs.Single(run => run.Text == "strong").Formatting
            .Should().Be(RunFormatting.Default with { Underline = true });
        ((Paragraph)view.Model.Blocks[0]).Runs.Single(run => run.Text == "bold").Formatting.Underline.Should().BeTrue();
        status.Should().Contain("2");
    }

    [StaFact]
    public void ReplaceAll_WithTrackChanges_TracksStoryEditsAndDoesNotLoop()
    {
        var view = BuildView(
            document => document.Footnotes[1] = new Footnote(1, "Confidential note"),
            Runs(new Run("Confidential body"), Run.FootnoteReference(1)));
        view.RevisionAuthor = "Ada Reviewer";
        view.TrackChangesEnabled = true;

        var status = ReplaceAll(view, "Confidential", "Strictly Confidential");

        view.CommitToModel();
        var noteRuns = view.Model.Footnotes[1].Content[0].Runs;
        noteRuns.Should().ContainSingle(run => run.Revision == RevisionKind.Inserted && run.Text == "Strictly Confidential");
        noteRuns.Should().ContainSingle(run => run.Revision == RevisionKind.Deleted && run.Text == "Confidential");
        var bodyRuns = ((Paragraph)view.Model.Blocks[0]).Runs;
        bodyRuns.Should().ContainSingle(run => run.Revision == RevisionKind.Inserted && run.Text == "Strictly Confidential");
        bodyRuns.Should().ContainSingle(run => run.Revision == RevisionKind.Deleted && run.Text == "Confidential");
        status.Should().Contain("2");
    }
}
