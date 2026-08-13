using FluentAssertions;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;
using Free.Shared.Ribbon;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// The View &gt; Zoom commands are registered only when the host supplies backed actions, and executing them
/// runs those actions. The page-relative fit arithmetic itself is covered headlessly by
/// <c>FreeW.Core.Model.Tests.ZoomFitTests</c>; here we only prove the wiring.
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
            view,
            store,
            FreeWRibbonHostExecutionPorts.Empty with
            {
                OpenZoomDialog = () => opened++,
            });

        registry.TryGet("freew.zoom-dialog", out var command).Should().BeTrue();

        command!.Execute(RibbonCommandContext.Empty);

        opened.Should().Be(1, "executing the zoom command opens the Zoom dialog via the host action");
    }

    [StaFact]
    public void ZoomQuickCommands_AreRegistered_AndExecutingThemRunsHostActions()
    {
        var view = new DocumentView();
        view.LoadModel(Sample());
        var store = new RibbonStateStore();
        var zoom100 = 0;
        var onePage = 0;
        var pageWidth = 0;

        var registry = FreeWRibbonCommands.Build(
            view,
            store,
            FreeWRibbonHostExecutionPorts.Empty with
            {
                ApplyZoom = (_, _) => zoom100++,
                ZoomOnePage = () => onePage++,
                ZoomPageWidth = () => pageWidth++,
            });

        registry.TryGet("freew.zoom-100", out var zoom100Command).Should().BeTrue();
        registry.TryGet("freew.zoom-one-page", out var onePageCommand).Should().BeTrue();
        registry.TryGet("freew.zoom-page-width", out var pageWidthCommand).Should().BeTrue();

        zoom100Command!.Execute(RibbonCommandContext.Empty);
        onePageCommand!.Execute(RibbonCommandContext.Empty);
        pageWidthCommand!.Execute(RibbonCommandContext.Empty);

        zoom100.Should().Be(1, "100% applies the host's fixed zoom preset");
        onePage.Should().Be(1, "One Page applies the host's whole-page fit preset");
        pageWidth.Should().Be(1, "Page Width applies the host's page-width fit preset");
    }

    [StaFact]
    public void ZoomCommand_IsAbsent_WhenHostSuppliesNoAction()
    {
        var view = new DocumentView();
        view.LoadModel(Sample());
        var store = new RibbonStateStore();

        var registry = FreeWRibbonCommands.Build(view, store);

        registry.TryGet("freew.zoom-dialog", out _).Should().BeFalse();
        registry.TryGet("freew.zoom-100", out _).Should().BeFalse();
        registry.TryGet("freew.zoom-one-page", out _).Should().BeFalse();
        registry.TryGet("freew.zoom-page-width", out _).Should().BeFalse();
    }
}
