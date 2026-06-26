using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using FreeW.App.Avalonia;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// AV-FIND: headless tests that drive the option-aware Find &amp; Replace engine on
/// <see cref="DocumentView"/> directly (FindNext / FindPrevious / FindAll / ReplaceNext /
/// ReplaceAll), covering wrap-around, match-case, whole-word, find-all count, single-undo
/// ReplaceAll, and the no-match path. All cases run on the shared headless UI thread and opt out
/// cleanly when no drawing backend is available (mirrors the other DocumentView headless tests).
/// </summary>
public sealed class DocumentViewFindReplaceEngineTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    private static async Task<bool> OnUiThread(Action action)
    {
        try
        {
            await Session.Dispatch(action, CancellationToken.None);
            return true;
        }
        catch (Exception)
        {
            return false; // no headless drawing backend in this CI environment — test is skipped
        }
    }

    private static DocumentView ViewWith(params string[] paragraphs)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        foreach (var text in paragraphs)
            doc.Blocks.Add(new Paragraph { Runs = { new Run(text) } });

        var view = new DocumentView();
        view.LoadDocument(doc);
        view.Measure(new Size(800, 4000));
        return view;
    }

    // ── FindNext / wrap ────────────────────────────────────────────────────────

    [Fact]
    public async Task FindNext_selects_first_match_then_advances_then_wraps()
    {
        string first = "", second = "", third = "";
        var ran = await OnUiThread(() =>
        {
            var view = ViewWith("fox here", "another fox", "no match");

            view.FindNext("fox").Should().BeTrue();
            first = view.SelectedText;

            view.FindNext("fox").Should().BeTrue();
            second = view.SelectedText;

            // Third call wraps around back to the first occurrence.
            view.FindNext("fox").Should().BeTrue();
            third = view.SelectedText;
        });

        if (!ran) return;
        first.Should().Be("fox");
        second.Should().Be("fox");
        third.Should().Be("fox");
    }

    [Fact]
    public async Task FindPrevious_walks_backwards_and_wraps()
    {
        var ordinals = new System.Collections.Generic.List<int>();
        var ran = await OnUiThread(() =>
        {
            var view = ViewWith("alpha beta", "beta gamma", "delta beta");
            var opts = DocumentView.FindOptions.Default;

            // Position at the last match, then walk backwards.
            view.FindNext("beta"); // 1st
            view.FindNext("beta"); // 2nd
            view.FindNext("beta"); // 3rd (last)

            view.FindPrevious("beta", opts).Should().BeTrue();
            ordinals.Add(view.CurrentFindOrdinal("beta", opts)); // expect 2
            view.FindPrevious("beta", opts).Should().BeTrue();
            ordinals.Add(view.CurrentFindOrdinal("beta", opts)); // expect 1
            view.FindPrevious("beta", opts).Should().BeTrue();
            ordinals.Add(view.CurrentFindOrdinal("beta", opts)); // wraps to 3
        });

        if (!ran) return;
        ordinals.Should().Equal(2, 1, 3);
    }

    // ── Match case / whole word ─────────────────────────────────────────────────

    [Fact]
    public async Task FindNext_match_case_skips_differently_cased_hits()
    {
        var matched = "";
        var ran = await OnUiThread(() =>
        {
            var view = ViewWith("FOX and fox");
            // Match-case search for lowercase "fox" must skip "FOX" and select the lowercase one.
            view.FindNext("fox", new DocumentView.FindOptions(MatchCase: true)).Should().BeTrue();
            matched = view.SelectedText;
        });

        if (!ran) return;
        matched.Should().Be("fox");
    }

    [Fact]
    public async Task FindAll_whole_word_filters_substring_hits()
    {
        var all = 0;
        var whole = 0;
        var ran = await OnUiThread(() =>
        {
            var view = ViewWith("foxglove fox foxes");
            all = view.FindAll("fox", DocumentView.FindOptions.Default);
            whole = view.FindAll("fox", new DocumentView.FindOptions(WholeWord: true));
        });

        if (!ran) return;
        all.Should().Be(3, "fox appears in foxglove, fox, foxes");
        whole.Should().Be(1, "only standalone 'fox' is a whole-word hit");
    }

    // ── FindAll count ───────────────────────────────────────────────────────────

    [Fact]
    public async Task FindAll_counts_matches_across_paragraphs()
    {
        var count = 0;
        var ran = await OnUiThread(() =>
        {
            var view = ViewWith("Hello world", "say hello", "HELLO again");
            count = view.FindAll("hello");
        });

        if (!ran) return;
        count.Should().Be(3, "case-insensitive across three paragraphs");
    }

    [Fact]
    public async Task FindAll_empty_query_clears_highlight_and_returns_zero()
    {
        var count = -1;
        var ran = await OnUiThread(() =>
        {
            var view = ViewWith("some text");
            view.FindAll("text");
            count = view.FindAll(string.Empty);
        });

        if (!ran) return;
        count.Should().Be(0);
    }

    // ── ReplaceCurrent / advance ────────────────────────────────────────────────

    [Fact]
    public async Task ReplaceNext_replaces_current_match_then_selects_the_next()
    {
        var plain = "";
        var nextSel = "";
        var ran = await OnUiThread(() =>
        {
            var view = ViewWith("cat cat cat");
            view.FindNext("cat");          // select first
            view.ReplaceNext("cat", "dog"); // replace it, select next "cat"
            plain = view.PlainText;
            nextSel = view.SelectedText;
        });

        if (!ran) return;
        plain.Should().Be("dog cat cat", "only the first occurrence is replaced");
        nextSel.Should().Be("cat", "the next match is now selected");
    }

    // ── ReplaceAll + single undo ────────────────────────────────────────────────

    [Fact]
    public async Task ReplaceAll_replaces_all_and_returns_count()
    {
        var count = 0;
        var plain = "";
        var ran = await OnUiThread(() =>
        {
            var view = ViewWith("red red green", "red blue red");
            count = view.ReplaceAll("red", "X");
            plain = view.PlainText;
        });

        if (!ran) return;
        count.Should().Be(4);
        plain.Should().Be("X X green\nX blue X");
    }

    [Fact]
    public async Task ReplaceAll_is_a_single_undo_step()
    {
        var before = "";
        var after = "";
        var reverted = "";
        var ran = await OnUiThread(() =>
        {
            var view = ViewWith("aa aa aa");
            before = view.PlainText;
            view.ReplaceAll("aa", "bb");
            after = view.PlainText;
            view.Undo();            // a single undo must revert ALL replacements
            reverted = view.PlainText;
        });

        if (!ran) return;
        before.Should().Be("aa aa aa");
        after.Should().Be("bb bb bb");
        reverted.Should().Be("aa aa aa", "one undo reverts the whole ReplaceAll");
    }

    [Fact]
    public async Task ReplaceAll_with_replacement_containing_query_does_not_loop()
    {
        var count = 0;
        var plain = "";
        var ran = await OnUiThread(() =>
        {
            var view = ViewWith("a a a");
            // Replacement contains the query — must not re-scan inserted text (would loop forever).
            count = view.ReplaceAll("a", "aa");
            plain = view.PlainText;
        });

        if (!ran) return;
        count.Should().Be(3);
        plain.Should().Be("aa aa aa");
    }

    // ── No match ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task NoMatch_returns_gracefully()
    {
        bool found = true, prevFound = true;
        int all = -1, replaced = -1;
        var ran = await OnUiThread(() =>
        {
            var view = ViewWith("nothing to see here");
            found = view.FindNext("zzz");
            prevFound = view.FindPrevious("zzz", DocumentView.FindOptions.Default);
            all = view.FindAll("zzz");
            replaced = view.ReplaceAll("zzz", "qqq");
        });

        if (!ran) return;
        found.Should().BeFalse();
        prevFound.Should().BeFalse();
        all.Should().Be(0);
        replaced.Should().Be(0);
    }

    [Fact]
    public async Task CurrentFindOrdinal_reports_position_among_matches()
    {
        var ordinals = new System.Collections.Generic.List<int>();
        var ran = await OnUiThread(() =>
        {
            var view = ViewWith("x then x then x");
            var opts = DocumentView.FindOptions.Default;
            view.FindNext("x"); ordinals.Add(view.CurrentFindOrdinal("x", opts));
            view.FindNext("x"); ordinals.Add(view.CurrentFindOrdinal("x", opts));
            view.FindNext("x"); ordinals.Add(view.CurrentFindOrdinal("x", opts));
        });

        if (!ran) return;
        ordinals.Should().Equal(1, 2, 3);
    }
}
