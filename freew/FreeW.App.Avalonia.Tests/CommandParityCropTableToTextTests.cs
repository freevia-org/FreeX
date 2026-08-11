using System.Threading;
using Avalonia;
using Avalonia.Headless;
using Free.Shared.Ribbon;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Avalonia.Ribbon;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

public sealed class CommandParityCropTableToTextTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Fact]
    public void AvaloniaRibbon_ExposesCropAndTableToTextInWpfEquivalentGroups()
    {
        var definition = FreeWRibbon.BuildDefinition();

        definition.FindTab("picture-format")!.FindGroup("picture-adjust")!.Controls
            .Select(CommandId)
            .Should().Contain("freew.image-crop");
        definition.FindTab("layout")!.FindGroup("data")!.Controls
            .Select(CommandId)
            .Should().Contain("freew.table-to-text");
        definition.FindTab("table-layout")!.FindGroup("table-data")!.Controls
            .Select(CommandId)
            .Should().Contain("freew.table-to-text");
    }

    [Fact]
    public async Task ImageCropRegistryRoute_MatchesSelectionEnablementMutationAndUndo()
    {
        await Session.Dispatch(() =>
        {
            var image = new InlineImage(OnePixelPng(), 120, 80)
            {
                Wrapping = ImageWrapping.Square,
            };
            var paragraph = new Paragraph();
            paragraph.Runs.Add(Run.FromImage(image));
            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();
            document.Blocks.Add(paragraph);

            var editor = new DocumentView();
            editor.LoadDocument(document);
            editor.Measure(new Size(800, 1200));
            var callbacks = NoopCallbacks() with
            {
                ShowImageCropDialogAsync = selected =>
                {
                    selected.Should().BeSameAs(image);
                    return ValueTask.FromResult<ImageCropDialogResult?>(new(0.1, 0.2, 0.15, 0.05));
                },
            };
            var registry = FreeWRibbon.BuildRegistry(editor, callbacks);
            var command = Stateful(registry, "freew.image-crop");
            command.GetState().IsEnabled.Should().BeFalse();

            editor.SelectFloating(0, 0);
            command.GetState().IsEnabled.Should().BeTrue();
            command.Execute(RibbonCommandContext.Empty);

            image.CropLeft.Should().Be(0.1);
            image.CropRight.Should().Be(0.2);
            image.CropTop.Should().Be(0.15);
            image.CropBottom.Should().Be(0.05);

            editor.Undo();
            image.HasCrop.Should().BeFalse();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task TableToTextRegistryRoute_MatchesCaretEnablementMutationSelectionAndUndo()
    {
        await Session.Dispatch(() =>
        {
            var table = new Table();
            table.Rows.Add(Row("North", "120"));
            table.Rows.Add(Row("South", "98"));
            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();
            document.Blocks.Add(table);

            var editor = new DocumentView();
            editor.LoadDocument(document);
            editor.Measure(new Size(800, 1200));
            var callbacks = NoopCallbacks() with
            {
                ShowTableToTextDialogAsync = () => ValueTask.FromResult<char?>(';'),
            };
            var registry = FreeWRibbon.BuildRegistry(editor, callbacks);
            var command = Stateful(registry, "freew.table-to-text");
            command.GetState().IsEnabled.Should().BeFalse();

            editor.PlaceCaretInCell(0, 0, 0, 0, 0);
            command.GetState().IsEnabled.Should().BeTrue();
            command.Execute(RibbonCommandContext.Empty);

            editor.Document.Blocks.OfType<Paragraph>().Select(paragraph => paragraph.PlainText)
                .Should().Equal("North;120", "South;98");
            editor.CellCaretInfo.Should().BeNull("the converted table no longer owns the caret");

            editor.Undo();
            editor.Document.Blocks.Should().ContainSingle().Which.Should().BeOfType<Table>();
        }, CancellationToken.None);
    }

    private static IRibbonStatefulCommand Stateful(RibbonCommandRegistry registry, string commandId)
    {
        registry.TryGet(new RibbonCommandId(commandId), out var command).Should().BeTrue();
        return command.Should().BeAssignableTo<IRibbonStatefulCommand>().Subject;
    }

    private static string CommandId(RibbonControl control) => control switch
    {
        RibbonButton button => button.CommandId.Value,
        _ => string.Empty,
    };

    private static TableRow Row(params string[] values)
    {
        var row = new TableRow();
        foreach (var value in values)
            row.Cells.Add(new TableCell(value));
        return row;
    }

    private static byte[] OnePixelPng() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

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
