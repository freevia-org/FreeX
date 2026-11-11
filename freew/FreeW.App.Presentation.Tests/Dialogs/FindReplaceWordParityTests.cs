using Free.Shared.AppServices;
using FreeW.App.Presentation.ContextMenus;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests;

/// <summary>
/// Word-parity Find and Replace: special (caret) codes, find/replace formatting, the extra match
/// options, and search/replace across the stories outside the body.
/// </summary>
public sealed class FindReplaceWordParityTests
{
    private static readonly FindReplaceSearchOptions NoOptions = new();
    private static readonly RunFormatting Bold = new() { Bold = true };

    private sealed class Context(TextDocument document) : IDocumentCommandContext
    {
        public TextDocument Document { get; } = document;
    }

    // ---- Special drop-down -------------------------------------------------------------------

    [Fact]
    public void SpecialItems_ForFindWhat_OfferEveryWordSearchCode()
    {
        FindReplaceDialogPlanner.BuildSpecialItems(FindReplaceDialogFieldKind.Find, useWildcards: false)
            .Select(item => item.Insert)
            .Should().Equal("^p", "^t", "^?", "^#", "^$", "^^", "^+", "^=", "^l", "^m", "^~", "^s", "^-", "^w");
    }

    [Fact]
    public void SpecialItems_ForReplaceWith_OfferOnlyInsertableCodesPlusFindWhatText()
    {
        var items = FindReplaceDialogPlanner.BuildSpecialItems(FindReplaceDialogFieldKind.Replace, useWildcards: false);

        items.Select(item => item.Insert)
            .Should().Equal("^p", "^t", "^^", "^+", "^=", "^&", "^l", "^~", "^s", "^-");
        items.Should().Contain(item => item.Label == "Find What Text  (^&)");
    }

    [Fact]
    public void SpecialItems_WithWildcards_OfferWildcardOperatorsForFindWhatOnly()
    {
        FindReplaceDialogPlanner.BuildSpecialItems(FindReplaceDialogFieldKind.Find, useWildcards: true)
            .Select(item => item.Insert)
            .Should().Equal("?", "[0-9]", "[A-Za-z]", "<", ">");
        FindReplaceDialogPlanner.BuildSpecialItems(FindReplaceDialogFieldKind.Replace, useWildcards: true)
            .Should().Contain(item => item.Insert == "^p");
    }

    [Fact]
    public void SpecialItems_AreLocalized_AndEveryCodeIsOneTheSearchUnderstands()
    {
        var text = new FindReplaceDialogText(key => $"[{key}]");

        FindReplaceDialogPlanner.BuildSpecialItems(FindReplaceDialogFieldKind.Find, false, text)[0].Label
            .Should().Be("[FreeW_FindReplace_Special_ParagraphMark]  (^p)");
        FindReplaceDialogPlanner.BuildSpecialItems(FindReplaceDialogFieldKind.Find, false)
            .Should().OnlyContain(item => FindSpecialCodes.ContainsFindCode(item.Insert));
        FreeWContextMenuPlanner.FindSpecialCharacters.Select(item => item.Insert)
            .Should().Equal(FindReplaceDialogPlanner
                .BuildSpecialItems(FindReplaceDialogFieldKind.Find, false)
                .Select(item => item.Insert));
    }

    // ---- Options -----------------------------------------------------------------------------

    [Fact]
    public void NormalizeOptions_WithWildcards_ClearsEveryOptionButMatchCase()
    {
        var all = new FindReplaceSearchOptions(true, true, true, true, true, true, true);

        FindReplaceDialogPlanner.NormalizeOptions(all)
            .Should().Be(new FindReplaceSearchOptions(MatchCase: true, WholeWord: false, UseWildcards: true));
        FindReplaceDialogPlanner.NormalizeOptions(all with { UseWildcards = false })
            .Should().Be(all with { UseWildcards = false });
    }

    [Fact]
    public void PlainEditorSearch_IsOnlyUsedWhenNoOptionIsSet()
    {
        FindReplaceDialogPlanner.ShouldUsePlainEditorSearch(NoOptions).Should().BeTrue();
        FindReplaceDialogPlanner.ShouldUsePlainEditorSearch(NoOptions with { MatchPrefix = true }).Should().BeFalse();
        FindReplaceDialogPlanner.ShouldUsePlainEditorSearch(NoOptions with { IgnoreWhiteSpace = true }).Should().BeFalse();
    }

