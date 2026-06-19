using FluentAssertions;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;
using Free.Shared.Ribbon;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// The View &gt; Zoom command (<c>freew.zoom-dialog</c>) is registered only when the host supplies the
/// dialog-opening action, and executing it runs that action (the host opens <see cref="ZoomDialog"/> and
/// applies the chosen factor to <c>DocumentView.ZoomLevel</c>). The page-relative fit arithmetic itself is
/// covered headlessly by <c>FreeW.Core.Model.Tests.ZoomFitTests</c>; here we only prove the wiring.
/// </summary>
public sealed class ZoomDialogCommandTests
{
    private static TextDocument Sample()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Hello"));
        return doc;
    }

    [StaFact]
    public void ZoomCommand_IsRegistered_AndExecutingItRunsTheHostAction()
    {
        var view = new DocumentView();
        view.LoadModel(Sample());
        var store = new RibbonStateStore();
        var opened = 0;

        var registry = FreeWRibbonCommands.Build(
            view, store, onPrintPreview: null, onToggleNavPane: null, isNavPaneVisible: null,
            onToggleReadMode: null, isReadModeActive: null, onTogglePrintLayout: null, isPrintLayoutActive: null,
            onToggleOutlineView: null, isOutlineViewActive: null, onZoomDialog: () => opened++);

        registry.TryGet("freew.zoom-dialog", out var command).Should().BeTrue();

        command!.Execute(RibbonCommandContext.Empty);

        opened.Should().Be(1, "executing the zoom command opens the Zoom dialog via the host action");
    }

    [StaFact]
    public void ZoomCommand_IsAbsent_WhenHostSuppliesNoAction()
    {
        var view = new DocumentView();
        view.LoadModel(Sample());
        var store = new RibbonStateStore();

        var registry = FreeWRibbonCommands.Build(view, store);

        registry.TryGet("freew.zoom-dialog", out _).Should().BeFalse();
    }
}
