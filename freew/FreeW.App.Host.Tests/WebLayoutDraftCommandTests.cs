using FreeW.App.Host.Editing;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;
using Free.Shared.Ribbon;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Wiring coverage for the View &gt; Views commands <c>freew.web-layout</c> and <c>freew.draft-view</c>.
/// They register only when the host supplies the callbacks, executing them switches the editor's
/// <see cref="DocumentView.ViewMode"/>, and their stateful checked-state reflects the active mode so the
/// three print-family views (Print Layout / Web Layout / Draft) read as mutually exclusive. The chrome
/// behaviour itself (page sheet on/off) is covered by <see cref="ViewModeTests"/>.
/// </summary>
public sealed class WebLayoutDraftCommandTests
{
    private static TextDocument Sample()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Hello"));
        return doc;
    }

    // Build the registry the same way MainWindow does: the View ports drive DocumentView.SetViewMode and
    // their active-state predicates read back DocumentView.ViewMode.
    private static (RibbonCommandRegistry Registry, DocumentView View) Build()
    {
        var view = new DocumentView();
        view.LoadModel(Sample());
        var store = new RibbonStateStore();

        var registry = FreeWRibbonCommands.Build(
            view,
            store,
            FreeWRibbonHostExecutionPorts.Empty with
            {
                SetPrintLayout = () => view.SetViewMode(DocumentViewMode.PrintLayout),
                IsPrintLayoutActive = () => view.ViewMode == DocumentViewMode.PrintLayout,
                SetWebLayout = () => view.SetViewMode(DocumentViewMode.WebLayout),
                IsWebLayoutActive = () => view.ViewMode == DocumentViewMode.WebLayout,
                SetDraftView = () => view.SetViewMode(DocumentViewMode.Draft),
                IsDraftViewActive = () => view.ViewMode == DocumentViewMode.Draft,
            });

        return (registry, view);
    }

    [StaFact]
    public void WebLayoutCommand_SwitchesEditorToWebLayout()
    {
        var (registry, view) = Build();

        registry.TryGet("freew.web-layout", out var command).Should().BeTrue();
        command!.Execute(RibbonCommandContext.Empty);

        view.ViewMode.Should().Be(DocumentViewMode.WebLayout);
    }

    [StaFact]
    public void DraftCommand_SwitchesEditorToDraft()
    {
        var (registry, view) = Build();

        registry.TryGet("freew.draft-view", out var command).Should().BeTrue();
        command!.Execute(RibbonCommandContext.Empty);

        view.ViewMode.Should().Be(DocumentViewMode.Draft);
    }

    [StaFact]
    public void ViewToggles_AreMutuallyExclusive_InCheckedState()
    {
        var (registry, _) = Build();

        registry.TryGet("freew.print-layout", out var print).Should().BeTrue();
        registry.TryGet("freew.web-layout", out var web).Should().BeTrue();
        registry.TryGet("freew.draft-view", out var draft).Should().BeTrue();

        var printState = (IRibbonStatefulCommand)print!;
        var webState = (IRibbonStatefulCommand)web!;
        var draftState = (IRibbonStatefulCommand)draft!;

        // Default Print Layout: exactly Print Layout is checked.
        printState.GetState().IsChecked.Should().BeTrue();
        webState.GetState().IsChecked.Should().BeFalse();
        draftState.GetState().IsChecked.Should().BeFalse();

        // Switching to Web Layout: only Web Layout is checked.
        web!.Execute(RibbonCommandContext.Empty);
        printState.GetState().IsChecked.Should().BeFalse();
        webState.GetState().IsChecked.Should().BeTrue();
        draftState.GetState().IsChecked.Should().BeFalse();

        // Switching to Draft: only Draft is checked.
        draft!.Execute(RibbonCommandContext.Empty);
        printState.GetState().IsChecked.Should().BeFalse();
        webState.GetState().IsChecked.Should().BeFalse();
        draftState.GetState().IsChecked.Should().BeTrue();
    }

    [StaFact]
    public void Commands_AreAbsent_WhenHostSuppliesNoCallbacks()
    {
        var view = new DocumentView();
        view.LoadModel(Sample());

        var registry = FreeWRibbonCommands.Build(view, new RibbonStateStore());

        registry.TryGet("freew.web-layout", out _).Should().BeFalse();
        registry.TryGet("freew.draft-view", out _).Should().BeFalse();
    }
}
