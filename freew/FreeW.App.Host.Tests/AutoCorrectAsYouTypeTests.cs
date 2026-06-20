using System.Linq;
using System.Windows.Documents;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Host.Tests;

/// <summary>
/// End-to-end coverage for the Word-"AutoCorrect"-tab rules inside the live editor: type characters through
/// the same path as real keystrokes (<see cref="DocumentView.SimulateTypeText"/>) and assert the produced
/// text. The pure transform decisions are covered exhaustively in
/// <c>FreeW.Core.Model.Tests.AutoCorrectEngineTests</c>; these verify the editor actually applies them via
/// the shared delete-back/insert path and that the per-rule toggles flow through. Runs on STA because the
/// RichTextBox needs it.
/// </summary>
public sealed class AutoCorrectAsYouTypeTests
{
    private static DocumentView NewEditor(AutoCorrectOptions? autoCorrect = null, AutoFormatOptions? autoFormat = null)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph());
        var view = new DocumentView
        {
            AutoCorrectOptions = autoCorrect ?? AutoCorrectOptions.Default,
            // Default AutoFormat sentence-capitalization is left on; tests that care lead with text so the
            // word under test is not at a sentence start.
            AutoFormatOptions = autoFormat ?? AutoFormatOptions.Default,
        };
        view.LoadModel(doc);
        view.CaretPosition = view.Document.ContentStart.GetInsertionPosition(LogicalDirection.Forward)
            ?? view.Document.ContentStart;
        return view;
    }

    [StaFact]
    public void ReplaceTable_FixesTypoWhileTyping()
    {
        var view = NewEditor();
        // Lead with "I " so the typo is mid-sentence (no AutoFormat capitalization interferes).
        view.SimulateTypeText("I teh ");
        view.CommitToModel();

        view.Model.PlainText.Should().Be("I the ");
    }

    [StaFact]
    public void ReplaceTable_GlyphEntry_AppliedWhileTyping()
    {
        // Symbols-as-text rule on the AutoFormat tab is independent; here we drive the AutoCorrect table
        // entry "(c)" → © by typing the closing paren (the table key includes it).
        var view = NewEditor(autoFormat: AutoFormatOptions.AllOff);
        view.SimulateTypeText("(c) ");
        view.CommitToModel();

        view.Model.PlainText.Should().StartWith("©");
    }

    [StaFact]
    public void TwoInitialCaps_CorrectedWhileTyping()
    {
        var view = NewEditor(autoFormat: AutoFormatOptions.AllOff);
        view.SimulateTypeText("TWo ");
        view.CommitToModel();

        view.Model.PlainText.Should().Be("Two ");
    }

    [StaFact]
    public void DayName_CapitalizedWhileTyping()
    {
        var view = NewEditor(autoFormat: AutoFormatOptions.AllOff);
        view.SimulateTypeText("monday ");
        view.CommitToModel();

        view.Model.PlainText.Should().Be("Monday ");
    }

    [StaFact]
    public void DisabledReplaceText_LeavesTypoVerbatim()
    {
        var opts = AutoCorrectOptions.Default;
        opts.ReplaceText = false;
        var view = NewEditor(opts, AutoFormatOptions.AllOff);
        view.SimulateTypeText("teh ");
        view.CommitToModel();

        view.Model.PlainText.Should().Be("teh ");
    }

    [StaFact]
    public void DisabledMaster_SuppressesAutoCorrectToo()
    {
        var view = NewEditor(autoFormat: AutoFormatOptions.AllOff);
        view.AutoCorrectEnabled = false;
        view.SimulateTypeText("teh ");
        view.CommitToModel();

        view.Model.PlainText.Should().Be("teh ");
    }
}
