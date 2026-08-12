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
        var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());

        registry.TryGet(new RibbonCommandId("freew.table"), out var command)
            .Should().BeTrue("the shared WPF command id must be registered");
        command!.Execute(RibbonCommandContext.Empty);

        var table = view.Document.Blocks.OfType<Table>().Should().ContainSingle().Subject;
        table.Rows.Should().HaveCount(2);
        table.Rows.Select(row => row.Cells.Count).Should().Equal(2, 2);
    }

    [Fact]
    public void Cover_page_dropdown_primary_action_inserts_wpf_default_cover_page()
    {
        var document = TextDocument.CreateEmpty();
        document.Properties.Title = "Primary action title";
        var view = new DocumentView();
        view.LoadDocument(document);
        var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());

        registry.TryGet(new RibbonCommandId("freew.cover-page"), out var command)
            .Should().BeTrue("the shared WPF command id must be registered");
        command!.Execute(RibbonCommandContext.Empty);

        view.Document.Blocks.OfType<Paragraph>().First().PlainText.Should().Be("Primary action title");
    }

    [Fact]
    public void Equation_dropdown_primary_action_inserts_wpf_default_equation()
    {
        var document = TextDocument.CreateEmpty();
        var view = new DocumentView();
        view.LoadDocument(document);
        var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());

        registry.TryGet(new RibbonCommandId("freew.equation"), out var command)
            .Should().BeTrue("the shared WPF command id must be registered");
        command!.Execute(RibbonCommandContext.Empty);

        var equations = view.Document.Blocks.OfType<Paragraph>()
            .SelectMany(paragraph => paragraph.Runs)
            .Select(run => run.Equation)
            .Where(item => item is not null)
            .ToArray();
        equations.Should().ContainSingle();
        equations[0]!.LinearText.Should().Contain("E = m");
    }

    [Fact]
    public void Caption_dropdown_primary_action_invokes_dialog_backed_insert()
    {
        var document = TextDocument.CreateEmpty();
        var view = new DocumentView();
        view.LoadDocument(document);
        var registry = FreeWAvaloniaRibbonCommands.Build(
            view,
            NoopCallbacks(() => view.InsertCaption(CaptionLabel.Figure, "Primary caption")));

        registry.TryGet(new RibbonCommandId("freew.caption"), out var command)
            .Should().BeTrue("the shared WPF command id must be registered");
        command!.Execute(RibbonCommandContext.Empty);

        view.Document.Blocks.OfType<Paragraph>()
            .Should().ContainSingle(paragraph => paragraph.StyleId == Captions.StyleId)
            .Which.PlainText.Should().Be("Figure 1: Primary caption");
    }

    [Fact]
    public void Caption_dropdown_primary_action_without_shell_callback_does_not_mutate()
    {
        var document = TextDocument.CreateEmpty();
        var before = document.Blocks.ToArray();
        var view = new DocumentView();
        view.LoadDocument(document);
        var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());

        registry.TryGet(new RibbonCommandId("freew.caption"), out var command).Should().BeTrue();
        command!.Execute(RibbonCommandContext.Empty);

        view.Document.Blocks.Should().Equal(before);
    }

    private static FreeWRibbonHostExecutionPorts NoopCallbacks(Action? openCaptionDialog = null) =>
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
            ApplyZoom: (_, _) => { },
            OpenCaptionDialog: openCaptionDialog);
}
