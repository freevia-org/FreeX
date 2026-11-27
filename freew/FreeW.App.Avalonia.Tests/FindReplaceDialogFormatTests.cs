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
/// The Avalonia Find &amp; Replace dialog has the WPF dialog's Format / No Formatting buttons and format
/// summary lines, and Find Next / Replace / Replace All honor Find what and Replace with formatting through
/// the shared planners.
/// </summary>
public sealed class FindReplaceDialogFormatTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    private static Task<bool> OnUiThread(Action action) => HeadlessUiThread.Run(action);

    private static DocumentView ViewWithMixedCats()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("plain cat then "));
        paragraph.Runs.Add(new Run("bold cat", new RunFormatting { Bold = true }));
        document.Blocks.Add(paragraph);
        var view = new DocumentView();
        view.LoadDocument(document);
        view.Measure(new Size(800, 2000));
        return view;
    }

    private static string BodyText(DocumentView view) => ((Paragraph)view.Document.Blocks[0]).PlainText;

    [Fact]
    public async Task Dialog_renders_the_format_buttons_with_the_wpf_automation_ids_and_a_summary_per_box()
    {
        IReadOnlyList<string> ids = [];
        string summary = "unset", clearedSummary = "unset", replaceSummary = "unset";
        var ran = await OnUiThread(() =>
        {
            var dialog = new FindReplaceDialog(ViewWithMixedCats(), FindReplaceOpenMode.Find);
            try
            {
                ids = dialog.ToolButtonAutomationIdsForTest;
                dialog.SetFormatChooserForTest((_, _) => Task.FromResult<FindFormat?>(new FindFormat { Bold = true }));
                dialog.ChooseFormatForTest(FindReplaceDialogFieldKind.Find).GetAwaiter().GetResult();
                summary = dialog.FindFormatSummaryForTest;
                replaceSummary = dialog.ReplaceFormatSummaryForTest;
                dialog.ClearFormatForTest(FindReplaceDialogFieldKind.Find);
                clearedSummary = dialog.FindFormatSummaryForTest;
            }
            finally
            {
                dialog.Close();
            }
        });
        ran.Should().BeTrue();

        ids.Should().Contain(FindReplaceDialogPlanner.FormatButtonAutomationId);
        ids.Should().Contain(FindReplaceDialogPlanner.NoFormattingButtonAutomationId);
        summary.Should().Be(FindReplaceDialogPlanner.DescribeFormat(new FindFormat { Bold = true }));
        summary.Should().NotBeEmpty();
        replaceSummary.Should().BeEmpty("the format was set for Find what only");
        clearedSummary.Should().BeEmpty("No Formatting clears it");
    }

    [Fact]
    public async Task Cancelling_the_format_dialog_leaves_the_formatting_unchanged()
    {
        string summary = "unset";
        var ran = await OnUiThread(() =>
        {
            var dialog = new FindReplaceDialog(ViewWithMixedCats(), FindReplaceOpenMode.Find);
            try
            {
                dialog.SetFormatChooserForTest((_, _) => Task.FromResult<FindFormat?>(null));
                dialog.ChooseFormatForTest(FindReplaceDialogFieldKind.Find).GetAwaiter().GetResult();
                summary = dialog.FindFormatSummaryForTest;
            }
            finally
            {
                dialog.Close();
            }
        });
        ran.Should().BeTrue();
        summary.Should().BeEmpty();
    }

    [Fact]
    public async Task Find_with_bold_criteria_skips_the_plain_match_and_Replace_replaces_the_bold_one()
    {
        string? afterFind = null, afterReplace = null, status = null;
        var ran = await OnUiThread(() =>
        {
            var view = ViewWithMixedCats();
            var dialog = new FindReplaceDialog(view, FindReplaceOpenMode.Replace);
            try
            {
                dialog.SetFormatChooserForTest((_, _) => Task.FromResult<FindFormat?>(new FindFormat { Bold = true }));
                dialog.ChooseFormatForTest(FindReplaceDialogFieldKind.Find).GetAwaiter().GetResult();
                dialog.SetFindTextForTest("cat");
                dialog.SetReplaceTextForTest("dog");

                dialog.FindNextForTest();
                afterFind = view.SelectedText;
                dialog.ReplaceForTest();
                afterReplace = BodyText(view);
                status = dialog.StatusForTest;
            }
            finally
            {
                dialog.Close();
            }
        });
        ran.Should().BeTrue();

        afterFind.Should().Be("cat");
        afterReplace.Should().Be("plain cat then bold dog");
        status.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Formatting_only_search_finds_the_formatted_stretch_with_an_empty_find_box()
    {
        string? selected = null;
        var ran = await OnUiThread(() =>
        {
            var view = ViewWithMixedCats();
            var dialog = new FindReplaceDialog(view, FindReplaceOpenMode.Find);
            try
            {
                dialog.SetFormatChooserForTest((_, _) => Task.FromResult<FindFormat?>(new FindFormat { Bold = true }));
                dialog.ChooseFormatForTest(FindReplaceDialogFieldKind.Find).GetAwaiter().GetResult();
                dialog.FindNextForTest();
                selected = view.SelectedText;
            }
            finally
            {
                dialog.Close();
            }
        });
        ran.Should().BeTrue();
        selected.Should().Be("bold cat");
    }

    [Fact]
    public async Task ReplaceAll_with_replace_formatting_applies_it_to_the_replacement()
    {
        string? text = null;
        bool[] boldRuns = [];
        string[] runTexts = [];
        var ran = await OnUiThread(() =>
        {
            var view = ViewWithMixedCats();
            var dialog = new FindReplaceDialog(view, FindReplaceOpenMode.Replace);
            try
            {
                dialog.SetFormatChooserForTest((_, _) => Task.FromResult<FindFormat?>(new FindFormat { Bold = true }));
                dialog.ChooseFormatForTest(FindReplaceDialogFieldKind.Replace).GetAwaiter().GetResult();
                dialog.SetFindTextForTest("plain cat");
                dialog.SetReplaceTextForTest("dog");
                dialog.ReplaceAllForTest();

                var paragraph = (Paragraph)view.Document.Blocks[0];
                text = paragraph.PlainText;
                runTexts = paragraph.Runs.Select(run => run.Text).ToArray();
                boldRuns = paragraph.Runs.Select(run => run.Formatting.Bold).ToArray();
            }
            finally
            {
                dialog.Close();
            }
        });
        ran.Should().BeTrue();

        text.Should().Be("dog then bold cat");
        var dog = Array.FindIndex(runTexts, run => run.Contains("dog"));
        dog.Should().BeGreaterThanOrEqualTo(0);
        boldRuns[dog].Should().BeTrue("Replace with formatting makes the replacement bold");
        runTexts[dog].Should().Be("dog");
    }

    [Fact]
    public async Task Replace_with_an_empty_box_and_formatting_keeps_the_found_text_and_only_reformats_it()
    {
        string? text = null;
        string[] runTexts = [];
        bool[] boldRuns = [];
        var ran = await OnUiThread(() =>
        {
            var view = ViewWithMixedCats();
            var dialog = new FindReplaceDialog(view, FindReplaceOpenMode.Replace);
            try
            {
                dialog.SetFormatChooserForTest((_, _) => Task.FromResult<FindFormat?>(new FindFormat { Bold = true }));
                dialog.ChooseFormatForTest(FindReplaceDialogFieldKind.Replace).GetAwaiter().GetResult();
                dialog.SetFindTextForTest("plain cat");
                dialog.FindNextForTest();
                dialog.ReplaceForTest();

                var paragraph = (Paragraph)view.Document.Blocks[0];
                text = paragraph.PlainText;
                runTexts = paragraph.Runs.Select(run => run.Text).ToArray();
                boldRuns = paragraph.Runs.Select(run => run.Formatting.Bold).ToArray();
            }
            finally
            {
                dialog.Close();
            }
        });
        ran.Should().BeTrue();

        text.Should().Be("plain cat then bold cat");
        var reformatted = Array.FindIndex(runTexts, run => run == "plain cat");
        reformatted.Should().BeGreaterThanOrEqualTo(0);
        boldRuns[reformatted].Should().BeTrue();
    }
}
