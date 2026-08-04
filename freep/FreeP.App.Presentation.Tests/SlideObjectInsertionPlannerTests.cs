using FreeP.App.Compositor;
using FreeP.Core.IO;

namespace FreeP.App.Compositor.Tests;

public sealed class SlideObjectInsertionPlannerTests
{
    private static EditingSession MakeSession()
    {
        var presentation = Presentation.CreateEmpty();
        return new EditingSession(presentation, new PresentationCommandBus(presentation));
    }

    [Fact]
    public void BuiltInCommandIds_AreUnique()
    {
        SlideObjectInsertionPlanner.BuiltInCommandIds
            .Should()
            .OnlyHaveUniqueItems();
    }

    [Theory]
    [InlineData(SlideObjectInsertionPlanner.TextBoxCommandId, SlideObjectInsertionKind.TextBox)]
    [InlineData(SlideObjectInsertionPlanner.RectangleCommandId, SlideObjectInsertionKind.AutoShape)]
    [InlineData(SlideObjectInsertionPlanner.RoundedRectangleCommandId, SlideObjectInsertionKind.AutoShape)]
    [InlineData(SlideObjectInsertionPlanner.EllipseCommandId, SlideObjectInsertionKind.AutoShape)]
    [InlineData(SlideObjectInsertionPlanner.TriangleCommandId, SlideObjectInsertionKind.AutoShape)]
    [InlineData(SlideObjectInsertionPlanner.DiamondCommandId, SlideObjectInsertionKind.AutoShape)]
    [InlineData(SlideObjectInsertionPlanner.HexagonCommandId, SlideObjectInsertionKind.AutoShape)]
    [InlineData(SlideObjectInsertionPlanner.ParallelogramCommandId, SlideObjectInsertionKind.AutoShape)]
    [InlineData(SlideObjectInsertionPlanner.TrapezoidCommandId, SlideObjectInsertionKind.AutoShape)]
    [InlineData(SlideObjectInsertionPlanner.LeftArrowCommandId, SlideObjectInsertionKind.AutoShape)]
    [InlineData(SlideObjectInsertionPlanner.RightArrowCommandId, SlideObjectInsertionKind.AutoShape)]
    [InlineData(SlideObjectInsertionPlanner.UpArrowCommandId, SlideObjectInsertionKind.AutoShape)]
    [InlineData(SlideObjectInsertionPlanner.DownArrowCommandId, SlideObjectInsertionKind.AutoShape)]
    [InlineData(SlideObjectInsertionPlanner.Star5CommandId, SlideObjectInsertionKind.AutoShape)]
    [InlineData(SlideObjectInsertionPlanner.CrossCommandId, SlideObjectInsertionKind.AutoShape)]
    [InlineData(SlideObjectInsertionPlanner.PlusSignCommandId, SlideObjectInsertionKind.AutoShape)]
    [InlineData(SlideObjectInsertionPlanner.PentagonCommandId, SlideObjectInsertionKind.AutoShape)]
    [InlineData(SlideObjectInsertionPlanner.OctagonCommandId, SlideObjectInsertionKind.AutoShape)]
    [InlineData(SlideObjectInsertionPlanner.LeftRightArrowCommandId, SlideObjectInsertionKind.AutoShape)]
    [InlineData(SlideObjectInsertionPlanner.UpDownArrowCommandId, SlideObjectInsertionKind.AutoShape)]
    [InlineData(SlideObjectInsertionPlanner.Star8CommandId, SlideObjectInsertionKind.AutoShape)]
    [InlineData(SlideObjectInsertionPlanner.ChevronCommandId, SlideObjectInsertionKind.AutoShape)]
    [InlineData(SlideObjectInsertionPlanner.HomePlateCommandId, SlideObjectInsertionKind.AutoShape)]
    [InlineData(SlideObjectInsertionPlanner.RightTriangleCommandId, SlideObjectInsertionKind.AutoShape)]
    [InlineData(SlideObjectInsertionPlanner.MinusSignCommandId, SlideObjectInsertionKind.AutoShape)]
    [InlineData(SlideObjectInsertionPlanner.MultiplySignCommandId, SlideObjectInsertionKind.AutoShape)]
    [InlineData(SlideObjectInsertionPlanner.DivideSignCommandId, SlideObjectInsertionKind.AutoShape)]
    [InlineData(SlideObjectInsertionPlanner.EqualSignCommandId, SlideObjectInsertionKind.AutoShape)]
    [InlineData(SlideObjectInsertionPlanner.NotEqualSignCommandId, SlideObjectInsertionKind.AutoShape)]
    [InlineData(SlideObjectInsertionPlanner.WaveCommandId, SlideObjectInsertionKind.AutoShape)]
    [InlineData(SlideObjectInsertionPlanner.RectangularCalloutCommandId, SlideObjectInsertionKind.AutoShape)]
    [InlineData(SlideObjectInsertionPlanner.RoundedRectangularCalloutCommandId, SlideObjectInsertionKind.AutoShape)]
    [InlineData(SlideObjectInsertionPlanner.OvalCalloutCommandId, SlideObjectInsertionKind.AutoShape)]
    [InlineData(SlideObjectInsertionPlanner.ExplosionCommandId, SlideObjectInsertionKind.AutoShape)]
    [InlineData(SlideObjectInsertionPlanner.RibbonCommandId, SlideObjectInsertionKind.AutoShape)]
    [InlineData(SlideObjectInsertionPlanner.FlowchartProcessCommandId, SlideObjectInsertionKind.AutoShape)]
    [InlineData(SlideObjectInsertionPlanner.FlowchartDecisionCommandId, SlideObjectInsertionKind.AutoShape)]
    [InlineData(SlideObjectInsertionPlanner.FlowchartDataCommandId, SlideObjectInsertionKind.AutoShape)]
    [InlineData(SlideObjectInsertionPlanner.FlowchartPredefinedProcessCommandId, SlideObjectInsertionKind.AutoShape)]
    [InlineData(SlideObjectInsertionPlanner.FlowchartDocumentCommandId, SlideObjectInsertionKind.AutoShape)]
    [InlineData(SlideObjectInsertionPlanner.FlowchartTerminatorCommandId, SlideObjectInsertionKind.AutoShape)]
    [InlineData(SlideObjectInsertionPlanner.LineCalloutCommandId, SlideObjectInsertionKind.AutoShape)]
    [InlineData(SlideObjectInsertionPlanner.CylinderCommandId, SlideObjectInsertionKind.AutoShape)]
    [InlineData(SlideObjectInsertionPlanner.ChordCommandId, SlideObjectInsertionKind.AutoShape)]
    [InlineData(SlideObjectInsertionPlanner.HeartCommandId, SlideObjectInsertionKind.AutoShape)]
    [InlineData(SlideObjectInsertionPlanner.ConnectorCommandId, SlideObjectInsertionKind.Connector)]
    [InlineData(SlideObjectInsertionPlanner.ElbowConnectorCommandId, SlideObjectInsertionKind.Connector)]
    [InlineData(SlideObjectInsertionPlanner.CurvedConnectorCommandId, SlideObjectInsertionKind.Connector)]
    [InlineData(SlideObjectInsertionPlanner.PictureCommandId, SlideObjectInsertionKind.Picture)]
    [InlineData(SlideObjectInsertionPlanner.VideoCommandId, SlideObjectInsertionKind.Media)]
    [InlineData(SlideObjectInsertionPlanner.AudioCommandId, SlideObjectInsertionKind.Media)]
    [InlineData(SlideObjectInsertionPlanner.Table3x3CommandId, SlideObjectInsertionKind.Table)]
    [InlineData(SlideObjectInsertionPlanner.Table2x2CommandId, SlideObjectInsertionKind.Table)]
    [InlineData(SlideObjectInsertionPlanner.Table4x4CommandId, SlideObjectInsertionKind.Table)]
    [InlineData(SlideObjectInsertionPlanner.ChartColumnCommandId, SlideObjectInsertionKind.Chart)]
    [InlineData(SlideObjectInsertionPlanner.ChartBarCommandId, SlideObjectInsertionKind.Chart)]
    [InlineData(SlideObjectInsertionPlanner.ChartLineCommandId, SlideObjectInsertionKind.Chart)]
    [InlineData(SlideObjectInsertionPlanner.ChartPieCommandId, SlideObjectInsertionKind.Chart)]
    [InlineData(SlideObjectInsertionPlanner.ChartOfPieCommandId, SlideObjectInsertionKind.Chart)]
    [InlineData(SlideObjectInsertionPlanner.SmartArtBasicProcessCommandId, SlideObjectInsertionKind.SmartArt)]
    public void TryCreatePlan_MapsKnownObjectCommandIds(
        string commandId,
        SlideObjectInsertionKind expectedKind)
    {
        SlideObjectInsertionPlanner.TryCreatePlan(commandId, out var plan).Should().BeTrue();
        plan.CommandId.Should().Be(commandId);
        plan.Kind.Should().Be(expectedKind);
    }

