namespace FreeP.App.Compositor.Tests;

public sealed class PresentationDomainSurfaceSessionTests
{
    [Fact]
    public void ZoomSession_OwnsInsertionRetargetingPropertiesAndCoverPersistence()
    {
        var presentation = BuildZoomPresentation();
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        var dirtyCount = 0;
        var canvasCount = 0;
        var hostCount = 0;
        var renderCount = 0;
        var session = new PresentationZoomAuthoringSession(
            () => editor,
            new PresentationZoomAuthoringSessionCallbacks(
                () => dirtyCount++,
                () => canvasCount++,
                () => hostCount++,
                (_, slideIndex, _, _) =>
                {
                    renderCount++;
                    return [(byte)(slideIndex + 1), 2, 3, 4];
                }));

        var insert = session.BuildSlideInsertionRequest();
        insert.Should().NotBeNull();
        insert!.Options.Select(option => option.Id).Should().Equal("slide-2", "slide-3", "slide-4");
        var shape = session.ApplySlideInsertion("slide-2");

        shape.Should().NotBeNull();
        shape!.Kind.Should().Be(SlideShapeKind.Zoom);
        renderCount.Should().Be(1);

        editor.Select(shape.Id);
        var target = session.BuildSelectedTargetRequest();
        target.Should().NotBeNull();
        target!.Kind.Should().Be(PresentationZoomTargetKind.Slide);
        target.SelectedTargetId.Should().Be("slide-2");
        session.ApplySelectedTarget(target, "slide-3").Should().BeTrue();
        shape.PreservedObject!.ZoomTargetSlideNumericId.Should().Be(presentation.Slides[2].NumericId);

        var properties = session.BuildSelectedPropertiesRequest();
        properties.Should().NotBeNull();
        session.ApplySelectedProperties(
            properties!,
            new PresentationZoomPropertiesApplyRequest(
                new ZoomObjectProperties(false, "cover", "900", false),
                ApplySummaryPropertiesToAllTiles: true,
                SummaryTileProperties: null,
                SummaryTileLayout: null)).Should().BeTrue();
        shape.PreservedObject.ZoomProperties!.ImageType.Should().Be("cover");

        var cover = session.BuildSelectedCoverTargetRequest();
        cover.Should().NotBeNull();
        cover!.RequiresSummaryTarget.Should().BeFalse();
        session.ApplySelectedCoverImage(cover, null, [9, 8, 7], "image/png").Should().BeTrue();
        session.RestoreSelectedPreview(cover, null).Should().BeTrue();

        dirtyCount.Should().Be(3);
        canvasCount.Should().Be(3);
        hostCount.Should().Be(3);
        renderCount.Should().Be(3);
    }

    [Fact]
    public void ZoomSession_OwnsSummaryTargetProjectionAndPreviewRefresh()
    {
        var presentation = BuildZoomPresentation();
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        var session = CreateZoomSession(editor);

        var insert = session.BuildSummaryInsertionRequest();
        insert.Should().NotBeNull();
        var shape = session.ApplySummaryInsertion(["section-1", "section-2"]);

        shape.Should().NotBeNull();
        shape!.PreservedObject!.SummaryZoomTargets.Select(target => target.SectionId)
            .Should().Equal("section-1", "section-2");
        editor.Select(shape.Id);

        var request = session.BuildSelectedSummaryTargetsRequest();
        request.Should().NotBeNull();
        request!.SelectedTargetIds.Should().Equal("section-1", "section-2");
        session.ApplySelectedSummaryTargets(
            request,
            ["section-2", "section-3"]).Should().BeTrue();
        shape.PreservedObject.SummaryZoomTargets.Select(target => target.SectionId)
            .Should().Equal("section-2", "section-3");
    }

    [Fact]
    public void DomainContextSession_OwnsTableEnablementAndStructuralCommands()
    {
        var presentation = Presentation.CreateEmpty();
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        var shape = editor.InsertTable(2, 2);
        editor.Select(shape.Id);
        editor.SetActiveTableCell(0, 0);
        var session = CreateContextSession(editor);

        var plan = session.BuildTable(shape.Id);

        plan.Should().NotBeNull();
        plan!.Entries.Where(entry => entry.Kind != PresentationDomainContextMenuEntryKind.Separator)
            .Select(entry => entry.Text)
            .Should().Equal(
                "Insert Row Above",
                "Insert Row Below",
                "Insert Column Left",
                "Insert Column Right",
                "Delete Row",
                "Delete Column",
                "Column Width",
                "Merge with Right Cell",
                "Split Cell");
        var merge = plan.Entries.Single(entry => entry.Text == "Merge with Right Cell");
        merge.IsEnabled.Should().BeTrue();
        session.Execute(merge.Action!).Should().BeTrue();
        shape.Table!.Rows[0].Cells[0].GridSpan.Should().Be(2);

        var split = session.BuildTable(shape.Id)!.Entries.Single(entry => entry.Text == "Split Cell");
        split.IsEnabled.Should().BeTrue();
        session.Execute(split.Action!).Should().BeTrue();
        shape.Table.Rows[0].Cells[0].GridSpan.Should().Be(1);
    }

