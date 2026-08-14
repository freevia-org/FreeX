using Free.Shared.Ribbon;
using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Presentation.Tests;

public sealed class NoteReferenceRibbonWorkflowTests
{
    [Fact]
    public void SharedWorkflowOwnsInsertionAliasesNavigationAndOptions()
    {
        var calls = new List<string>();
        var bindings = new FreeWRibbonCommandBindingPorts();

        NoteReferenceRibbonWorkflow.Register(
            bindings,
            Ports(calls, openNotes: () => calls.Add("open-notes")));

        Execute(bindings, FreeWRibbonCommandAction.Footnote);
        Execute(bindings, FreeWRibbonCommandAction.Endnote);
        Execute(bindings, FreeWRibbonCommandAction.NextFootnote);
        Execute(bindings, FreeWRibbonCommandAction.PreviousFootnote);
        Execute(bindings, FreeWRibbonCommandAction.NextEndnote);
        Execute(bindings, FreeWRibbonCommandAction.PreviousEndnote);
        Execute(bindings, FreeWRibbonCommandAction.FootnoteEndnoteOptions);

        calls.Should().Equal(
            "insert-footnote",
            "insert-endnote",
            "next-footnote",
            "previous-footnote",
            "next-endnote",
            "previous-endnote",
            "options");

        bindings.TryGet("freew.insert-footnote", out var footnoteAlias).Should().BeTrue();
        bindings.TryGet("freew.insert-endnote", out var endnoteAlias).Should().BeTrue();
        footnoteAlias.Should().BeSameAs(Command(bindings, FreeWRibbonCommandAction.Footnote));
        endnoteAlias.Should().BeSameAs(Command(bindings, FreeWRibbonCommandAction.Endnote));
    }

    [Fact]
    public void CompletePanePairCreatesLiveToggleAndReturnsItForStatePublication()
    {
        var calls = new List<string>();
        var visible = false;
        var bindings = new FreeWRibbonCommandBindingPorts();

        var stateful = NoteReferenceRibbonWorkflow.Register(
            bindings,
            Ports(
                calls,
                openNotes: () => calls.Add("open-notes"),
                toggleNotes: () =>
                {
                    calls.Add("toggle-notes");
                    visible = !visible;
                },
                isNotesVisible: () => visible));

        stateful.Should().NotBeNull();
        stateful!.GetState().IsChecked.Should().BeFalse();
        Execute(bindings, FreeWRibbonCommandAction.ShowNotes);
        stateful.GetState().IsChecked.Should().BeTrue();
        calls.Should().Equal("toggle-notes");
    }

    [Fact]
    public void IncompletePanePairUsesOpenFallbackAndMissingNativeRoutesFailClosed()
    {
        var calls = new List<string>();
        var bindings = new FreeWRibbonCommandBindingPorts();

        var stateful = NoteReferenceRibbonWorkflow.Register(
            bindings,
            Ports(
                calls,
                openNotes: () => calls.Add("open-notes"),
                toggleNotes: () => calls.Add("toggle-notes"),
                isNotesVisible: null,
                hasOptions: false));

        stateful.Should().BeNull();
        Execute(bindings, FreeWRibbonCommandAction.ShowNotes);
        calls.Should().Equal("open-notes");

        var options = Command(bindings, FreeWRibbonCommandAction.FootnoteEndnoteOptions);
        options.Should().BeSameAs(FreeWRibbonExecutionProfile.UnavailableCommand);
        ((IRibbonStatefulCommand)options).GetState().IsEnabled.Should().BeFalse();

        var withoutNotes = new FreeWRibbonCommandBindingPorts();
        NoteReferenceRibbonWorkflow.Register(withoutNotes, Ports([], openNotes: null));
        Command(withoutNotes, FreeWRibbonCommandAction.ShowNotes)
            .Should().BeSameAs(FreeWRibbonExecutionProfile.UnavailableCommand);
    }

    [Fact]
    public void MissingNoteDialogPortsDisableInsertionCommandsAndAliases()
    {
        var bindings = new FreeWRibbonCommandBindingPorts();
        NoteReferenceRibbonWorkflow.Register(
            bindings,
            new NoteReferenceRibbonPorts(
                InsertFootnote: null,
                InsertEndnote: null,
                MoveToNextFootnote: () => { },
                MoveToPreviousFootnote: () => { },
                MoveToNextEndnote: () => { },
                MoveToPreviousEndnote: () => { },
                OpenNotes: null,
                ToggleNotesPane: null,
                IsNotesPaneVisible: null,
                OpenFootnoteEndnoteOptions: null));

        var footnote = Command(bindings, FreeWRibbonCommandAction.Footnote);
        var endnote = Command(bindings, FreeWRibbonCommandAction.Endnote);
        foreach (var command in new[] { footnote, endnote })
        {
            command.Should().BeSameAs(FreeWRibbonExecutionProfile.UnavailableCommand);
            command.Should().BeAssignableTo<IRibbonStatefulCommand>()
                .Which.GetState().IsEnabled.Should().BeFalse();
        }

        bindings.TryGet("freew.insert-footnote", out var footnoteAlias).Should().BeTrue();
        bindings.TryGet("freew.insert-endnote", out var endnoteAlias).Should().BeTrue();
        footnoteAlias.Should().BeSameAs(footnote);
        endnoteAlias.Should().BeSameAs(endnote);
    }

    [Fact]
    public void BothRenderersDelegateTheFootnotesFamilyToTheSharedWorkflow()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var wpf = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs"));
        var avalonia = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Avalonia", "Ribbon", "FreeWAvaloniaRibbonCommands.cs"));

        wpf.Should().Contain("NoteReferenceRibbonWorkflow.Register(");
        avalonia.Should().Contain("NoteReferenceRibbonWorkflow.Register(");
        wpf.Should().NotContain("referenceCommands.Bind(FreeWRibbonCommandAction.Footnote,");
        avalonia.Should().NotContain("family.Bind(FreeWRibbonCommandAction.Footnote,");
        avalonia.Should().NotContain("family.BindToggle(FreeWRibbonCommandAction.ShowNotes,");
        avalonia.Should().NotContain("callbacks.OpenFootnoteDialog ??");
        avalonia.Should().NotContain("callbacks.OpenEndnoteDialog ??");
    }

    private static NoteReferenceRibbonPorts Ports(
        ICollection<string> calls,
        Action? openNotes,
        Action? toggleNotes = null,
        Func<bool>? isNotesVisible = null,
        bool hasOptions = true) =>
        new(
            () => calls.Add("insert-footnote"),
            () => calls.Add("insert-endnote"),
            () => calls.Add("next-footnote"),
            () => calls.Add("previous-footnote"),
            () => calls.Add("next-endnote"),
            () => calls.Add("previous-endnote"),
            openNotes,
            toggleNotes,
            isNotesVisible,
            hasOptions ? () => calls.Add("options") : null);

    private static void Execute(
        FreeWRibbonCommandBindingPorts bindings,
        FreeWRibbonCommandAction action) =>
        Command(bindings, action).Execute(RibbonCommandContext.Empty);

    private static IRibbonCommand Command(
        FreeWRibbonCommandBindingPorts bindings,
        FreeWRibbonCommandAction action)
    {
        var route = FreeWRibbonCommandWorkflow.Routes.Single(candidate => candidate.Action == action);
        bindings.TryGet(route.CommandId, out var command).Should().BeTrue();
        return command!;
    }
}