    [Theory]
    [InlineData(SlideObjectInsertionPlanner.TextBoxCommandId, DrawingShapeKind.Rectangle, true)]
    [InlineData(SlideObjectInsertionPlanner.RectangleCommandId, DrawingShapeKind.Rectangle, false)]
    [InlineData(SlideObjectInsertionPlanner.RoundedRectangleCommandId, DrawingShapeKind.RoundedRectangle, false)]
    [InlineData(SlideObjectInsertionPlanner.EllipseCommandId, DrawingShapeKind.Ellipse, false)]
    [InlineData(SlideObjectInsertionPlanner.TriangleCommandId, DrawingShapeKind.Triangle, false)]
    [InlineData(SlideObjectInsertionPlanner.DiamondCommandId, DrawingShapeKind.Diamond, false)]
    [InlineData(SlideObjectInsertionPlanner.HexagonCommandId, DrawingShapeKind.Hexagon, false)]
    [InlineData(SlideObjectInsertionPlanner.ParallelogramCommandId, DrawingShapeKind.Parallelogram, false)]
    [InlineData(SlideObjectInsertionPlanner.TrapezoidCommandId, DrawingShapeKind.Trapezoid, false)]
    [InlineData(SlideObjectInsertionPlanner.LeftArrowCommandId, DrawingShapeKind.LeftArrow, false)]
    [InlineData(SlideObjectInsertionPlanner.RightArrowCommandId, DrawingShapeKind.RightArrow, false)]
    [InlineData(SlideObjectInsertionPlanner.UpArrowCommandId, DrawingShapeKind.UpArrow, false)]
    [InlineData(SlideObjectInsertionPlanner.DownArrowCommandId, DrawingShapeKind.DownArrow, false)]
    [InlineData(SlideObjectInsertionPlanner.Star5CommandId, DrawingShapeKind.Star5, false)]
    [InlineData(SlideObjectInsertionPlanner.CrossCommandId, DrawingShapeKind.Cross, false)]
    [InlineData(SlideObjectInsertionPlanner.PlusSignCommandId, DrawingShapeKind.PlusSign, false)]
    [InlineData(SlideObjectInsertionPlanner.PentagonCommandId, DrawingShapeKind.Pentagon, false)]
    [InlineData(SlideObjectInsertionPlanner.OctagonCommandId, DrawingShapeKind.Octagon, false)]
    [InlineData(SlideObjectInsertionPlanner.LeftRightArrowCommandId, DrawingShapeKind.LeftRightArrow, false)]
    [InlineData(SlideObjectInsertionPlanner.UpDownArrowCommandId, DrawingShapeKind.UpDownArrow, false)]
    [InlineData(SlideObjectInsertionPlanner.Star8CommandId, DrawingShapeKind.Star8, false)]
    [InlineData(SlideObjectInsertionPlanner.ChevronCommandId, DrawingShapeKind.Chevron, false)]
    [InlineData(SlideObjectInsertionPlanner.HomePlateCommandId, DrawingShapeKind.HomePlate, false)]
    [InlineData(SlideObjectInsertionPlanner.RightTriangleCommandId, DrawingShapeKind.RightTriangle, false)]
    [InlineData(SlideObjectInsertionPlanner.MinusSignCommandId, DrawingShapeKind.MinusSign, false)]
    [InlineData(SlideObjectInsertionPlanner.MultiplySignCommandId, DrawingShapeKind.MultiplySign, false)]
    [InlineData(SlideObjectInsertionPlanner.DivideSignCommandId, DrawingShapeKind.DivideSign, false)]
    [InlineData(SlideObjectInsertionPlanner.EqualSignCommandId, DrawingShapeKind.EqualSign, false)]
    [InlineData(SlideObjectInsertionPlanner.NotEqualSignCommandId, DrawingShapeKind.NotEqualSign, false)]
    [InlineData(SlideObjectInsertionPlanner.WaveCommandId, DrawingShapeKind.Wave, false)]
    [InlineData(SlideObjectInsertionPlanner.RectangularCalloutCommandId, DrawingShapeKind.RectangularCallout, false)]
    [InlineData(SlideObjectInsertionPlanner.RoundedRectangularCalloutCommandId, DrawingShapeKind.RoundedRectangularCallout, false)]
    [InlineData(SlideObjectInsertionPlanner.OvalCalloutCommandId, DrawingShapeKind.OvalCallout, false)]
    [InlineData(SlideObjectInsertionPlanner.ExplosionCommandId, DrawingShapeKind.Explosion, false)]
    [InlineData(SlideObjectInsertionPlanner.RibbonCommandId, DrawingShapeKind.Ribbon, false)]
    [InlineData(SlideObjectInsertionPlanner.FlowchartProcessCommandId, DrawingShapeKind.FlowchartProcess, false)]
    [InlineData(SlideObjectInsertionPlanner.FlowchartDecisionCommandId, DrawingShapeKind.FlowchartDecision, false)]
    [InlineData(SlideObjectInsertionPlanner.FlowchartDataCommandId, DrawingShapeKind.FlowchartData, false)]
    [InlineData(SlideObjectInsertionPlanner.FlowchartPredefinedProcessCommandId, DrawingShapeKind.FlowchartPredefinedProcess, false)]
    [InlineData(SlideObjectInsertionPlanner.FlowchartDocumentCommandId, DrawingShapeKind.FlowchartDocument, false)]
    [InlineData(SlideObjectInsertionPlanner.FlowchartTerminatorCommandId, DrawingShapeKind.FlowchartTerminator, false)]
    [InlineData(SlideObjectInsertionPlanner.LineCalloutCommandId, DrawingShapeKind.LineCallout, false)]
    [InlineData(SlideObjectInsertionPlanner.CylinderCommandId, DrawingShapeKind.Cylinder, false)]
    [InlineData(SlideObjectInsertionPlanner.ChordCommandId, DrawingShapeKind.Chord, false)]
    [InlineData(SlideObjectInsertionPlanner.HeartCommandId, DrawingShapeKind.Heart, false)]
    public void ApplyCommand_InsertsExpectedAutoShape(
        string commandId,
        DrawingShapeKind expectedShape,
        bool expectsTextBody)
    {
        var editor = MakeSession();
        var before = editor.CurrentSlide!.Shapes.Count;

        var added = SlideObjectInsertionPlanner.ApplyCommand(editor, commandId);

        added.Should().NotBeNull();
        editor.CurrentSlide.Shapes.Should().HaveCount(before + 1);
        added!.Kind.Should().Be(SlideShapeKind.AutoShape);
        added.AutoShapeKind.Should().Be(expectedShape);
        (added.TextBody is not null).Should().Be(expectsTextBody);
    }

