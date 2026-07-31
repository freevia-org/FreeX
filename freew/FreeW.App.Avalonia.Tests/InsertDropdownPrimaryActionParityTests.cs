using FluentAssertions;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Avalonia.Ribbon;
using FreeW.Core.Model;
using Free.Shared.Ribbon;

namespace FreeW.App.Avalonia.Tests;

public sealed class InsertDropdownPrimaryActionParityTests
{
    [Fact]
    public void Table_dropdown_primary_action_inserts_wpf_default_two_by_two_table()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("Before table"));
        var view = new DocumentView();
        view.LoadDocument(document);
        var registry = FreeWRibbon.BuildRegistry(view, NoopCallbacks());

        registry.TryGet(new RibbonCommandId("freew.table"), out var command)
            .Should().BeTrue("the shared WPF command id must be registered");
        command!.Execute(RibbonCommandContext.Empty);

        var table = view.Document.Blocks.OfType<Table>().Should().ContainSingle().Subject;
        table.Rows.Should().HaveCount(2);
        table.Rows.Select(row => row.Cells.Count).Should().Equal(2, 2);
    }

    private static RibbonHostCallbacks NoopCallbacks() =>
        new(
            Open: () => { },
            Save: () => { },
            Cut: () => { },
            Copy: () => { },
            Paste: () => { },
            Backstage: () => { },
            NewDocument: () => { },
            ToggleNavigationPane: () => { },
            ToggleReviewingPane: () => { },
            ToggleRevealFormatting: () => { },
            OpenFindReplaceDialog: () => { },
            SetPrintLayout: () => { },
            SetWebLayout: () => { },
            SetDraftView: () => { },
            OpenFontDialog: () => { },
            OpenParagraphDialog: () => { },
            OpenPageSetupDialog: () => { },
            ToggleOrientation: () => { },
            ApplyMarginPreset: _ => { },
            ApplyPaperSize: _ => { },
            InsertPicture: () => { },
            OpenWordCountDialog: () => { },
            ApplyZoom: (_, _) => { });
}
