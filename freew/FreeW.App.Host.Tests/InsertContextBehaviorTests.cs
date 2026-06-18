using System.Linq;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Behavior QA over the real editing surface: inserting an object should leave the editor in the state
/// that drives the matching contextual ribbon tab (Word puts the caret in a freshly inserted table so
/// "Table Tools" appears immediately). Runs on STA because it builds the real WPF <see cref="DocumentView"/>.
/// </summary>
public sealed class InsertContextBehaviorTests
{
    private static DocumentView EmptyView()
    {
        var view = new DocumentView();
        view.LoadModel(TextDocument.CreateEmpty());
        return view;
    }

    [StaFact]
    public void InsertTable_PlacesCaretInTable_SoTableContextActivates()
    {
        var view = EmptyView();
        Assert.False(view.IsCaretInTable());

        view.InsertTable(2, 2);

        // Word behaviour: the caret lands in the new table so the Table Design contextual tab shows at once.
        Assert.True(view.IsCaretInTable());
    }

    [StaFact]
    public void InsertImage_AddsTheImageToTheModel()
    {
        var view = EmptyView();
        view.InsertImage(new InlineImage(OnePixelPng, 96, 96));
        view.CommitToModel();

        // The inserted picture must survive the commit cycle as a model image run — otherwise it would be
        // silently dropped on the next edit/save (and Picture Format / image commands would have no target).
        var hasImage = view.Model.Blocks.OfType<Paragraph>().SelectMany(p => p.Runs).Any(r => r.Image is not null);
        Assert.True(hasImage);
    }

    // A minimal valid 1x1 transparent PNG (so InsertImage can decode it into a real BitmapImage).
    private static readonly byte[] OnePixelPng =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41, 0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82
    ];
}
