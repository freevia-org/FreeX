using System.Threading;
using Avalonia;
using Avalonia.Headless;
using Free.Shared.Ribbon;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Avalonia.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

public sealed class FinalFiveCommandParityTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Fact]
    public void AvaloniaProfile_UsesTheFiveWpfCommandIdsInEquivalentGroups()
    {
        var definition = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Avalonia);
        var text = definition.FindTab("insert")!.FindGroup("text")!;
        var draw = definition.FindTab("table-design")!.FindGroup("draw-borders")!;

        DirectCommandIds(text).Should().Contain(
            "freew.field",
            "freew.save-quickpart",
            "freew.building-blocks-organizer");
        DirectCommandIds(draw).Should().Contain("freew.draw-table", "freew.eraser");

        var quickParts = text.Controls.OfType<RibbonDropdown>()
            .Single(control => control.CommandId.Value == "freew.insert-quickpart");
        quickParts.Menu.Items
            .Where(item => item.CommandId is not null)
            .Select(item => item.CommandId!.Value.Value)
            .Should().Contain(
                "freew.field",
                "freew.save-quickpart",
                "freew.building-blocks-organizer");
    }

    [Fact]
    public void InsertTextCommandRoutes_InvokeTheirRealShellWorkflows()
    {
        var calls = new List<string>();
        var registry = FreeWAvaloniaRibbonCommands.Build(new DocumentView(), NoopCallbacks() with
        {
            OpenFieldDialog = () => calls.Add("field"),
            SaveQuickPartSelection = () => calls.Add("save"),
            OpenBuildingBlocksOrganizer = () => calls.Add("organizer"),
            OpenDrawTableDialog = () => calls.Add("draw"),
            OpenSplitCellDialog = () => calls.Add("split"),
        });

        Execute(registry, "freew.field");
        Execute(registry, "freew.save-quickpart");
        Execute(registry, "freew.building-blocks-organizer");
        Execute(registry, "freew.draw-table");
        Execute(registry, "freew.table-split-cell");

        calls.Should().Equal("field", "save", "organizer", "draw", "split");
    }

    [Fact]
    public async Task InsertTextCommands_UseSharedQuickPartAndFieldBehavior()
    {
        await Session.Dispatch(() =>
        {
            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();
            document.Properties.Title = "Parity Report";
            document.Blocks.Add(new Paragraph("Alpha Beta"));
            var editor = new DocumentView();
            editor.LoadDocument(document);
            editor.Measure(new Size(800, 1200));
            editor.SetSelectionRangePublic(0, 6, 0, 10);

            editor.InsertComplexField(" TITLE ");

            var paragraph = document.Blocks.OfType<Paragraph>().Single();
            paragraph.PlainText.Should().Be("Alpha Parity Report");
            paragraph.Runs.Single(run => run.ComplexField is not null)
                .ComplexField!.Instruction.Should().Be(" TITLE ");

            editor.Undo();
            paragraph.PlainText.Should().Be("Alpha Beta");
            editor.Redo();
            paragraph.PlainText.Should().Be("Alpha Parity Report");

            var part = FreeW.App.Presentation.Ribbon.QuickPartCommandPlanner
                .CreateSelection("One\nTwo", "Snippet")!;
            editor.Undo();
            editor.SetSelectionRangePublic(0, 0, 0, 0);
            editor.InsertQuickPartText(part.Text);
            var paragraphs = document.Blocks.OfType<Paragraph>().Select(block => block.PlainText).ToArray();
            paragraphs[0].Should().StartWith("One");
            paragraphs.Should().Contain("Two");
        }, CancellationToken.None);
    }

    [Fact]
    public async Task TableDrawingCommands_MutateAndUndo()
    {
        await Session.Dispatch(() =>
        {
            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();
            document.Blocks.Add(new Paragraph("Body"));
            var editor = new DocumentView();
            editor.LoadDocument(document);
            editor.Measure(new Size(800, 1200));
            var registry = FreeWAvaloniaRibbonCommands.Build(editor, NoopCallbacks() with
            {
                OpenDrawTableDialog = () => editor.InsertTable(2, 3),
            });

            Execute(registry, "freew.draw-table");
            document.Blocks.OfType<Table>().Single().Rows.Should().HaveCount(2);
            document.Blocks.OfType<Table>().Single().Rows[0].Cells.Should().HaveCount(3);
            editor.Undo();
            document.Blocks.Should().ContainSingle().Which.Should().BeOfType<Paragraph>();

            var table = Table.Create(1, 2);
            document.Blocks.Add(table);
            editor.PlaceCaretInCell(1, 0, 0, 0, 0);
            Execute(registry, "freew.eraser");
            table.Rows[0].Cells.Should().ContainSingle();
            table.Rows[0].Cells[0].GridSpan.Should().Be(2);
            editor.Undo();
            table.Rows[0].Cells.Should().HaveCount(2);
            editor.Redo();
            table.Rows[0].Cells.Should().ContainSingle();
        }, CancellationToken.None);
    }

    private static IEnumerable<string> DirectCommandIds(RibbonGroup group) =>
        group.Controls.Select(control => control.CommandId.Value);

    private static void Execute(RibbonCommandRegistry registry, string commandId)
    {
        registry.TryGet(new RibbonCommandId(commandId), out var command).Should().BeTrue();
        command!.Execute(RibbonCommandContext.Empty);
    }

    private static FreeWRibbonHostExecutionPorts NoopCallbacks() => new(
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
