using Free.Shared.AppServices;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.Dialogs;
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
}