    [Fact]
    public void ApplyCommand_InsertsFreeConnector()
    {
        var editor = MakeSession();
        var before = editor.CurrentSlide!.Shapes.Count;

        var added = SlideObjectInsertionPlanner.ApplyCommand(
            editor,
            SlideObjectInsertionPlanner.ConnectorCommandId);

        added.Should().NotBeNull();
        added!.Kind.Should().Be(SlideShapeKind.Connector);
        added.AutoShapeKind.Should().Be(DrawingShapeKind.Line);
        added.ConnectionStart.Should().BeNull();
        added.ConnectionEnd.Should().BeNull();
        editor.Undo();
        editor.CurrentSlide!.Shapes.Should().HaveCount(before);
    }

    [Theory]
    [InlineData(SlideObjectInsertionPlanner.ElbowConnectorCommandId, DrawingShapeKind.ElbowConnector)]
    [InlineData(SlideObjectInsertionPlanner.CurvedConnectorCommandId, DrawingShapeKind.CurvedConnector)]
    public void ApplyCommand_InsertsConnectorVariant(string commandId, DrawingShapeKind expectedKind)
    {
        var editor = MakeSession();

        var added = SlideObjectInsertionPlanner.ApplyCommand(editor, commandId);

        added.Should().NotBeNull();
        added!.Kind.Should().Be(SlideShapeKind.Connector);
        added.AutoShapeKind.Should().Be(expectedKind);
    }

    [Theory]
    [InlineData(SlideObjectInsertionPlanner.Table3x3CommandId, 3, 3)]
    [InlineData(SlideObjectInsertionPlanner.Table2x2CommandId, 2, 2)]
    [InlineData(SlideObjectInsertionPlanner.Table4x4CommandId, 4, 4)]
    public void ApplyCommand_InsertsExpectedTable(string commandId, int rows, int columns)
    {
        var editor = MakeSession();

        var added = SlideObjectInsertionPlanner.ApplyCommand(editor, commandId);

        added.Should().NotBeNull();
        added!.Kind.Should().Be(SlideShapeKind.Table);
        added.Table.Should().NotBeNull();
        added.Table!.Rows.Should().HaveCount(rows);
        added.Table.ColumnWidthsEmu.Should().HaveCount(columns);
    }

    [Theory]
    [InlineData(SlideObjectInsertionPlanner.ChartColumnCommandId, ChartType.ColumnClustered)]
    [InlineData(SlideObjectInsertionPlanner.ChartBarCommandId, ChartType.BarClustered)]
    [InlineData(SlideObjectInsertionPlanner.ChartLineCommandId, ChartType.Line)]
    [InlineData(SlideObjectInsertionPlanner.ChartPieCommandId, ChartType.Pie)]
    [InlineData(SlideObjectInsertionPlanner.ChartOfPieCommandId, ChartType.OfPie)]
    [InlineData(SlideObjectInsertionPlanner.ChartColumnStackedCommandId, ChartType.ColumnStacked)]
    [InlineData(SlideObjectInsertionPlanner.ChartColumnStacked100CommandId, ChartType.ColumnStacked100)]
    [InlineData(SlideObjectInsertionPlanner.ChartBarStackedCommandId, ChartType.BarStacked)]
    [InlineData(SlideObjectInsertionPlanner.ChartBarStacked100CommandId, ChartType.BarStacked100)]
    [InlineData(SlideObjectInsertionPlanner.ChartLineMarkersCommandId, ChartType.LineMarkers)]
    [InlineData(SlideObjectInsertionPlanner.ChartAreaCommandId, ChartType.Area)]
    [InlineData(SlideObjectInsertionPlanner.ChartAreaStackedCommandId, ChartType.AreaStacked)]
    [InlineData(SlideObjectInsertionPlanner.ChartScatterCommandId, ChartType.Scatter)]
    [InlineData(SlideObjectInsertionPlanner.ChartDoughnutCommandId, ChartType.Doughnut)]
    [InlineData(SlideObjectInsertionPlanner.ChartRadarCommandId, ChartType.Radar)]
    [InlineData(SlideObjectInsertionPlanner.ChartBubbleCommandId, ChartType.Bubble)]
    [InlineData(SlideObjectInsertionPlanner.ChartStockCommandId, ChartType.Stock)]
    [InlineData(SlideObjectInsertionPlanner.ChartSurfaceCommandId, ChartType.Surface)]
    [InlineData(SlideObjectInsertionPlanner.ChartSurface3DCommandId, ChartType.Surface3D)]
    [InlineData(SlideObjectInsertionPlanner.ChartFunnelCommandId, ChartType.Funnel)]
    [InlineData(SlideObjectInsertionPlanner.ChartWaterfallCommandId, ChartType.Waterfall)]
    public void ApplyCommand_InsertsExpectedChart(string commandId, ChartType chartType)
    {
        var editor = MakeSession();

        var added = SlideObjectInsertionPlanner.ApplyCommand(editor, commandId);

        added.Should().NotBeNull();
        added!.Kind.Should().Be(SlideShapeKind.Chart);
        added.Chart.Should().NotBeNull();
        added.Chart!.ChartType.Should().Be(chartType);
    }