    [Fact]
    public void OptionLabels_ForNewOptions_ResolveThroughTheTextCatalog()
    {
        var text = new FindReplaceDialogText(key => $"[{key}]");

        FindReplaceDialogPlanner.LabelFor(FindReplaceOptionKind.MatchPrefix, text)
            .Should().Be("[FreeW_FindReplace_Option_MatchPrefix]");
        FindReplaceDialogPlanner.LabelFor(FindReplaceOptionKind.MatchCase, text).Should().Be("Match case");
        FindReplaceDialogPlanner.OptionChoices
            .Should().OnlyContain(choice =>
                FindReplaceDialogPlanner.LabelFor(choice.Kind, FindReplaceDialogText.NeutralEnglish) == choice.Label);
    }

    [Fact]
    public void FindAll_HonoursCaretCodesAndTheNewOptions()
    {
        FindReplaceDialogPlanner.FindAll("a\tb", "^t", NoOptions).Should().Equal((1, 1));
        FindReplaceDialogPlanner.FindAll("interact winter", "inter", NoOptions with { MatchPrefix = true })
            .Should().Equal((0, 5));
        FindReplaceDialogPlanner.FindAll("walking kingdom", "king", NoOptions with { MatchSuffix = true })
            .Should().Equal((3, 4));
        FindReplaceDialogPlanner.FindAll("it's", "its", NoOptions with { IgnorePunctuation = true })
            .Should().Equal((0, 4));
        FindReplaceDialogPlanner.FindAll("data base", "database", NoOptions with { IgnoreWhiteSpace = true })
            .Should().Equal((0, 9));
    }

    // ---- Requests and replacement plans ------------------------------------------------------

    [Fact]
    public void SearchRequest_AllowsAnEmptyTermOnlyWithFormatting()
    {
        FindReplaceDialogPlanner.TryCreateSearchRequest("", NoOptions, FindFormat.None, out _, out var error)
            .Should().BeFalse();
        error.Should().Be(FindReplaceValidationError.SearchTermRequired);

        FindReplaceDialogPlanner.TryCreateSearchRequest(
                "", NoOptions, new FindFormat { Bold = true }, out var request, out error)
            .Should().BeTrue();
        error.Should().BeNull();
        request!.Term.Should().BeEmpty();
        request.ToCriteria().IsFormatOnly.Should().BeTrue();
    }

    [Fact]
    public void ReplaceRequest_ForAFormatOnlySearch_NeedsSomethingToReplaceWith()
    {
        var bold = new FindFormat { Bold = true };

        FindReplaceDialogPlanner.TryCreateReplaceRequest("", "", NoOptions, bold, null, out _, out var error)
            .Should().BeFalse();
        error.Should().Be(FindReplaceValidationError.ReplacementRequired);
        FindReplaceDialogPlanner.ValidationMessageFor(error, null, FindReplaceDialogText.NeutralEnglish)
            .Should().Be("Enter replacement text or choose replacement formatting.");

        FindReplaceDialogPlanner.TryCreateReplaceRequest(
                "", "", NoOptions, bold, new FindFormat { Italic = true }, out var request, out _)
            .Should().BeTrue();
        request!.ReplaceFormat.Should().Be(new FindFormat { Italic = true });

        FindReplaceDialogPlanner.TryCreateReplaceRequest("x", "", NoOptions, null, FindFormat.None, out request, out _)
            .Should().BeTrue();
        request!.ReplaceFormat.Should().BeNull();
    }

