using System;
using FreeW.App.Host;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Regression coverage for the print/preview paginator's FlowDocument clone. The editor stamps
/// non-public <c>Tag</c> payloads on paragraphs/runs/hyperlinks/cells; <see cref="PrintLayout"/>'s clone
/// goes through <c>XamlWriter.Save</c>, which used to throw "Cannot serialize a non-public type" on those
/// Tags — crashing Print and Print Preview on essentially any styled document. Runs on STA because it
/// builds the real WPF editing surface.
/// </summary>
public sealed class PrintLayoutTests
{
    [StaFact]
    public void BuildPaginator_DocumentWithTaggedParagraphs_DoesNotThrow()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        // StyleId and BookmarkName both cause DocumentView to stamp a non-public ParagraphTag.
        doc.Blocks.Add(new Paragraph("A heading") { StyleId = "Heading1" });
        doc.Blocks.Add(new Paragraph("Body text with a bookmark") { BookmarkName = "bm1" });

        var view = new DocumentView();
        view.LoadModel(doc);

        var ex = Record.Exception(() =>
        {
            var paginator = PrintLayout.BuildPaginator(view);
            paginator.ComputePageCount();
            _ = paginator.GetPage(0);
        });

        Assert.Null(ex);
    }

    [StaFact]
    public void BuildPaginator_LeavesEditorTagsIntact()
    {
        // The clone strips Tags on the live editor document during serialization; it must restore them so
        // a subsequent CommitToModel still recovers style ids, bookmarks, etc.
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Styled") { StyleId = "Heading1" });

        var view = new DocumentView();
        view.LoadModel(doc);

        _ = PrintLayout.BuildPaginator(view);

        view.CommitToModel();
        var recovered = (Paragraph)view.Model.Blocks[0];
        Assert.Equal("Heading1", recovered.StyleId);
    }
}