    [Fact]
    public void ApplyCommand_InsertsDefaultComboChartWithSecondaryLineAndUndo()
    {
        var editor = MakeSession();
        var before = editor.CurrentSlide!.Shapes.Count;

        var added = SlideObjectInsertionPlanner.ApplyCommand(
            editor,
            SlideObjectInsertionPlanner.ChartComboCommandId);

        added.Should().NotBeNull();
        added!.Chart!.ChartType.Should().Be(ChartType.ColumnClustered);
        added.Chart.Series.Should().HaveCount(2);
        added.Chart.Series[1].OverrideChartType.Should().Be(ChartType.LineMarkers);
        added.Chart.Series[1].OnSecondaryAxis.Should().BeTrue();
        editor.CurrentSlide.Shapes.Should().HaveCount(before + 1);

        editor.Undo();
        editor.CurrentSlide.Shapes.Should().HaveCount(before);
    }

    [Fact]
    public void ApplyCommand_PictureWithoutPayload_IsNoOp()
    {
        var editor = MakeSession();
        var before = editor.CurrentSlide!.Shapes.Count;

        var added = SlideObjectInsertionPlanner.ApplyCommand(
            editor,
            SlideObjectInsertionPlanner.PictureCommandId);

        added.Should().BeNull();
        editor.CurrentSlide.Shapes.Should().HaveCount(before);
    }

    [Fact]
    public void ApplyCommand_InsertsNativeSmartArt_WithUndoAndDistinctPartNames()
    {
        var editor = MakeSession();
        var initialCount = editor.CurrentSlide!.Shapes.Count;

        var first = SlideObjectInsertionPlanner.ApplyCommand(
            editor, SlideObjectInsertionPlanner.SmartArtBasicProcessCommandId);
        var second = SlideObjectInsertionPlanner.ApplyCommand(
            editor, SlideObjectInsertionPlanner.SmartArtBasicProcessCommandId);

        first.Should().NotBeNull();
        second.Should().NotBeNull();
        first!.Kind.Should().Be(SlideShapeKind.SmartArt);
        first.SmartArt!.Data!.Family.Should().Be(SmartArtFamily.Process);
        new[] { first.SmartArt.Data.Nodes[0].Text }
            .Concat(first.SmartArt.Data.Nodes[0].Children.Select(node => node.Text))
            .Should().Equal("Step 1", "Step 2", "Step 3");
        first.SmartArt.Parts.Keys.Should().Contain("ppt/diagrams/data1.xml");
        second!.SmartArt!.Parts.Keys.Should().Contain("ppt/diagrams/data2.xml");
        first.SmartArt.PartRels.Should().ContainKey("ppt/diagrams/data1.xml");

        editor.Undo();
        editor.CurrentSlide!.Shapes.Should().HaveCount(initialCount + 1);
        editor.Undo();
        editor.CurrentSlide.Shapes.Should().HaveCount(initialCount);
        editor.Redo();
        editor.CurrentSlide.Shapes.Should().Contain(shape => shape.Kind == SlideShapeKind.SmartArt);
    }

    [Fact]
    public void ApplyCommand_InsertsEveryLiveSmartArtLayoutPreset()
    {
        foreach (var preset in SlideObjectInsertionPlanner.InsertableSmartArtLayouts)
        {
            var editor = MakeSession();
            var commandId = preset == SmartArtLayoutPreset.BasicProcess
                ? SlideObjectInsertionPlanner.SmartArtBasicProcessCommandId
                : SlideObjectInsertionPlanner.SmartArtLayoutCommandId(preset);

            var added = SlideObjectInsertionPlanner.ApplyCommand(
                editor,
                commandId,
                smartArtPicturePayload: preset is (SmartArtLayoutPreset.PictureAccentProcess or SmartArtLayoutPreset.PictureCaptionList or SmartArtLayoutPreset.PictureAccentList or SmartArtLayoutPreset.PictureStack or SmartArtLayoutPreset.PictureLineup or SmartArtLayoutPreset.PictureStrips or SmartArtLayoutPreset.ContinuousPictureList or SmartArtLayoutPreset.PictureGrid)
                    ? SlideObjectInsertionPlanner.CreateSmartArtPicturePayload(
                        [SlideObjectInsertionPlanner.CreatePicturePayload([1, 2, 3], "sample.png")])
                    : null);

            added.Should().NotBeNull(preset.ToString());
            added!.Kind.Should().Be(SlideShapeKind.SmartArt);
            added.SmartArt!.Data!.Family.Should().NotBe(SmartArtFamily.Unknown, preset.ToString());
            added.SmartArt.Data.LayoutUniqueId.Should().Contain("/layout/", preset.ToString());
        }
    }

    [Fact]
    public void ApplyCommand_BasicMatrixUsesFlatComponentsAndRoundTripsItsLiveQuadrants()
    {
        var editor = MakeSession();
        var added = SlideObjectInsertionPlanner.ApplyCommand(
            editor,
            SlideObjectInsertionPlanner.SmartArtLayoutCommandId(SmartArtLayoutPreset.BasicMatrix));

        added.Should().NotBeNull();
        var authored = added!.SmartArt!;
        authored.Data!.Nodes.Should().HaveCount(3);
        authored.Data.Nodes.Should().OnlyContain(node => node.Level == 0 && node.Children.Count == 0);
        var diagram = System.Xml.Linq.XDocument.Parse(
            System.Text.Encoding.UTF8.GetString(authored.Parts["ppt/diagrams/data1.xml"].Bytes));
        var diagramNamespace = System.Xml.Linq.XNamespace.Get(
            "http://schemas.openxmlformats.org/drawingml/2006/diagram");
        diagram.Descendants(diagramNamespace + "cxn").Should().BeEmpty(
            "Basic Matrix components are authored as flat siblings");

        var live = SmartArtLayoutEngine.Layout(
            authored.Data,
            added.OffsetXEmu,
            added.OffsetYEmu,
            added.ExtentCxEmu,
            added.ExtentCyEmu,
            editor.Presentation.Theme!);
        live.Should().HaveCount(4, "Basic Matrix emits one whole diamond plus one quadrant per authored component");
        live!.Skip(1).Select(shape => shape.PlainText)
            .Should().Equal("Step 1", "Step 2", "Step 3");

        using var package = new MemoryStream();
        PptxPackageWriter.Write(editor.Presentation, package);
        package.Position = 0;
        var reopened = PptxPackageReader.Read(package);
        var reread = reopened.Slides[0].Shapes.Single(shape => shape.Kind == SlideShapeKind.SmartArt);
        var rereadSmartArt = reread.SmartArt!;

        rereadSmartArt.Data!.LayoutUniqueId.Should().Contain("/layout/basicMatrix");
        rereadSmartArt.Data.Nodes.Should().HaveCount(3);
        rereadSmartArt.Data.Nodes.Should().OnlyContain(node => node.Level == 0 && node.Children.Count == 0);
        var rereadLive = SmartArtLayoutEngine.Layout(
            rereadSmartArt.Data,
            reread.OffsetXEmu,
            reread.OffsetYEmu,
            reread.ExtentCxEmu,
            reread.ExtentCyEmu,
            reopened.Theme!);
        rereadLive.Should().HaveCount(4);
        rereadLive!.Skip(1).Select(shape => shape.PlainText)
            .Should().Equal("Step 1", "Step 2", "Step 3");
    }

