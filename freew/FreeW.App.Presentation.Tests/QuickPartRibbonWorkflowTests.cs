using Free.Shared.Ribbon;
using FreeW.App.Presentation.QuickParts;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests;

public sealed class QuickPartRibbonWorkflowTests
{
    [Fact]
    public void InsertSessionSelectsSavedLibraryEntriesAndReturnsTheirText()
    {
        var library = QuickPartLibrary.LoadFromPath(null);
        library.Save(new QuickPart("Greeting", ["Hello", "world"]));
        library.Save(new QuickPart("Signature", ["Regards"]));
        var session = new QuickPartInsertSession(library);

        session.Current.Names.Should().Equal("Greeting", "Signature");
        session.Current.SelectedIndex.Should().Be(0);
        session.Current.CanInsert.Should().BeTrue();

        session.SelectIndex(1);
        session.AcceptSelection().Should().Be(new QuickPartInsertAction("Signature", "Regards"));

        library.Remove("Signature");
        session.AcceptSelection().Should().BeNull(
            "a renderer must not insert an entry removed after the picker was opened");
    }

    [Fact]
    public void EmptyInsertSessionHasNoSelectionOrAction()
    {
        var session = new QuickPartInsertSession(QuickPartLibrary.LoadFromPath(null));

        session.Current.IsEmpty.Should().BeTrue();
        session.Current.SelectedIndex.Should().Be(-1);
        session.Current.CanInsert.Should().BeFalse();
        session.AcceptSelection().Should().BeNull();
    }

    [Fact]
    public void SharedWorkflowOwnsCanonicalCommandsAndCompatibilityAliases()
    {
        var inserted = new List<RunFieldKind>();
        var insertCalls = 0;
        var saveCalls = 0;
        var organizerCalls = 0;
        var insert = new ActionRibbonCommand(() => insertCalls++);
        var save = new ActionRibbonCommand(() => saveCalls++);
        var organizer = new ActionRibbonCommand(() => organizerCalls++);
        var bindings = new FreeWRibbonCommandBindingPorts();

        QuickPartRibbonWorkflow.Register(
            bindings,
            new QuickPartRibbonPorts(insert, save, organizer, inserted.Add));

        Command(bindings, "freew.insert-quickpart").Should().BeSameAs(insert);
        Command(bindings, "freew.quick-parts").Should().BeSameAs(insert);
        Command(bindings, "freew.quick-parts.snippet").Should().BeSameAs(insert);
        Command(bindings, "freew.save-quickpart").Should().BeSameAs(save);
        Command(bindings, "freew.building-blocks-organizer").Should().BeSameAs(organizer);

        Command(bindings, "freew.insert-quickpart").Execute(RibbonCommandContext.Empty);
        Command(bindings, "freew.save-quickpart").Execute(RibbonCommandContext.Empty);
        Command(bindings, "freew.building-blocks-organizer").Execute(RibbonCommandContext.Empty);
        Command(bindings, "freew.docprop-title").Execute(RibbonCommandContext.Empty);
        Command(bindings, "freew.quick-parts.date").Execute(RibbonCommandContext.Empty);

        insertCalls.Should().Be(1);
        saveCalls.Should().Be(1);
        organizerCalls.Should().Be(1);
        inserted.Should().Equal(RunFieldKind.Title, RunFieldKind.Date);
    }

    [Fact]
    public void BothRenderersProjectSharedQuickPartStateAndRouting()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var wpf = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs"));
        var avaloniaRegistry = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Avalonia", "Ribbon", "FreeWAvaloniaRibbonCommands.cs"));
        var avaloniaDialog = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Avalonia", "InsertDialogs.cs"));

        wpf.Should().Contain("QuickPartRibbonWorkflow.Register(")
            .And.Contain("new QuickPartInsertSession(library)");
        avaloniaRegistry.Should().Contain("QuickPartRibbonWorkflow.Register(");
        avaloniaDialog.Should().Contain("QuickPartInsertSession")
            .And.Contain("_session.AcceptSelection()")
            .And.NotContain("SnippetText");
    }

    private static IRibbonCommand Command(
        FreeWRibbonCommandBindingPorts bindings,
        string commandId)
    {
        bindings.TryGet(new RibbonCommandId(commandId), out var command).Should().BeTrue();
        return command!;
    }
}