    [Fact]
    public void PlanReplacement_FollowsWordsTextVersusFormattingRules()
    {
        var textOnly = FindReplaceDialogPlanner.PlanReplacement(
            new FindReplaceReplaceRequest("cat", "[^&]^p", NoOptions), "Cat");
        textOnly.Should().Be(new FindReplaceReplacementPlan(true, "[Cat]\r", null));
        textOnly.InsertsParagraphMark.Should().BeTrue();

        var deletion = FindReplaceDialogPlanner.PlanReplacement(new FindReplaceReplaceRequest("cat", "", NoOptions), "cat");
        deletion.Should().Be(new FindReplaceReplacementPlan(true, string.Empty, null));

        var italic = new FindFormat { Italic = true };
        FindReplaceDialogPlanner.PlanReplacement(
                new FindReplaceReplaceRequest("cat", "", NoOptions, ReplaceFormat: italic), "cat")
            .Should().Be(new FindReplaceReplacementPlan(false, "cat", italic));
        FindReplaceDialogPlanner.PlanReplacement(
                new FindReplaceReplaceRequest("cat", "dog", NoOptions, ReplaceFormat: italic), "cat")
            .Should().Be(new FindReplaceReplacementPlan(true, "dog", italic));
    }

    [Fact]
    public void StatusBuilders_NameAFormatOnlySearchInsteadOfQuotingAnEmptyTerm()
    {
        var request = new FindReplaceSearchRequest("", NoOptions, new FindFormat { Bold = true });

        FindReplaceDialogPlanner.BuildFindStatus(request, found: false).Should().Be("\"formatted text\" not found.");
    }

    // ---- Formatting --------------------------------------------------------------------------

    [Fact]
    public void DescribeFormat_ListsWhatIsSetTheWayWordsSummaryLineDoes()
    {
        var document = new TextDocument();
        document.Styles["Heading1"] = new DocumentStyle { Id = "Heading1", Name = "Heading 1" };
        var format = new FindFormat
        {
            FontFamily = "Arial",
            FontSizePt = 12,
            Bold = true,
            Italic = false,
            Underline = true,
            ColorHex = "ff0000",
            Highlight = true,
            ParagraphStyleId = "Heading1",
        };

        FindReplaceDialogPlanner.DescribeFormat(format, document: document).Should().Be(
            "Format: Font: Arial, 12 pt, Bold, Not Italic, Underline, Font color: #FF0000, Highlight, Style: Heading 1");
        FindReplaceDialogPlanner.DescribeFormat(new FindFormat { Highlight = false })
            .Should().Be("Format: Not Highlight");
        FindReplaceDialogPlanner.DescribeFormat(new FindFormat { Highlight = true, HighlightColorHex = "00ff00" })
            .Should().Be("Format: Highlight: #00FF00");
        FindReplaceDialogPlanner.DescribeFormat(FindFormat.None).Should().BeEmpty();
        FindReplaceDialogPlanner.DescribeFormat(null).Should().BeEmpty();
    }

    [Fact]
    public void TryBuildFormat_ParsesTheFormatDialogFields()
    {
        FindReplaceDialogPlanner.TryBuildFormat(
                new FindFormatDialogInput(
                    Bold: true,
                    Italic: false,
                    FontFamily: " Arial ",
                    FontSizeText: "12",
                    ColorText: "ff0000",
                    Highlight: true,
                    HighlightColorText: "#00ff00",
                    ParagraphStyleId: "Heading1"),
                out var format,
                out var error)
            .Should().BeTrue();

        error.Should().BeNull();
        format.Should().Be(new FindFormat
        {
            Bold = true,
            Italic = false,
            FontFamily = "Arial",
            FontSizePt = 12,
            ColorHex = "#FF0000",
            Highlight = true,
            HighlightColorHex = "#00FF00",
            ParagraphStyleId = "Heading1",
        });

        FindReplaceDialogPlanner.TryBuildFormat(default, out format, out _).Should().BeTrue();
        format.IsEmpty.Should().BeTrue();
        FindFormatDialogInput.From(new FindFormat { Bold = true, ColorHex = "#FF0000" })
            .Should().Be(new FindFormatDialogInput(Bold: true, ColorText: "#FF0000"));
    }

    [Theory]
    [InlineData("abc", null, FindReplaceTextId.FormatDialogInvalidSize)]
    [InlineData("0", null, FindReplaceTextId.FormatDialogInvalidSize)]
    [InlineData("12", "red", FindReplaceTextId.FormatDialogInvalidColor)]
    public void TryBuildFormat_RejectsBadSizesAndColours(string size, string? color, FindReplaceTextId expected)
    {
        FindReplaceDialogPlanner.TryBuildFormat(
                new FindFormatDialogInput(FontSizeText: size, ColorText: color), out _, out var error)
            .Should().BeFalse();
        error.Should().Be(expected);
    }