    [Fact]
    public void ApplyCommand_AccentProcessUsesMainAccentTopologyAndRoundTripsItsLiveRoles()
    {
        var editor = MakeSession();
        var added = SlideObjectInsertionPlanner.ApplyCommand(
            editor,
            SlideObjectInsertionPlanner.SmartArtLayoutCommandId(SmartArtLayoutPreset.AccentProcess));

        added.Should().NotBeNull();
        var authored = added!.SmartArt!;
        authored.Data!.LayoutUniqueId.Should().Contain("/layout/accentProcess");
        authored.Data.Nodes.Should().HaveCount(3);
        authored.Data.Nodes.Should().OnlyContain(node =>
            node.ModelId.StartsWith("main-", StringComparison.Ordinal)
            && node.Level == 0
            && string.IsNullOrEmpty(node.Text)
            && node.Children.Count == 1);
        authored.Data.Nodes.SelectMany(node => node.Children)
            .Select(node => node.ModelId)
            .Should().Equal("accent-1", "accent-2", "accent-3");

        var diagram = System.Xml.Linq.XDocument.Parse(
            System.Text.Encoding.UTF8.GetString(authored.Parts["ppt/diagrams/data1.xml"].Bytes));
        var diagramNamespace = System.Xml.Linq.XNamespace.Get(
            "http://schemas.openxmlformats.org/drawingml/2006/diagram");
        diagram.Descendants(diagramNamespace + "cxn").Should().HaveCount(3);

        var live = SmartArtLayoutEngine.Layout(
            authored.Data,
            added.OffsetXEmu,
            added.OffsetYEmu,
            added.ExtentCxEmu,
            added.ExtentCyEmu,
            editor.Presentation.Theme!);
        live.Should().HaveCount(8, "Accent Process emits two stage roles and two transitions per authored stage sequence");
        live!.Where(shape => shape.Name.Contains("_Main_", StringComparison.Ordinal))
            .Should().HaveCount(3);
        live.Where(shape => shape.Name.Contains("_Accent_", StringComparison.Ordinal))
            .Select(shape => shape.PlainText)
            .Should().Equal("Step 1", "Step 2", "Step 3");

        using var package = new MemoryStream();
        PptxPackageWriter.Write(editor.Presentation, package);
        package.Position = 0;
        var reopened = PptxPackageReader.Read(package);
        var reread = reopened.Slides[0].Shapes.Single(shape => shape.Kind == SlideShapeKind.SmartArt);
        var rereadSmartArt = reread.SmartArt!;

        rereadSmartArt.Data!.LayoutUniqueId.Should().Contain("/layout/accentProcess");
        rereadSmartArt.Data.IsLiveLayoutSupported.Should().BeTrue();
        rereadSmartArt.Data.Nodes.Select(node => node.ModelId)
            .Should().Equal("main-1", "main-2", "main-3");
        SmartArtLayoutEngine.Layout(
            rereadSmartArt.Data,
            reread.OffsetXEmu,
            reread.OffsetYEmu,
            reread.ExtentCxEmu,
            reread.ExtentCyEmu,
            reopened.Theme!).Should().HaveCount(8);
    }

    [Fact]
    public void ApplyCommand_TitledMatrixUsesFlatTitleAndBodyComponentsAndRoundTripsItsLiveCells()
    {
        var editor = MakeSession();
        var added = SlideObjectInsertionPlanner.ApplyCommand(
            editor,
            SlideObjectInsertionPlanner.SmartArtLayoutCommandId(SmartArtLayoutPreset.TitledMatrix));

        added.Should().NotBeNull();
        var authored = added!.SmartArt!;
        authored.Data!.Nodes.Should().HaveCount(3);
        authored.Data.Nodes.Should().OnlyContain(node => node.Level == 0 && node.Children.Count == 0);
        var diagram = System.Xml.Linq.XDocument.Parse(
            System.Text.Encoding.UTF8.GetString(authored.Parts["ppt/diagrams/data1.xml"].Bytes));
        var diagramNamespace = System.Xml.Linq.XNamespace.Get(
            "http://schemas.openxmlformats.org/drawingml/2006/diagram");
        diagram.Descendants(diagramNamespace + "cxn").Should().BeEmpty(
            "Titled Matrix authors its title and body cells as flat siblings");

        var live = SmartArtLayoutEngine.Layout(
            authored.Data,
            added.OffsetXEmu,
            added.OffsetYEmu,
            added.ExtentCxEmu,
            added.ExtentCyEmu,
            editor.Presentation.Theme!);
        live.Should().HaveCount(3, "Titled Matrix emits one title band and one body cell per remaining authored component");
        live!.Select(shape => shape.PlainText)
            .Should().Equal("Step 1", "Step 2", "Step 3");

        using var package = new MemoryStream();
        PptxPackageWriter.Write(editor.Presentation, package);
        package.Position = 0;
        var reopened = PptxPackageReader.Read(package);
        var reread = reopened.Slides[0].Shapes.Single(shape => shape.Kind == SlideShapeKind.SmartArt);
        var rereadSmartArt = reread.SmartArt!;

        rereadSmartArt.Data!.LayoutUniqueId.Should().Contain("/layout/titledMatrix");
        rereadSmartArt.Data.Nodes.Should().HaveCount(3);
        rereadSmartArt.Data.Nodes.Should().OnlyContain(node => node.Level == 0 && node.Children.Count == 0);
        var rereadLive = SmartArtLayoutEngine.Layout(
            rereadSmartArt.Data,
            reread.OffsetXEmu,
            reread.OffsetYEmu,
            reread.ExtentCxEmu,
            reread.ExtentCyEmu,
            reopened.Theme!);
        rereadLive.Should().HaveCount(3);
        rereadLive!.Select(shape => shape.PlainText)
            .Should().Equal("Step 1", "Step 2", "Step 3");
    }