    [Fact]
    public void DomainContextSession_OwnsWaterfallStateAndChartDialogDispatch()
    {
        var presentation = Presentation.CreateEmpty();
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        var pointRequests = new List<(int Series, int Point)>();
        var session = CreateContextSession(
            editor,
            (series, point) => pointRequests.Add((series, point)));
        var shape = editor.InsertChart(ChartType.Waterfall);
        var hit = new ChartSubtargetHit(
            shape.Id,
            ChartSubtargetKind.Point,
            SeriesIndex: 0,
            PointIndex: 1);

        var plan = session.BuildChart(hit);
        plan.Entries[0].Text.Should().Be("Set as Total");
        session.Execute(plan.Entries[0].Action!).Should().BeTrue();
        shape.Chart!.WaterfallTotalPointIndices.Should().Contain(1);
        session.BuildChart(hit).Entries[0].Text.Should().Be("Clear Total");

        var formatPoint = plan.Entries.Single(entry => entry.Text == "Format Data Point...");
        session.Execute(formatPoint.Action!).Should().BeTrue();
        pointRequests.Should().Equal((0, 1));
    }

    [Fact]
    public void NotesSession_OwnsProjectionNoOpDetectionAndUndoableMutation()
    {
        var presentation = Presentation.CreateEmpty();
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        var session = new PresentationNotesPaneSession(() => editor);

        var first = session.ApplyText("First line" + Environment.NewLine + "Second line");
        var noOp = session.ApplyText(first.Plan.Text);

        first.Changed.Should().BeTrue();
        first.Plan.Text.Should().Be("First line" + Environment.NewLine + "Second line");
        first.Plan.Preview.SlideIndex.Should().Be(0);
        noOp.Changed.Should().BeFalse();
        editor.Undo();
        session.BuildProjection().Text.Should().BeEmpty();
    }

    [Fact]
    public void HyperlinkSession_OwnsSelectedRunFallbackAndShapeMutation()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides.Add(new Slide { Id = "target", Title = "Target" });
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        var shape = new SlideShape { Id = 42, Name = "Link target" };
        editor.CurrentSlide!.Shapes.Add(shape);
        editor.Select(shape.Id);
        var session = new PresentationHyperlinkWorkflowSession(() => editor);
        var hyperlink = new Hyperlink { Url = "https://example.test", Tooltip = "Example" };

        var shapeRequest = session.BuildRequest(false, null);
        var shapeResult = session.Apply(shapeRequest, hyperlink);

        shapeResult.Target.Should().Be(PresentationHyperlinkApplyTarget.SelectedShape);
        editor.SelectedShapeHyperlink!.Url.Should().Be("https://example.test");

        var runRequest = session.BuildRequest(true, hyperlink);
        var runResult = session.Apply(runRequest, hyperlink, _ => true);
        runResult.Target.Should().Be(PresentationHyperlinkApplyTarget.SelectedTextRun);
    }

    [Fact]
    public void MainWindowSourceGuards_KeepPortableDomainSurfaceSemanticsInPresentation()
    {
        var root = FindWorkspaceRoot();
        var wpf = File.ReadAllText(Path.Combine(root, "freep", "FreeP.App.Host", "MainWindow.cs"));
        var avalonia = File.ReadAllText(Path.Combine(root, "freep", "FreeP.App.Avalonia", "MainWindow.cs"));

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("PresentationZoomAuthoringSession _zoomAuthoringSession");
            source.Should().Contain("PresentationDomainContextMenuSession _domainContextMenuSession");
            source.Should().Contain("PresentationNotesPaneSession _notesPaneSession");
            source.Should().Contain("PresentationHyperlinkWorkflowSession _hyperlinkWorkflowSession");
            source.Should().NotContain("SummaryZoomPreviewPlanner.AttachPreviewImage(");
            source.Should().NotContain("SummaryZoomPreviewPlanner.AttachPreviewImages(");
            source.Should().NotContain("Editor.SetSelectedZoomObjectProperties(");
            source.Should().NotContain("Editor.SetSummaryZoomTargets(");
            source.Should().NotContain("Editor.SetWaterfallPointTotal(");
            source.Should().NotContain("TableCellEditPlanner.PlanSelectedCell(");
            source.Should().NotContain("PresentationNotesPagePreviewPlanner.Build(");
            source.Should().NotContain("HyperlinkDialogPlanner.BuildDialogRequest(");
            source.Should().NotContain("Editor.SetShapeHyperlink(");
            source.Should().NotContain("private static string FormatNotesText(");
            source.Should().NotContain("\"Insert Row Above\"");
            source.Should().NotContain("\"Format Data Point...\"");
        }
    }

    private static PresentationZoomAuthoringSession CreateZoomSession(EditingSession editor) => new(
        () => editor,
        new PresentationZoomAuthoringSessionCallbacks(
            () => { },
            () => { },
            () => { },
            (_, slideIndex, _, _) => [(byte)(slideIndex + 1)]));

    private static PresentationDomainContextMenuSession CreateContextSession(
        EditingSession editor,
        Action<int, int>? openPoint = null) => new(
            () => editor,
            new PresentationDomainContextMenuSessionCallbacks(
                openPoint ?? ((_, _) => { }),
                _ => { },
                _ => { },
                _ => { },
                _ => { },
                () => { }));

    private static Presentation BuildZoomPresentation()
    {
        var presentation = new Presentation();
        for (var index = 1; index <= 4; index++)
        {
            presentation.Slides.Add(new Slide
            {
                Id = $"slide-{index}",
                NumericId = (uint)(255 + index),
                Title = $"Slide {index}",
            });
        }

        AddSection(presentation, "section-1", "One", "slide-1");
        AddSection(presentation, "section-2", "Two", "slide-2");
        AddSection(presentation, "section-3", "Three", "slide-3");
        return presentation;
    }

    private static void AddSection(
        Presentation presentation,
        string id,
        string name,
        params string[] slideIds)
    {
        var section = new PresentationSection { Id = id, Name = name };
        section.SlideIds.AddRange(slideIds);
        presentation.Sections.Add(section);
    }

    private static string FindWorkspaceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FreeP.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("FreeP workspace root not found.");
    }
}