    [Fact]
    public void FormatStyleChoices_ListParagraphStylesByName_AndAlwaysOfferNormal()
    {
        var document = new TextDocument();
        document.Styles.Clear();
        document.Styles["Heading1"] = new DocumentStyle { Id = "Heading1", Name = "Heading 1" };
        document.Styles["Strong"] = new DocumentStyle { Id = "Strong", Name = "Strong", Type = StyleType.Character };

        FindReplaceDialogPlanner.BuildFormatStyleChoices(document)
            .Should().Equal(("Heading1", "Heading 1"), ("Normal", "Normal"));
    }

    [Fact]
    public void FindAllInSegments_AppliesTextAndFormattingCriteria()
    {
        FindTextSegment[] segments =
        [
            new("cat ", RunFormatting.Default, null),
            new("cat", Bold, null),
            new("\r", RunFormatting.Default, null, IsStructural: true),
            new("cat", Bold, "Heading1"),
        ];

        FindReplaceDialogPlanner.FindAllInSegments(
                segments, new FindReplaceSearchRequest("cat", NoOptions, new FindFormat { Bold = true }))
            .Should().Equal((4, 3), (8, 3));
        FindReplaceDialogPlanner.FindAllInSegments(
                segments, new FindReplaceSearchRequest("cat^pcat", NoOptions, new FindFormat { Bold = true }))
            .Should().Equal((4, 7));
        FindReplaceDialogPlanner.FindAllInSegments(
                segments, new FindReplaceSearchRequest("", NoOptions, new FindFormat { ParagraphStyleId = "Heading1" }))
            .Should().Equal((8, 3));
    }

    // ---- Stories -----------------------------------------------------------------------------

    private static TextDocument BuildStoryDocument()
    {
        var document = new TextDocument();
        var firstSection = new Section(new PageSettings());
        firstSection.HeadersFooters.Header = new HeaderFooter("Draft S1 header");
        document.Blocks.Add(new Paragraph("Draft body") { SectionBreak = firstSection });

        var host = new Paragraph("Second ");
        host.Runs.Add(Run.FromShape(Shape.TextBoxWith("Draft box", 100, 40)));
        document.Blocks.Add(host);

        document.Header = new HeaderFooter("Draft header");
        document.Footer = new HeaderFooter("Page footer Draft");
        document.FinalSectionHeadersFooters.FirstHeader = new HeaderFooter("Draft first");
        document.Footnotes[1] = new Footnote(1, "Draft note");
        document.Endnotes[1] = new Endnote(1, "Draft endnote");
        var comment = new Comment(3, "Draft comment", "Ann");
        comment.AddReply(4, "Draft reply", "Bob");
        document.Comments[3] = comment;
        return document;
    }

    private static int ReplaceAllInStories(
        TextDocument document,
        FindReplaceReplaceRequest request,
        List<IDocumentCommand>? executed = null,
        FindReplaceRevisionContext? tracked = null)
    {
        var context = new Context(document);
        return FindReplaceStoryPlanner.ReplaceAll(
            document,
            request,
            command =>
            {
                command.Apply(context);
                executed?.Add(command);
            },
            tracked);
    }

    [Fact]
    public void CountMatches_CoversBodyAndEveryStory_WithoutDoubleCountingTextBoxMirrors()
    {
        var document = BuildStoryDocument();

        // body 1 + S1 header + header + footer + first header + footnote + endnote + comment + reply
        // + text box = 10. The text box's mirror text in its host paragraph is not counted again.
        FindReplaceDialogPlanner.CountMatches(document, "Draft", NoOptions).Should().Be(10);
    }

    [Fact]
    public void StoryFindNext_WalksStoriesInOrder_AndResumesPastAPosition()
    {
        var document = BuildStoryDocument();
        var request = new FindReplaceSearchRequest("Draft", NoOptions);
        var found = new List<string>();

        FindReplaceStoryPosition? from = null;
        while (FindReplaceStoryPlanner.FindNext(document, request, from) is { } match)
        {
            found.Add(FindReplaceStoryPlanner.DescribeLocation(match));
            from = match.End;
        }

        found.Should().Equal(
            "header (section 1)",
            "header (section 2)",
            "footer (section 2)",
            "first-page header (section 2)",
            "footnote 1",
            "endnote 1",
            "comment 3",
            "comment 4",
            "text box 1");
    }