    [Fact]
    public void ApplyCommand_GridMatrixUsesFlatComponentsAndRoundTripsItsLiveQuadrants()
    {
        var editor = MakeSession();
        var added = SlideObjectInsertionPlanner.ApplyCommand(
            editor,
            SlideObjectInsertionPlanner.SmartArtLayoutCommandId(SmartArtLayoutPreset.GridMatrix));

        added.Should().NotBeNull();
        var authored = added!.SmartArt!;
        authored.Data!.Family.Should().Be(SmartArtFamily.Matrix);
        authored.Data.LayoutUniqueId.Should().Contain("/layout/gridMatrix");
        authored.Data.Nodes.Should().HaveCount(3);
        authored.Data.Nodes.Should().OnlyContain(node => node.Level == 0 && node.Children.Count == 0);
        var diagram = System.Xml.Linq.XDocument.Parse(
            System.Text.Encoding.UTF8.GetString(authored.Parts["ppt/diagrams/data1.xml"].Bytes));
        var diagramNamespace = System.Xml.Linq.XNamespace.Get(
            "http://schemas.openxmlformats.org/drawingml/2006/diagram");
        diagram.Descendants(diagramNamespace + "cxn").Should().BeEmpty(
            "Grid Matrix authors its quadrant components as flat siblings");

        var live = SmartArtLayoutEngine.Layout(
            authored.Data,
            added.OffsetXEmu,
            added.OffsetYEmu,
            added.ExtentCxEmu,
            added.ExtentCyEmu,
            editor.Presentation.Theme!);
        live.Should().HaveCount(3, "Grid Matrix emits one live cell per authored component in its four-quadrant envelope");
        live!.Select(shape => shape.PlainText)
            .Should().Equal("Step 1", "Step 2", "Step 3");
        live.Should().OnlyContain(shape => shape.Kind == SlideShapeKind.AutoShape
            && shape.AutoShapeKind == DrawingShapeKind.Rectangle);
        live.Select(shape => shape.OffsetXEmu).Distinct().Should().HaveCount(2);
        live.Select(shape => shape.OffsetYEmu).Distinct().Should().HaveCount(2);
        live.Select(shape => shape.ExtentCxEmu).Should().Equal(live.Select(shape => shape.ExtentCyEmu));

        using var package = new MemoryStream();
        PptxPackageWriter.Write(editor.Presentation, package);
        package.Position = 0;
        var reopened = PptxPackageReader.Read(package);
        var reread = reopened.Slides[0].Shapes.Single(shape => shape.Kind == SlideShapeKind.SmartArt);
        var rereadSmartArt = reread.SmartArt!;

        rereadSmartArt.Data!.Family.Should().Be(SmartArtFamily.Matrix);
        rereadSmartArt.Data.LayoutUniqueId.Should().Contain("/layout/gridMatrix");
        rereadSmartArt.Data.IsLiveLayoutSupported.Should().BeTrue();
        rereadSmartArt.Data.Nodes.Should().HaveCount(3);
        rereadSmartArt.Data.Nodes.Should().OnlyContain(node => node.Level == 0 && node.Children.Count == 0);
        var rereadLive = SmartArtLayoutEngine.Layout(
            rereadSmartArt.Data,
            reread.OffsetXEmu,
            reread.OffsetYEmu,
            reread.ExtentCxEmu,
            reread.ExtentCyEmu,
            reopened.Theme!);
        rereadLive.Should().HaveCount(3);
        rereadLive!.Select(shape => shape.PlainText)
            .Should().Equal("Step 1", "Step 2", "Step 3");
    }

    [Fact]
    public void ApplyCommand_InsertsPictureCaptionListWithOneImagePerNode()
    {
        var editor = MakeSession();
        var payload = SlideObjectInsertionPlanner.CreateSmartArtPicturePayload(
            [SlideObjectInsertionPlanner.CreatePicturePayload([1, 2, 3], "sample.png")]);

        var added = SlideObjectInsertionPlanner.ApplyCommand(
            editor,
            SlideObjectInsertionPlanner.SmartArtLayoutCommandId(SmartArtLayoutPreset.PictureCaptionList),
            smartArtPicturePayload: payload);

        added.Should().NotBeNull();
        added!.SmartArt!.Data!.LayoutUniqueId.Should().Contain("pictureCaptionList");
        added.SmartArt.Data.IsLiveLayoutSupported.Should().BeTrue();
        Flatten(added.SmartArt.Data.Nodes).Select(node => node.Picture).Should()
            .AllSatisfy(picture => picture.Should().NotBeNull());
        added.SmartArt.Parts.Values.Should().Contain(part => part.ContentType == "image/png");

        using var package = new MemoryStream();
        FreeP.Core.IO.PptxPackageWriter.Write(editor.Presentation, package);
        package.Position = 0;
        var reopened = FreeP.Core.IO.PptxPackageReader.Read(package);
        var reopenedSmartArt = reopened.Slides[0].Shapes.Single(shape => shape.Kind == SlideShapeKind.SmartArt).SmartArt!;
        reopenedSmartArt.Data!.IsLiveLayoutSupported.Should().BeTrue();
        Flatten(reopenedSmartArt.Data.Nodes).Select(node => node.Picture!.Bytes).Should()
            .AllSatisfy(bytes => bytes.Should().Equal(1, 2, 3));

        static IEnumerable<SmartArtNode> Flatten(IEnumerable<SmartArtNode> nodes)
        {
            foreach (var node in nodes)
            {
                yield return node;
                foreach (var child in Flatten(node.Children))
                    yield return child;
            }
        }
    }

    [Fact]
    public void ApplyCommand_InsertsPictureAccentListWithOneImagePerNode()
    {
        var editor = MakeSession();
        var payload = SlideObjectInsertionPlanner.CreateSmartArtPicturePayload(
            [SlideObjectInsertionPlanner.CreatePicturePayload([1, 2, 3], "sample.png")]);

        var added = SlideObjectInsertionPlanner.ApplyCommand(
            editor,
            SlideObjectInsertionPlanner.SmartArtLayoutCommandId(SmartArtLayoutPreset.PictureAccentList),
            smartArtPicturePayload: payload);

        added.Should().NotBeNull();
        added!.SmartArt!.Data!.LayoutUniqueId.Should().Contain("pictureAccentList");
        added.SmartArt.Data.IsLiveLayoutSupported.Should().BeTrue();
        added.SmartArt.Data.Nodes.Select(node => node.Picture).Should()
            .AllSatisfy(picture => picture.Should().NotBeNull());
    }

    [Fact]
    public void ApplyCommand_InsertsPictureStackWithOneImagePerNode()
    {
        var editor = MakeSession();
        var payload = SlideObjectInsertionPlanner.CreateSmartArtPicturePayload(
            [SlideObjectInsertionPlanner.CreatePicturePayload([1, 2, 3], "sample.png")]);

        var added = SlideObjectInsertionPlanner.ApplyCommand(
            editor,
            SlideObjectInsertionPlanner.SmartArtLayoutCommandId(SmartArtLayoutPreset.PictureStack),
            smartArtPicturePayload: payload);

        added.Should().NotBeNull();
        added!.SmartArt!.Data!.LayoutUniqueId.Should().Contain("pictureStack");
        added.SmartArt.Data.IsLiveLayoutSupported.Should().BeTrue();
        added.SmartArt.Data.Nodes.Select(node => node.Picture).Should()
            .AllSatisfy(picture => picture.Should().NotBeNull());
    }

    [Fact]
    public void ApplyCommand_InsertsPictureLineupWithOneImagePerNode()
    {
        var editor = MakeSession();
        var payload = SlideObjectInsertionPlanner.CreateSmartArtPicturePayload(
            [SlideObjectInsertionPlanner.CreatePicturePayload([1, 2, 3], "sample.png")]);

        var added = SlideObjectInsertionPlanner.ApplyCommand(
            editor,
            SlideObjectInsertionPlanner.SmartArtLayoutCommandId(SmartArtLayoutPreset.PictureLineup),
            smartArtPicturePayload: payload);

        added.Should().NotBeNull();
        added!.SmartArt!.Data!.LayoutUniqueId.Should().Contain("pictureLineup");
        added.SmartArt.Data.IsLiveLayoutSupported.Should().BeTrue();
        added.SmartArt.Data.Nodes.Select(node => node.Picture).Should()
            .AllSatisfy(picture => picture.Should().NotBeNull());
    }

