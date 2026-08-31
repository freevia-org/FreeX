using Free.Shared.AppServices;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.Dialogs;
using System.Linq;
using FreeW.Core.Model;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Round 179. WPF Replace All searched ONLY the body: its TryFind walks TextPointers over
/// editor.Document, the RichTextBox body FlowDocument, and headers/footers are not in it -- they
/// live in the model and are edited through a separate sub-editor the dialog is never handed. So a
/// term appearing in a page header was silently left unreplaced and the count reported back to the
/// user was short by that many. The Avalonia shell got this in r177; WPF kept the original gap.
/// </summary>
public sealed class Round179_WpfReplaceAllHeaderFooterTests
{
    private static DocumentView BuildView(string body, string? header, string? footer)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph(body));
        if (header is not null)
            document.Header = new HeaderFooter(header);
        if (footer is not null)
            document.Footer = new HeaderFooter(footer);

        var view = new DocumentView();
        view.LoadModel(document);
        return view;
    }

    private static int RunReplaceAll(DocumentView view, string term, string replacement)
    {
        var dialog = new FindReplaceDialog(null!, view, FindReplaceOpenMode.Replace);
        try
        {
            dialog.Show();
            dialog.SetFindTextForTest(term);
            dialog.SetReplaceTextForTest(replacement);
            dialog.ReplaceAllForTest();
        }
        finally
        {
            dialog.Close();
        }

        view.CommitToModel();
        return 0;
    }

    [StaFact]
    public void ReplaceAll_ReplacesInTheHeaderAndFooterAsWellAsTheBody()
    {
        var view = BuildView("body mentions Draft as well", "Draft header", "Draft footer");

        RunReplaceAll(view, "Draft", "Final");

        ((Paragraph)view.Model.Blocks[0]).PlainText.Should().Be("body mentions Final as well");
        view.Model.Header!.Paragraphs[0].PlainText.Should().Be(
            "Final header", "the header must not be skipped just because the search walks the body");
        view.Model.Footer!.Paragraphs[0].PlainText.Should().Be("Final footer", "nor the footer");
    }

    [StaFact]
    public void ReplaceAll_WhenTheReplacementContainsTheSearchTerm_ReplacesOncePerSlot()
    {
        // The resume position is what stops this: rescanning the slot from offset 0 would re-find
        // the term inside the text just written and run to the iteration cap.
        var view = BuildView("nothing relevant here", "Confidential header", "Confidential footer");

        RunReplaceAll(view, "Confidential", "Strictly Confidential");

        view.Model.Header!.Paragraphs[0].PlainText.Should().Be("Strictly Confidential header");
        view.Model.Footer!.Paragraphs[0].PlainText.Should().Be("Strictly Confidential footer");
    }

    [StaFact]
    public void ReplaceAll_RestrictedToASelection_LeavesTheHeaderAlone()
    {
        // A selection is a body concept; reaching outside it was the r178 bug on the Avalonia side.
        var view = BuildView("Draft one", "Draft header", null);
        view.SetSelectionRangeForTest(0, 0, 0, "Draft one".Length);

        RunReplaceAll(view, "Draft", "Final");

        view.Model.Header!.Paragraphs[0].PlainText.Should().Be(
            "Draft header", "the header lies far outside the user's selection");
    }

    [StaFact]
    public void ReplaceAll_KeepsThePageNumberFieldInAFooterItEdits()
    {
        // The ordinary Word footer: literal text plus PAGE and NUMPAGES field runs. r179 rebuilt the
        // whole paragraph into one run, which froze both fields into literal text the moment any
        // Replace All touched the footer -- even though the match never went near them.
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("body"));

        var footer = new HeaderFooter(string.Empty);
        var paragraph = footer.Paragraphs[0];
        paragraph.Runs.Clear();
        paragraph.Runs.Add(new Run("Draft - Page "));
        paragraph.Runs.Add(new Run("3") { FieldKind = RunFieldKind.PageNumber });
        paragraph.Runs.Add(new Run(" of "));
        paragraph.Runs.Add(new Run("7") { FieldKind = RunFieldKind.NumPages });
        document.Footer = footer;

        var view = new DocumentView();
        view.LoadModel(document);

        RunReplaceAll(view, "Draft", "Final");

        var runs = view.Model.Footer!.Paragraphs[0].Runs;
        runs.Should().Contain(run => run.FieldKind == RunFieldKind.PageNumber,
            "the page-number field must survive a replacement elsewhere in the same footer");
        runs.Should().Contain(run => run.FieldKind == RunFieldKind.NumPages);
        view.Model.Footer!.Paragraphs[0].PlainText.Should().Be("Final - Page 3 of 7");
    }

    [StaFact]
    public void ReplaceAll_KeepsPerRunFormattingOnRunsItDidNotTouch()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("body"));

        var header = new HeaderFooter(string.Empty);
        var paragraph = header.Paragraphs[0];
        paragraph.Runs.Clear();
        paragraph.Runs.Add(new Run("Draft ") { Formatting = RunFormatting.Default with { Bold = true } });
        paragraph.Runs.Add(new Run("subtitle") { Formatting = RunFormatting.Default with { Italic = true } });
        document.Header = header;

        var view = new DocumentView();
        view.LoadModel(document);

        RunReplaceAll(view, "Draft", "Final");

        var runs = view.Model.Header!.Paragraphs[0].Runs;
        runs.Should().Contain(run => run.Text.Contains("subtitle") && run.Formatting.Italic == true,
            "the untouched run must keep its own formatting rather than inherit the matched run's");
        view.Model.Header!.Paragraphs[0].PlainText.Should().Be("Final subtitle");
    }

    [StaFact]
    public void ReplaceAll_KeepsAnInlineImageRunInAHeaderItEdits()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("body"));

        var header = new HeaderFooter(string.Empty);
        var paragraph = header.Paragraphs[0];
        paragraph.Runs.Clear();
        paragraph.Runs.Add(new Run("Draft logo:"));
        paragraph.Runs.Add(new Run(string.Empty) { Image = new InlineImage(new byte[] { 1, 2, 3 }, 32, 32) });
        document.Header = header;

        var view = new DocumentView();
        view.LoadModel(document);

        RunReplaceAll(view, "Draft", "Final");

        view.Model.Header!.Paragraphs[0].Runs.Any(run => run.Image != null).Should().BeTrue(
            "an image run contributes no characters to the match and must not be deleted by it");
    }

    [StaFact]
    public void ReplaceAll_WithTrackChangesOn_RecordsTheHeaderEditAsARevision()
    {
        // The body loop records Replace All as a tracked revision when Track Changes is on. The
        // header/footer pass added in r179 rewrote that text untracked, hiding the change from the
        // reviewer Track Changes was enabled for.
        var view = BuildView("body", "Draft header", null);
        view.RevisionAuthor = "Ada Reviewer";
        view.TrackChangesEnabled = true;

        RunReplaceAll(view, "Draft", "Final");

        var runs = view.Model.Header!.Paragraphs[0].Runs;
        var inserted = runs.Where(run => run.Revision == RevisionKind.Inserted).ToList();
        var deleted = runs.Where(run => run.Revision == RevisionKind.Deleted).ToList();

        inserted.Should().ContainSingle(run => run.Text == "Final",
            "the replacement must be recorded as an insertion, not written in silently");
        inserted[0].RevisionAuthor.Should().Be("Ada Reviewer");
        deleted.Should().ContainSingle(run => run.Text == "Draft",
            "and the original must be kept struck through so the reviewer can see what changed");
    }
}