    [Fact]
    public void StoryMatch_DescribesItsLocationAndContext()
    {
        var document = BuildStoryDocument();
        var match = FindReplaceStoryPlanner.FindNext(
            document,
            new FindReplaceSearchRequest("footer", NoOptions),
            null)!;

        match.MatchedText.Should().Be("footer");
        FindReplaceStoryPlanner.DescribeMatch(match).Should().Be("Found in footer (section 2): Page footer Draft");
        FindReplaceStoryPlanner.IsCurrent(document, new FindReplaceSearchRequest("footer", NoOptions), match)
            .Should().BeTrue();
        FindReplaceStoryPlanner.IsCurrent(document, new FindReplaceSearchRequest("Draft", NoOptions), match)
            .Should().BeFalse();
    }

    [Fact]
    public void StoryReplaceAll_ReplacesInEveryStory_AndUndoRestoresEachOne()
    {
        var document = BuildStoryDocument();
        var executed = new List<IDocumentCommand>();

        var count = ReplaceAllInStories(document, new FindReplaceReplaceRequest("Draft", "Final", NoOptions), executed);

        count.Should().Be(9);
        document.Sections[0].HeadersFooters.Header!.PlainText.Should().Be("Final S1 header");
        document.Header!.PlainText.Should().Be("Final header");
        document.Footer!.PlainText.Should().Be("Page footer Final");
        document.FinalSectionHeadersFooters.FirstHeader!.PlainText.Should().Be("Final first");
        document.Footnotes[1].PlainText.Should().Be("Final note");
        document.Endnotes[1].PlainText.Should().Be("Final endnote");
        document.Comments[3].PlainText.Should().Be("Final comment");
        document.Comments[3].Replies[0].PlainText.Should().Be("Final reply");
        ((Paragraph)document.Blocks[1]).Runs[1].Shape!.PlainText.Should().Be("Final box");
        ((Paragraph)document.Blocks[0]).PlainText.Should().Be("Draft body", "the body is the host's job");
        FindReplaceDialogPlanner.CountMatches(document, "Draft", NoOptions).Should().Be(1);

        var context = new Context(document);
        for (var i = executed.Count - 1; i >= 0; i--)
            executed[i].Revert(context);

        FindReplaceDialogPlanner.CountMatches(document, "Draft", NoOptions).Should().Be(10);
        document.Comments[3].Replies[0].PlainText.Should().Be("Draft reply");
    }

    [Fact]
    public void StoryReplaceAll_DoesNotReFindAReplacementThatContainsTheSearchTerm()
    {
        var document = new TextDocument();
        document.Blocks.Add(new Paragraph("body"));
        document.Footnotes[1] = new Footnote(1, "aa a");

        ReplaceAllInStories(document, new FindReplaceReplaceRequest("a", "aa", NoOptions)).Should().Be(3);
        document.Footnotes[1].PlainText.Should().Be("aaaa aa");

        ReplaceAllInStories(document, new FindReplaceReplaceRequest("a", "", NoOptions)).Should().Be(6);
        document.Footnotes[1].PlainText.Should().Be(" ");
    }

    [Fact]
    public void StoryReplaceAll_ExpandsCodes_ButSkipsAReplacementThatInsertsAParagraphMark()
    {
        var document = new TextDocument();
        document.Blocks.Add(new Paragraph("body"));
        document.Header = new HeaderFooter("a;b");

        ReplaceAllInStories(document, new FindReplaceReplaceRequest(";", "^p", NoOptions)).Should().Be(0);
        document.Header.PlainText.Should().Be("a;b");

        ReplaceAllInStories(document, new FindReplaceReplaceRequest(";", "^t[^&]", NoOptions)).Should().Be(1);
        document.Header.PlainText.Should().Be("a\t[;]b");
    }

    [Fact]
    public void StoryReplaceAll_LeavesFieldTextAlone()
    {
        var document = new TextDocument();
        document.Blocks.Add(new Paragraph("body"));
        var footer = new HeaderFooter();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("Page 1 of "));
        paragraph.Runs.Add(new Run("1") { FieldKind = RunFieldKind.PageNumber });
        footer.Paragraphs.Add(paragraph);
        document.Footer = footer;