    [Fact]
    public void ApplyCommand_InsertsPictureCaptionListWithPlaceholdersWithoutPayload()
    {
        var editor = MakeSession();

        var added = SlideObjectInsertionPlanner.ApplyCommand(
            editor,
            SlideObjectInsertionPlanner.SmartArtLayoutCommandId(SmartArtLayoutPreset.PictureCaptionList));

        added.Should().NotBeNull();
        added!.SmartArt!.Data!.Nodes.SelectMany(node => new[] { node }.Concat(node.Children))
            .Should().HaveCount(3);
        added.SmartArt.Data.Nodes.SelectMany(node => new[] { node }.Concat(node.Children))
            .Select(node => node.Picture)
            .Should().OnlyContain(picture => picture == null);
        added.SmartArt.FallbackShapes.Select(shape => shape.PlainText)
            .Should().Contain("Add picture");
        editor.Bus.CanUndo.Should().BeTrue();
        editor.Undo();
        editor.CurrentSlide!.Shapes.Should().NotContain(shape => shape.Kind == SlideShapeKind.SmartArt);
        editor.Redo();
        editor.CurrentSlide.Shapes.Should().ContainSingle(shape => shape.Kind == SlideShapeKind.SmartArt);

        using var package = new MemoryStream();
        FreeP.Core.IO.PptxPackageWriter.Write(editor.Presentation, package);
        package.Position = 0;
        var reopened = FreeP.Core.IO.PptxPackageReader.Read(package);
        var reopenedSmartArt = reopened.Slides[0].Shapes.Single(shape => shape.Kind == SlideShapeKind.SmartArt).SmartArt!;
        reopenedSmartArt.Data!.Nodes.SelectMany(node => new[] { node }.Concat(node.Children))
            .Select(node => node.Picture)
            .Should().OnlyContain(picture => picture == null);
        reopenedSmartArt.FallbackShapes.Select(shape => shape.PlainText)
            .Should().Contain("Add picture");
    }

    [Fact]
    public void ApplyCommand_PictureCaptionList_IsUndoable()
    {
        var editor = MakeSession();
        var payload = SlideObjectInsertionPlanner.CreateSmartArtPicturePayload(
            [SlideObjectInsertionPlanner.CreatePicturePayload([1, 2, 3], "sample.png")]);

        SlideObjectInsertionPlanner.ApplyCommand(
            editor,
            SlideObjectInsertionPlanner.SmartArtLayoutCommandId(SmartArtLayoutPreset.PictureCaptionList),
            smartArtPicturePayload: payload).Should().NotBeNull();
        editor.CurrentSlide!.Shapes.Should().ContainSingle(shape => shape.Kind == SlideShapeKind.SmartArt);

        editor.Undo();
        editor.CurrentSlide.Shapes.Should().NotContain(shape => shape.Kind == SlideShapeKind.SmartArt);
        editor.Redo();
        editor.CurrentSlide.Shapes.Should().ContainSingle(shape => shape.Kind == SlideShapeKind.SmartArt);
    }

    [Fact]
    public void ApplyCommand_InsertsEveryLiveSmartArtLayoutPreset_AndRoundTrips()
    {
        var editor = MakeSession();
        foreach (var preset in SlideObjectInsertionPlanner.InsertableSmartArtLayouts)
        {
            var commandId = preset == SmartArtLayoutPreset.BasicProcess
                ? SlideObjectInsertionPlanner.SmartArtBasicProcessCommandId
                : SlideObjectInsertionPlanner.SmartArtLayoutCommandId(preset);

            SlideObjectInsertionPlanner.ApplyCommand(
                editor,
                commandId,
                smartArtPicturePayload: preset is (SmartArtLayoutPreset.PictureAccentProcess or SmartArtLayoutPreset.PictureCaptionList or SmartArtLayoutPreset.PictureAccentList or SmartArtLayoutPreset.PictureStack or SmartArtLayoutPreset.PictureLineup or SmartArtLayoutPreset.PictureStrips or SmartArtLayoutPreset.ContinuousPictureList or SmartArtLayoutPreset.PictureGrid)
                    ? SlideObjectInsertionPlanner.CreateSmartArtPicturePayload(
                        [SlideObjectInsertionPlanner.CreatePicturePayload([1, 2, 3], "sample.png")])
                    : null).Should().NotBeNull(preset.ToString());
        }

        using var package = new MemoryStream();
        FreeP.Core.IO.PptxPackageWriter.Write(editor.Presentation, package);
        package.Position = 0;

        var reopened = FreeP.Core.IO.PptxPackageReader.Read(package);
        var smartArts = reopened.Slides[0].Shapes
            .Where(shape => shape.Kind == SlideShapeKind.SmartArt)
            .Select(shape => shape.SmartArt!.Data!.LayoutUniqueId)
            .ToArray();

        smartArts.Should().HaveCount(SlideObjectInsertionPlanner.InsertableSmartArtLayouts.Count);
        smartArts.Should().OnlyContain(layout => layout.Contains("/layout/"));
    }

    [Fact]
    public void ApplyCommand_InsertsSmartArt_RoundTripsNativeDiagramParts()
    {
        var editor = MakeSession();
        SlideObjectInsertionPlanner.ApplyCommand(
            editor, SlideObjectInsertionPlanner.SmartArtBasicProcessCommandId).Should().NotBeNull();

        using var package = new MemoryStream();
        FreeP.Core.IO.PptxPackageWriter.Write(editor.Presentation, package);
        package.Position = 0;

        var reopened = FreeP.Core.IO.PptxPackageReader.Read(package);
        var smart = reopened.Slides[0].Shapes.Single(shape => shape.Kind == SlideShapeKind.SmartArt).SmartArt!;

        smart.Data!.Family.Should().Be(SmartArtFamily.Process);
        smart.Data.LayoutUniqueId.Should().Contain("layout/process1");
        Flatten(smart.Data.Nodes).Select(node => node.Text).Should().Equal("Step 1", "Step 2", "Step 3");
        smart.Parts.Values.Should().Contain(part => part.ContentType.Contains("diagramData"));
        smart.Parts.Values.Should().Contain(part => part.ContentType.Contains("diagramLayout"));
        smart.Parts.Values.Should().Contain(part => part.ContentType.Contains("diagramStyle"));
        smart.Parts.Values.Should().Contain(part => part.ContentType.Contains("diagramColors"));
        smart.Parts.Values.Should().Contain(part => part.ContentType.Contains("diagramDrawing"));
        smart.DrawingPartPath.Should().NotBeNull();
        smart.Parts.Should().ContainKey(smart.DrawingPartPath!);
        smart.PartRels.Keys.Should().Contain("ppt/diagrams/data1.xml");

        static IEnumerable<SmartArtNode> Flatten(IEnumerable<SmartArtNode> nodes)
        {
            foreach (var node in nodes)
            {
                yield return node;
                foreach (var child in Flatten(node.Children))
                    yield return child;
            }
        }
    }

