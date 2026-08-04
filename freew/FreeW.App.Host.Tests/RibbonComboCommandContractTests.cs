using Free.Shared.Ribbon;
using FreeW.App.Host.Editing;

namespace FreeW.App.Host.Tests;

public sealed class RibbonComboCommandContractTests
{
    [StaFact]
    public void TextAndParagraphCombos_ConsumeSharedSelectedValueContract()
    {
        var view = BuildView();
        var stateStore = new RibbonStateStore();
        var registry = FreeWRibbonCommands.Build(view, stateStore);

        stateStore.GetState("freew.font-family").Value.Should().Be("Calibri");
        stateStore.GetState("freew.font-size").Value.Should().Be("11");
        stateStore.GetState("freew.line-spacing").Value.Should().Be("1.15");
        stateStore.GetState("freew.style").Value.Should().Be("Normal");

        Execute(view, registry, "freew.font-family", "Arial");
        Execute(view, registry, "freew.font-size", "16");

        var run = ((Paragraph)view.Model.Blocks[0]).Runs.Single();
        run.Formatting.FontFamily.Should().Be("Arial");
        run.Formatting.FontSizePt.Should().Be(16);
        stateStore.GetState("freew.font-family").Value.Should().Be("Arial");
        stateStore.GetState("freew.font-size").Value.Should().Be("16");

        Execute(view, registry, "freew.line-spacing", "1.5");
        ((Paragraph)view.Model.Blocks[0]).Formatting.LineSpacing.Should().Be(1.5);
        stateStore.GetState("freew.line-spacing").Value.Should().Be("1.5");

        Execute(view, registry, "freew.style", "Heading 1");
        ((Paragraph)view.Model.Blocks[0]).StyleId.Should().Be("Heading1");
        stateStore.GetState("freew.style").Value.Should().Be("Heading 1");

        var titleDocument = TextDocument.CreateEmpty();
        titleDocument.Blocks.Clear();
        titleDocument.Blocks.Add(new Paragraph("Loaded title") { StyleId = "Title" });
        view.LoadModel(titleDocument);
        stateStore.GetState("freew.style").Value.Should().Be("Title");
    }

    [StaFact]
    public void LayoutAndHeaderFooterCombos_ConsumeSharedSelectedValueContract()
    {
        var view = BuildView();
        var stateStore = new RibbonStateStore();
        var registry = FreeWRibbonCommands.Build(view, stateStore);

        stateStore.GetState("freew.indent-left").Value.Should().Be("0");
        stateStore.GetState("freew.indent-right").Value.Should().Be("0");
        stateStore.GetState("freew.space-before").Value.Should().Be("0");
        stateStore.GetState("freew.space-after").Value.Should().Be("8");
        stateStore.GetState("freew.hf-header-from-top").Value.Should().Be("0");
        stateStore.GetState("freew.hf-footer-from-bottom").Value.Should().Be("0");

        Execute(view, registry, "freew.indent-left", "24");
        Execute(view, registry, "freew.indent-right", "18");
        Execute(view, registry, "freew.space-before", "6");
        Execute(view, registry, "freew.space-after", "10");
        Execute(view, registry, "freew.hf-header-from-top", "36");
        Execute(view, registry, "freew.hf-footer-from-bottom", "54");

        var formatting = ((Paragraph)view.Model.Blocks[0]).Formatting;
        formatting.IndentLeftPt.Should().Be(24);
        formatting.IndentRightPt.Should().Be(18);
        formatting.SpaceBeforePt.Should().Be(6);
        formatting.SpaceAfterPt.Should().Be(10);
        view.Model.Page.HeaderDistancePt.Should().Be(36);
        view.Model.Page.FooterDistancePt.Should().Be(54);
        stateStore.GetState("freew.indent-left").Value.Should().Be("24");
        stateStore.GetState("freew.indent-right").Value.Should().Be("18");
        stateStore.GetState("freew.space-before").Value.Should().Be("6");
        stateStore.GetState("freew.space-after").Value.Should().Be("10");
        stateStore.GetState("freew.hf-header-from-top").Value.Should().Be("36");
        stateStore.GetState("freew.hf-footer-from-bottom").Value.Should().Be("54");
    }

    [StaFact]
    public void ThemeCombo_PublishesLoadedAndAppliedDocumentTheme()
    {
        var view = BuildView();
        var stateStore = new RibbonStateStore();
        var registry = FreeWRibbonCommands.Build(view, stateStore);

        stateStore.GetState("freew.theme").Value.Should().Be("Office");

        Execute(view, registry, "freew.theme", "Berlin");
        view.Model.Theme.Name.Should().Be("Berlin");
        stateStore.GetState("freew.theme").Value.Should().Be("Berlin");

        var loaded = TextDocument.CreateEmpty();
        loaded.Theme = DocumentTheme.FindByName("Ion")!;
        view.LoadModel(loaded);
        stateStore.GetState("freew.theme").Value.Should().Be("Ion");

        registry.TryGet("freew.theme", out var command).Should().BeTrue();
        command!.Execute(RibbonCommandContext.ForSelectedValue("Missing Theme"));
        view.Model.Theme.Name.Should().Be("Ion");
        stateStore.GetState("freew.theme").Value.Should().Be("Ion");
    }

    private static DocumentView BuildView()
    {
        var model = TextDocument.CreateEmpty();
        model.Blocks.Clear();
        model.Blocks.Add(new Paragraph("Combo contract"));
        var view = new DocumentView();
        view.LoadModel(model);
        view.SetSelectionRangeForTest(0, 0, 0, "Combo contract".Length);
        return view;
    }

    private static void Execute(DocumentView view, RibbonCommandRegistry registry, string commandId, string value)
    {
        view.SetSelectionRangeForTest(0, 0, 0, "Combo contract".Length);
        registry.TryGet(commandId, out var command).Should().BeTrue();
        command!.Execute(RibbonCommandContext.ForSelectedValue(value));
    }
}
