using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using FluentAssertions;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;
using Free.Shared.AppServices;
using Xunit;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// The Avalonia Find &amp; Replace dialog renders every option of the shared
/// <see cref="FindReplaceDialogPlanner.Surface"/> (the same seven the WPF dialog offers) and Find Next /
/// Replace / Replace All honor each of them through the shared search engine.
/// </summary>
public sealed class FindReplaceDialogOptionsTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    private static Task<bool> OnUiThread(Action action) => HeadlessUiThread.Run(action);

    private static DocumentView ViewWith(string text)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph(text));
        var view = new DocumentView();
        view.LoadDocument(document);
        view.Measure(new Size(800, 2000));
        return view;
    }

    private static string BodyText(DocumentView view) => ((Paragraph)view.Document.Blocks[0]).PlainText;

    [Fact]
    public async Task Dialog_renders_every_surface_option_with_the_shared_labels_and_automation_ids()
    {
        IReadOnlyList<string> labels = [];
        IReadOnlyList<string> ids = [];
        var ran = await OnUiThread(() =>
        {
            var dialog = new FindReplaceDialog(new DocumentView(), FindReplaceOpenMode.Find);
            try
            {
                labels = dialog.OptionLabelsForTest;
                ids = dialog.OptionAutomationIdsForTest;
            }
            finally
            {
                dialog.Close();
            }
        });
        ran.Should().BeTrue();

        var text = new FindReplaceDialogText(null);
        labels.Should().Equal(FindReplaceDialogPlanner.Surface.Options
            .Select(option => FindReplaceDialogPlanner.LabelFor(option.Kind, text)));
        ids.Should().Equal(FindReplaceDialogPlanner.Surface.Options.Select(option => option.AutomationId));
        labels.Should().HaveCount(7);
    }

    [Fact]
    public async Task Use_wildcards_disables_and_unchecks_every_option_but_match_case()
    {
        var enabled = new Dictionary<FindReplaceOptionKind, bool>();
        var enabledAfterOff = new Dictionary<FindReplaceOptionKind, bool>();
        var ran = await OnUiThread(() =>
        {
            var dialog = new FindReplaceDialog(new DocumentView(), FindReplaceOpenMode.Find);
            try
            {
                dialog.SetOptionForTest(FindReplaceOptionKind.MatchPrefix, true);
                dialog.SetOptionForTest(FindReplaceOptionKind.IgnoreWhiteSpace, true);
                dialog.SetOptionForTest(FindReplaceOptionKind.UseWildcards, true);
                foreach (var option in FindReplaceDialogPlanner.Surface.Options)
                    enabled[option.Kind] = dialog.IsOptionEnabledForTest(option.Kind);

                dialog.SetOptionForTest(FindReplaceOptionKind.UseWildcards, false);
                foreach (var option in FindReplaceDialogPlanner.Surface.Options)
                    enabledAfterOff[option.Kind] = dialog.IsOptionEnabledForTest(option.Kind);
            }
            finally
            {
                dialog.Close();
            }
        });
        ran.Should().BeTrue();

        enabled.Where(pair => pair.Key is not (FindReplaceOptionKind.MatchCase or FindReplaceOptionKind.UseWildcards))
            .Should().OnlyContain(pair => !pair.Value);
        enabled[FindReplaceOptionKind.MatchCase].Should().BeTrue();
        enabledAfterOff.Values.Should().OnlyContain(value => value);
    }

    [Fact]
    public async Task Match_prefix_skips_a_mid_word_hit()
    {
        string? selected = null;
        (int Block, int Offset) caret = default;
        var ran = await OnUiThread(() =>
        {
            var view = ViewWith("winter interact");
            var dialog = new FindReplaceDialog(view, FindReplaceOpenMode.Find);
            try
            {
                dialog.SetFindTextForTest("inter");
                dialog.SetOptionForTest(FindReplaceOptionKind.MatchPrefix, true);
                dialog.FindNextForTest();
                selected = view.SelectedText;
                caret = view.CaretPositionForTest;
            }
            finally
            {
                dialog.Close();
            }
        });
        ran.Should().BeTrue();

        selected.Should().Be("inter");
        caret.Should().Be((0, 12), "only the word-initial 'inter' of 'interact' matches as a prefix");
    }

    [Fact]
    public async Task Match_suffix_skips_a_word_initial_hit()
    {
        (int Block, int Offset) caret = default;
        var ran = await OnUiThread(() =>
        {
            var view = ViewWith("interact winter");
            var dialog = new FindReplaceDialog(view, FindReplaceOpenMode.Find);
            try
            {
                dialog.SetFindTextForTest("inter");
                dialog.SetOptionForTest(FindReplaceOptionKind.MatchSuffix, true);
                dialog.FindNextForTest();
                caret = view.CaretPositionForTest;
            }
            finally
            {
                dialog.Close();
            }
        });
        ran.Should().BeTrue();

        caret.Should().Be((0, 15), "only the word-final 'inter' of 'winter' matches as a suffix");
    }

    [Fact]
    public async Task Ignore_punctuation_finds_and_replaces_across_punctuation()
    {
        string? selected = null;
        string? afterReplaceAll = null;
        var ran = await OnUiThread(() =>
        {
            var view = ViewWith("it's here");
            var dialog = new FindReplaceDialog(view, FindReplaceOpenMode.Find);
            try
            {
                dialog.SetFindTextForTest("its");
                dialog.SetOptionForTest(FindReplaceOptionKind.IgnorePunctuation, true);
                dialog.FindNextForTest();
                selected = view.SelectedText;

                dialog.SetReplaceTextForTest("X");
                dialog.ReplaceAllForTest();
                afterReplaceAll = BodyText(view);
            }
            finally
            {
                dialog.Close();
            }
        });
        ran.Should().BeTrue();

        selected.Should().Be("it's");
        afterReplaceAll.Should().Be("X here");
    }

    [Fact]
    public async Task Ignore_whitespace_finds_and_replaces_across_spaces()
    {
        string? selected = null;
        string? afterReplace = null;
        var ran = await OnUiThread(() =>
        {
            var view = ViewWith("data base");
            var dialog = new FindReplaceDialog(view, FindReplaceOpenMode.Find);
            try
            {
                dialog.SetFindTextForTest("database");
                dialog.SetOptionForTest(FindReplaceOptionKind.IgnoreWhiteSpace, true);
                dialog.FindNextForTest();
                selected = view.SelectedText;

                dialog.SetReplaceTextForTest("DB");
                dialog.ReplaceForTest();
                afterReplace = BodyText(view);
            }
            finally
            {
                dialog.Close();
            }
        });
        ran.Should().BeTrue();

        selected.Should().Be("data base");
        afterReplace.Should().Be("DB");
    }

    [Fact]
    public async Task Without_the_options_the_same_searches_do_not_match()
    {
        var statuses = new List<string>();
        var ran = await OnUiThread(() =>
        {
            var view = ViewWith("it's data base");
            var dialog = new FindReplaceDialog(view, FindReplaceOpenMode.Find);
            try
            {
                dialog.SetFindTextForTest("its");
                dialog.FindNextForTest();
                statuses.Add(dialog.StatusForTest);
                dialog.SetFindTextForTest("database");
                dialog.FindNextForTest();
                statuses.Add(dialog.StatusForTest);
            }
            finally
            {
                dialog.Close();
            }
        });
        ran.Should().BeTrue();

        statuses.Should().OnlyContain(status => status.Contains("not found", StringComparison.OrdinalIgnoreCase));
    }
}
