using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using FluentAssertions;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// freew-find-replace F1: FindReplaceDialogPlanner.FindNextMatch -- the search engine
/// <see cref="DocumentView.FindNext(string, FindReplaceSearchOptions)"/> (and transitively ReplaceNext /
/// ReplaceAll) actually drives -- used to only ever walk <c>document.Blocks</c>, so Find Next / Replace /
/// Replace All reported "not found" / 0 replacements for a term that existed only in the document's
/// default header or footer, even though it was plainly visible on every page. These tests exercise the
/// full production path end to end (not just the planner's match-location math covered by
/// FreeW.App.Presentation.Tests.Dialogs.FindReplaceDialogPlannerTests): DocumentView.FindNext/ReplaceAll
/// -&gt; FindReplaceDialogPlanner.FindNextMatch -&gt; DocumentView.SelectFindReplaceMatch, proving a header/
/// footer hit is not just detected but actually selected (SelectedText) and replaced (model mutation).
/// </summary>
public sealed class FindReplaceHeaderFooterTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    private static Task<bool> OnUiThread(Action action) => HeadlessUiThread.Run(action);

    private static DocumentView BuildViewWithHeaderAndBody(string headerText, string bodyText)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph(bodyText));
        doc.Header = new HeaderFooter(headerText);

        var view = new DocumentView();
        view.LoadDocument(doc);
        view.Measure(new Size(800, 2000));
        return view;
    }

    [Fact]
    public async Task FindNext_LocatesAndSelectsTextThatOnlyOccursInTheHeader()
    {
        var found = false;
        string? selected = null;
        var ran = await OnUiThread(() =>
        {
            var view = BuildViewWithHeaderAndBody("Confidential Draft", "nothing relevant here");

            found = view.FindNext("Confidential");
            selected = view.SelectedText;
        });
        if (!ran) return;

        found.Should().BeTrue("the header contains the search term and must be found, matching Word");
        selected.Should().Be("Confidential");
    }

    [Fact]
    public async Task FindNext_LocatesAndSelectsTextThatOnlyOccursInTheFooter()
    {
        var found = false;
        string? selected = null;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            doc.Blocks.Add(new Paragraph("nothing relevant here"));
            doc.Footer = new HeaderFooter("Confidential Draft");

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));

            found = view.FindNext("Confidential");
            selected = view.SelectedText;
        });
        if (!ran) return;

        found.Should().BeTrue("the footer contains the search term and must be found, matching Word");
        selected.Should().Be("Confidential");
    }

    /// <summary>
    /// r176 remediation. Making headers reachable turned a harmless no-op into data corruption. A
    /// header/footer match has no resume position -- the planner rescans each paragraph from offset 0
    /// and justifies that by assuming "each replacement removes that occurrence". That assumption
    /// fails whenever the replacement CONTAINS the search term: the term is recreated at the same
    /// offset, the next find returns the identical hit, and Replace All runs to its 10000-iteration
    /// cap rewriting the header into thousands of characters of garbage.
    ///
    /// The existing Replace All coverage could not catch this: it replaces "cat" with "dog", a
    /// replacement deliberately not containing the search term.
    /// </summary>
    [Fact]
    public async Task ReplaceAll_WhenTheReplacementContainsTheSearchTerm_ReplacesOnceAndStops()
    {
        var count = -1;
        string? headerText = null;
        var ran = await OnUiThread(() =>
        {
            var view = BuildViewWithHeaderAndBody("Confidential Draft", "nothing relevant here");

            count = view.ReplaceAll("Confidential", "Strictly Confidential");

            headerText = view.Document.Header!.Paragraphs[0].PlainText;
        });
        if (!ran) return;

        count.Should().Be(
            1,
            "there is one occurrence, so exactly one replacement -- rescanning from offset 0 re-finds the " +
            "term inside its own replacement, which is what ran away");
        headerText.Should().Be(
            "Strictly Confidential Draft",
            "the header must read as the user intended, not accumulate the replacement thousands of times");
    }
    [Fact]
    public async Task ReplaceAll_ReplacesTextInTheHeaderAndCountsIt()
    {
        var count = -1;
        string? headerText = null;
        var ran = await OnUiThread(() =>
        {
            var view = BuildViewWithHeaderAndBody("Confidential Draft", "nothing relevant here");

            count = view.ReplaceAll(
                "Confidential",
                "Approved",
                new FindReplaceSearchOptions(MatchCase: false, WholeWord: false, UseWildcards: false));

            headerText = view.Document.Header!.Paragraphs[0].PlainText;
        });
        if (!ran) return;

        count.Should().Be(1, "Replace All must count the header occurrence, not silently skip it");
        headerText.Should().Be("Approved Draft");
    }

    [Fact]
    public async Task ReplaceAll_ReplacesMultipleHeaderOnlyOccurrencesWithoutCrashing()
    {
        // Exercises ReplaceAllCore's loop across MULTIPLE header-only matches: each header hit leaves
        // _selectionAnchor null (it uses the separate _hfCaret/_hfSelectionAnchor model instead), so this
        // proves the loop's restrict-to-selection/limit bookkeeping tolerates that across repeated
        // iterations rather than just the single-match case above.
        var count = -1;
        string? headerText = null;
        var ran = await OnUiThread(() =>
        {
            var view = BuildViewWithHeaderAndBody("cat sat, cat ran", "nothing relevant here");

            count = view.ReplaceAll(
                "cat",
                "dog",
                new FindReplaceSearchOptions(MatchCase: false, WholeWord: false, UseWildcards: false));

            headerText = view.Document.Header!.Paragraphs[0].PlainText;
        });
        if (!ran) return;

        count.Should().Be(2);
        headerText.Should().Be("dog sat, dog ran");
    }

    [Fact]
    public async Task FindNext_StillPrefersABodyMatchOverAHeaderMatchWhenBothExist()
    {
        // Sibling/non-regression coverage: the header/footer fallback must only kick in once the body has
        // been searched with no hit -- an ordinary body match must keep winning, and the ordinary body
        // caret/selection model (not the header/footer one) must be the one that ends up active.
        var found = false;
        string? selected = null;
        var ran = await OnUiThread(() =>
        {
            var view = BuildViewWithHeaderAndBody("find in the header too", "find the body match here");

            found = view.FindNext("find");
            selected = view.SelectedText;
        });
        if (!ran) return;

        found.Should().BeTrue();
        selected.Should().Be("find");
    }

    [Fact]
    public async Task FindNext_StillReturnsFalseWhenTermIsNowhereInTheBodyOrHeader()
    {
        // Sibling/non-regression coverage: a document with header content that just doesn't contain the
        // term must still report "not found", not a phantom header hit.
        var found = true;
        var ran = await OnUiThread(() =>
        {
            var view = BuildViewWithHeaderAndBody("also nothing relevant", "nothing relevant here");

            found = view.FindNext("MAGICWORD");
        });
        if (!ran) return;

        found.Should().BeFalse();
    }
}
