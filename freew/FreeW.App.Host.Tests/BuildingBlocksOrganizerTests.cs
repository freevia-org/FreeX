using System.Linq;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Coverage for the Building Blocks Organizer (Insert › Quick Parts › Building Blocks Organizer). The
/// organizer lists blocks from the shared <see cref="QuickPartStore"/> and, on Insert, drops the selected
/// block's text at the caret through <see cref="DocumentView.InsertText(string)"/> — the same reversible
/// edit path the dialog's Insert button uses. These run on an STA thread (<c>[StaFact]</c>) because the
/// RichTextBox/FlowDocument need STA + a Dispatcher; mirrors the Mark Citation editor tests.
/// </summary>
public sealed class BuildingBlocksOrganizerTests
{
    private static DocumentView LoadedView()
    {
        var model = TextDocument.CreateEmpty();
        model.Blocks.Clear();
        model.Blocks.Add(new Paragraph("Body text"));
        var view = new DocumentView();
        view.LoadModel(model);
        return view;
    }

    [StaFact]
    public void Insert_PlacesTheBlocksContentIntoTheDocument()
    {
        var view = LoadedView();
        // A block as it would be listed in the organizer (carries gallery/category/description metadata).
        var block = new QuickPart("Greeting", ["Dear Sir or Madam,"], "AutoText", "General", "A formal opener");

        // The exact operation the organizer's Insert button performs on the selected block.
        view.Focus();
        view.InsertText(block.Text);
        view.CommitToModel();

        var text = string.Join("\n", view.Model.Blocks.OfType<Paragraph>().Select(p => p.PlainText));
        text.Should().Contain("Dear Sir or Madam,");
    }

    [StaFact]
    public void Insert_MultiLineBlock_PlacesEveryLine()
    {
        var view = LoadedView();
        var block = new QuickPart("Sig", ["Best regards,", "Jane Doe"], "AutoText", "Signatures", null);

        view.Focus();
        view.InsertText(block.Text);
        view.CommitToModel();

        var documentText = string.Join("\n", view.Model.Blocks.OfType<Paragraph>().Select(p => p.PlainText));
        documentText.Should().Contain("Best regards,");
        documentText.Should().Contain("Jane Doe");
    }
}