        ReplaceAllInStories(document, new FindReplaceReplaceRequest("1", "one", NoOptions)).Should().Be(1);

        paragraph.Runs.Select(run => run.Text).Should().Equal("Page one of ", "1");
        paragraph.Runs[1].FieldKind.Should().Be(RunFieldKind.PageNumber);
    }

    [Fact]
    public void StoryReplaceAll_FindsByFormatting_AndAppliesReplacementFormattingAndStyle()
    {
        var document = new TextDocument();
        document.Blocks.Add(new Paragraph("body"));
        var note = new Footnote(1);
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("plain "));
        paragraph.Runs.Add(new Run("strong", Bold));
        paragraph.Runs.Add(new Run(" tail"));
        note.Content.Add(paragraph);
        document.Footnotes[1] = note;

        var count = ReplaceAllInStories(
            document,
            new FindReplaceReplaceRequest(
                string.Empty,
                string.Empty,
                NoOptions,
                FindFormat: new FindFormat { Bold = true },
                ReplaceFormat: new FindFormat { Bold = false, Italic = true, ParagraphStyleId = "Quote" }));

        count.Should().Be(1);
        paragraph.PlainText.Should().Be("plain strong tail");
        paragraph.Runs.Single(run => run.Text == "strong").Formatting
            .Should().Be(new RunFormatting { Italic = true });
        paragraph.StyleId.Should().Be("Quote");
    }

    [Fact]
    public void StoryReplaceAll_WithReplacementTextAndFormatting_FormatsOnlyTheNewText()
    {
        var document = new TextDocument();
        document.Blocks.Add(new Paragraph("body"));
        document.Endnotes[1] = new Endnote(1, "see cat here");

        ReplaceAllInStories(
                document,
                new FindReplaceReplaceRequest("cat", "tiger", NoOptions, ReplaceFormat: new FindFormat { Bold = true }))
            .Should().Be(1);

        var runs = document.Endnotes[1].Content[0].Runs;
        runs.Select(run => (run.Text, run.Formatting.Bold))
            .Should().Equal(("see ", false), ("tiger", true), (" here", false));
    }

    [Fact]
    public void StoryReplaceAll_UnderTrackChanges_RecordsDeletionAndInsertion()
    {
        var document = new TextDocument();
        document.Blocks.Add(new Paragraph("body"));
        document.Header = new HeaderFooter("old old");

        var count = ReplaceAllInStories(
            document,
            new FindReplaceReplaceRequest("old", "new", NoOptions),
            tracked: new FindReplaceRevisionContext("Ann", "2026-01-01T00:00:00Z"));

        count.Should().Be(2);
        var runs = document.Header.Paragraphs[0].Runs;
        runs.Count(run => run.Revision == RevisionKind.Deleted && run.Text == "old").Should().Be(2);
        runs.Count(run => run.Revision == RevisionKind.Inserted && run.Text == "new").Should().Be(2);
        runs.Where(run => run.Revision != RevisionKind.None).Should().OnlyContain(run => run.RevisionAuthor == "Ann");
    }

    // ---- Session -----------------------------------------------------------------------------

    private sealed class Host : IFindReplaceDialogCommandHost
    {
        public FindReplaceSearchRequest? Find { get; private set; }
        public FindReplaceReplaceRequest? Replace { get; private set; }
        public string? Status { get; set; }
        public bool Found { get; set; } = true;

        public string? LastMatchStatus => Status;

        public bool FindNext(FindReplaceSearchRequest request)
        {
            Find = request;
            return Found;
        }

        public bool ReplaceNext(FindReplaceReplaceRequest request)
        {
            Replace = request;
            return Found;
        }

        public FindReplaceAllExecutionResult ReplaceAll(FindReplaceReplaceRequest request)
        {
            Replace = request;
            return new FindReplaceAllExecutionResult(2);
        }
    }

    [Fact]
    public void Session_CarriesFormatsAndNewOptionsIntoRequests_AndSummarisesThem()
    {
        var host = new Host();
        var session = new FindReplaceDialogSession(host);
        var bold = new FindFormat { Bold = true };
        var italic = new FindFormat { Italic = true };

        session.SetFormat(FindReplaceDialogFieldKind.Find, bold).FindFormatSummary.Should().Be("Format: Font: Bold");
        var state = session.SetFormat(FindReplaceDialogFieldKind.Replace, italic);
        state.ReplaceFormatSummary.Should().Be("Format: Font: Italic");
        state.FindFormat.Should().Be(bold);
        session.FormatFor(FindReplaceDialogFieldKind.Replace).Should().Be(italic);

        session.Execute(
            FindReplaceDialogActionKind.ReplaceAll,
            new FindReplaceDialogInput("", "", false, false, false, MatchPrefix: true, IgnorePunctuation: true));

        host.Replace.Should().Be(new FindReplaceReplaceRequest(
            string.Empty,
            string.Empty,
            new FindReplaceSearchOptions(false, false, false, MatchPrefix: true, IgnorePunctuation: true),
            bold,
            italic));
        session.State.StatusText.Should().Be("Replaced 2 occurrences.");
    }

    [Fact]
    public void Session_NoFormatting_ClearsOneBox_AndAnEmptySearchIsThenRejected()
    {
        var host = new Host();
        var session = new FindReplaceDialogSession(host);
        session.SetFormat(FindReplaceDialogFieldKind.Find, new FindFormat { Bold = true });
        session.SetFormat(FindReplaceDialogFieldKind.Replace, new FindFormat { Italic = true });

        var state = session.ClearFormat(FindReplaceDialogFieldKind.Find);

        state.FindFormat.Should().BeNull();
        state.FindFormatSummary.Should().BeEmpty();
        state.ReplaceFormat.Should().NotBeNull();
        session.Execute(FindReplaceDialogActionKind.FindNext, new FindReplaceDialogInput("", "", false, false, false))
            .StatusText.Should().Be(FindReplaceDialogPlanner.SearchTermRequiredMessage);
        host.Find.Should().BeNull();
        session.SetFormat(FindReplaceDialogFieldKind.Replace, FindFormat.None).ReplaceFormat.Should().BeNull();
    }

    [Fact]
    public void Session_FormatOnlyFind_IsRoutedToTheHost_AndUsesTheHostsStoryStatus()
    {
        var host = new Host { Status = "Found in footnote 1: note" };
        var session = new FindReplaceDialogSession(host);
        session.SetFormat(FindReplaceDialogFieldKind.Find, new FindFormat { Bold = true });

        var state = session.Execute(
            FindReplaceDialogActionKind.FindNext,
            new FindReplaceDialogInput("", "", false, false, false));

        host.Find!.Format.Should().Be(new FindFormat { Bold = true });
        state.StatusText.Should().Be("Found in footnote 1: note");

        host.Found = false;
        session.FindNext().StatusText.Should().Be("\"formatted text\" not found.");
    }

    [Fact]
    public void Session_OptionState_DisablesEverythingButMatchCaseUnderWildcards()
    {
        var session = new FindReplaceDialogSession(new Host());

        var state = session.SetInput(new FindReplaceDialogInput("a", "", true, true, true, true, true, true, true));

        state.IsOptionChecked(FindReplaceOptionKind.MatchCase).Should().BeTrue();
        state.IsOptionChecked(FindReplaceOptionKind.MatchPrefix).Should().BeFalse();
        state.IsOptionEnabled(FindReplaceOptionKind.IgnoreWhiteSpace).Should().BeFalse();
        state.IsOptionEnabled(FindReplaceOptionKind.MatchCase).Should().BeTrue();
        session.BuildSpecialItems(FindReplaceDialogFieldKind.Find).Select(item => item.Insert).Should().Contain("<");
    }

    [Fact]
    public void TextCatalog_ExposesEveryStringAsARequiredResourceKey()
    {
        FindReplaceDialogText.RequiredResourceKeys.Should().OnlyHaveUniqueItems();
        FindReplaceDialogText.RequiredResourceKeys.Should().HaveCount(Enum.GetValues<FindReplaceTextId>().Length);
        FindReplaceDialogPlanner.RequiredResourceKeys.Should().Contain(FindReplaceDialogText.RequiredResourceKeys);
        foreach (var id in Enum.GetValues<FindReplaceTextId>())
            FindReplaceDialogText.NeutralEnglish.Get(id).Should().NotBeNullOrWhiteSpace();
    }
}