    [Fact]
    public void InsertDefaultAutoShape_RejectsLineLikeKinds()
    {
        var editor = MakeSession();

        var action = () => editor.InsertDefaultAutoShape(DrawingShapeKind.ElbowConnector);

        action.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ApplyCommand_PictureWithPayload_InsertsPicture()
    {
        var editor = MakeSession();
        var payload = SlideObjectInsertionPlanner.CreatePicturePayload(new byte[] { 1, 2, 3 }, "sample.jpg");

        var added = SlideObjectInsertionPlanner.ApplyCommand(
            editor,
            SlideObjectInsertionPlanner.PictureCommandId,
            payload);

        added.Should().NotBeNull();
        added!.Kind.Should().Be(SlideShapeKind.Picture);
        added.Picture.Should().NotBeNull();
        added.Picture!.Bytes.Should().Equal(1, 2, 3);
        added.Picture.ContentType.Should().Be("image/jpeg");
    }

    [Theory]
    [InlineData(SlideObjectInsertionPlanner.VideoCommandId, true, "clip.mp4", "video/mp4")]
    [InlineData(SlideObjectInsertionPlanner.AudioCommandId, false, "narration.wav", "audio/wav")]
    public void ApplyCommand_MediaWithPayload_InsertsEmbeddedMedia(
        string commandId,
        bool isVideo,
        string fileName,
        string expectedContentType)
    {
        var editor = MakeSession();
        var payload = SlideObjectInsertionPlanner.CreateMediaPayload(
            new byte[] { 9, 8, 7 },
            fileName,
            isVideo);

        var added = SlideObjectInsertionPlanner.ApplyCommand(
            editor,
            commandId,
            mediaPayload: payload);

        added.Should().NotBeNull();
        added!.Kind.Should().Be(SlideShapeKind.Media);
        added.Media.Should().NotBeNull();
        added.Media!.IsVideo.Should().Be(isVideo);
        added.Media.ContentType.Should().Be(expectedContentType);
        added.Media.Bytes.Should().Equal(9, 8, 7);
    }

    [Theory]
    [InlineData("clip.mp4", true, "video/mp4")]
    [InlineData("clip.mov", true, "video/quicktime")]
    [InlineData("narration.mp3", false, "audio/mpeg")]
    [InlineData("narration.m4a", false, "audio/mp4")]
    [InlineData("unknown", true, "video/mp4")]
    [InlineData("unknown", false, "audio/mpeg")]
    public void InferMediaContentType_MapsCommonExtensions(
        string fileName,
        bool isVideo,
        string expectedContentType)
    {
        SlideObjectInsertionPlanner.InferMediaContentType(fileName, isVideo)
            .Should()
            .Be(expectedContentType);
    }

    [Fact]
    public void ApplyCommand_MediaWithoutPayload_IsNoOp()
    {
        var editor = MakeSession();
        var before = editor.CurrentSlide!.Shapes.Count;

        SlideObjectInsertionPlanner.ApplyCommand(
            editor,
            SlideObjectInsertionPlanner.VideoCommandId)
            .Should()
            .BeNull();

        editor.CurrentSlide.Shapes.Should().HaveCount(before);
    }

    [Theory]
    [InlineData("photo.jpg", "image/jpeg")]
    [InlineData("photo.jpeg", "image/jpeg")]
    [InlineData("photo.gif", "image/gif")]
    [InlineData("photo.bmp", "image/bmp")]
    [InlineData("photo.svg", "image/svg+xml")]
    [InlineData("photo.unknown", "image/png")]
    [InlineData(".jpg", "image/jpeg")]
    public void InferPictureContentType_MapsSupportedImageExtensions(
        string fileNameOrExtension,
        string expectedContentType)
    {
        SlideObjectInsertionPlanner.InferPictureContentType(fileNameOrExtension)
            .Should()
            .Be(expectedContentType);
    }

    [Fact]
    public void ApplyCommand_UnknownCommandId_IsNoOp()
    {
        var editor = MakeSession();

        SlideObjectInsertionPlanner.ApplyCommand(editor, "freep.unknown")
            .Should()
            .BeNull();
    }

    [Theory]
    [InlineData("budget.xlsx", "xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Excel.Sheet.12")]
    [InlineData("report.docx", "docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", "Word.Document.12")]
    public void CreatePayload_MapsOfficeFileMetadata(
        string fileName,
        string extension,
        string contentType,
        string progId)
    {
        var payload = OleInsertionPlanner.CreatePayload([1, 2, 3], fileName);

        payload.EmbeddedExtension.Should().Be(extension);
        payload.EmbeddedContentType.Should().Be(contentType);
        payload.ProgId.Should().Be(progId);
        payload.OleObjXml.Should().Contain("type=\"Embed\"");
        payload.OleObjXml.Should().Contain($"progId=\"{progId}\"");
        payload.EmbeddedBytes.Should().Equal(1, 2, 3);
    }

    [Fact]
    public void CreatePayload_PreservesExternalOleClassWhenSupplied()
    {
        var payload = OleInsertionPlanner.CreatePayload(
            [1, 2, 3],
            "Embedded.bin",
            "Vendor.Custom.Widget.7");

        payload.EmbeddedExtension.Should().Be("bin");
        payload.ProgId.Should().Be("Vendor.Custom.Widget.7");
        payload.OleObjXml.Should().Contain("progId=\"Vendor.Custom.Widget.7\"");
    }

    [Fact]
    public void InsertEmbeddedObject_IsUndoableAndPreservesPayload()
    {
        var editor = MakeSession();
        var initialCount = editor.CurrentSlide!.Shapes.Count;

        var added = editor.InsertEmbeddedObject([7, 8, 9], "budget.xlsx");

        added.Kind.Should().Be(SlideShapeKind.Ole);
        added.OleObject.Should().NotBeNull();
        added.OleObject!.EmbeddedExtension.Should().Be("xlsx");
        added.OleObject.ProgId.Should().Be("Excel.Sheet.12");
        added.OleObject.EmbeddedBytes.Should().Equal(7, 8, 9);
        editor.CurrentSlide.Shapes.Should().HaveCount(initialCount + 1);

        editor.Undo();
        editor.CurrentSlide.Shapes.Should().HaveCount(initialCount);
        editor.Redo();
        editor.CurrentSlide.Shapes.Should().ContainSingle(shape => shape.Kind == SlideShapeKind.Ole);

        using var package = new MemoryStream();
        FreeP.Core.IO.PptxPackageWriter.Write(editor.Presentation, package);
        package.Position = 0;
        var reopened = FreeP.Core.IO.PptxPackageReader.Read(package);
        var reopenedOle = reopened.Slides[0].Shapes.Single(shape => shape.Kind == SlideShapeKind.Ole);
        reopenedOle.OleObject.Should().NotBeNull();
        reopenedOle.OleObject!.EmbeddedBytes.Should().Equal(7, 8, 9);
        reopenedOle.OleObject.ProgId.Should().Be("Excel.Sheet.12");
    }

    [Fact]
    public void CreatePayload_RejectsEmptyFile()
    {
        var action = () => OleInsertionPlanner.CreatePayload([], "budget.xlsx");

        action.Should().Throw<ArgumentException>();
    }
}
