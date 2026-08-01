using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using FreeP.App.Compositor;
using FreeP.Core.IO;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class SmartArtEditingPlannerTests
{
    private const long FrameX = 914_400L;
    private const long FrameY = 457_200L;
    private const long FrameCx = 7_315_200L;
    private const long FrameCy = 3_657_600L;

    [Theory]
    [InlineData(SmartArtLayoutPreset.BasicProcess, "basicProcess", SmartArtFamily.Process)]
    [InlineData(SmartArtLayoutPreset.AccentProcess, "accentProcess", SmartArtFamily.Process)]
    [InlineData(SmartArtLayoutPreset.AscendingProcess, "ascendingProcess", SmartArtFamily.Process)]
    [InlineData(SmartArtLayoutPreset.DescendingProcess, "descendingProcess", SmartArtFamily.Process)]
    [InlineData(SmartArtLayoutPreset.BasicTimeline, "basicTimeline", SmartArtFamily.Process)]
    [InlineData(SmartArtLayoutPreset.PhasedProcess, "phasedProcess", SmartArtFamily.Process)]
    [InlineData(SmartArtLayoutPreset.CircleAccentTimeline, "circleAccentTimeline", SmartArtFamily.Process)]
    [InlineData(SmartArtLayoutPreset.StepDownProcess, "StepDownProcess", SmartArtFamily.Process)]
    [InlineData(SmartArtLayoutPreset.ContinuousBlockProcess, "continuousBlockProcess", SmartArtFamily.Process)]
    [InlineData(SmartArtLayoutPreset.SegmentedProcess, "segmentedProcess", SmartArtFamily.Process)]
    [InlineData(SmartArtLayoutPreset.ChevronProcess, "chevronProcess", SmartArtFamily.Process)]
    [InlineData(SmartArtLayoutPreset.BasicChevronProcess, "basicChevronProcess", SmartArtFamily.Process)]
    [InlineData(SmartArtLayoutPreset.ClosedChevronProcess, "closedChevronProcess", SmartArtFamily.Process)]
    [InlineData(SmartArtLayoutPreset.BendingProcess, "bendingProcess", SmartArtFamily.Process)]
    [InlineData(SmartArtLayoutPreset.AlternatingProcess, "alternatingProcess", SmartArtFamily.Process)]
    [InlineData(SmartArtLayoutPreset.ArrowRibbon, "arrowRibbon", SmartArtFamily.Process)]
    [InlineData(SmartArtLayoutPreset.CircleProcess, "circleProcess", SmartArtFamily.Process)]
    [InlineData(SmartArtLayoutPreset.FunnelProcess, "funnelProcess", SmartArtFamily.Process)]
    [InlineData(SmartArtLayoutPreset.VerticalProcess, "verticalProcess", SmartArtFamily.Process)]
    [InlineData(SmartArtLayoutPreset.VerticalBoxList, "verticalBoxList", SmartArtFamily.List)]
    [InlineData(SmartArtLayoutPreset.VerticalBlockList, "verticalBlockList", SmartArtFamily.List)]
    [InlineData(SmartArtLayoutPreset.VerticalChevronList, "verticalChevronList", SmartArtFamily.List)]
    [InlineData(SmartArtLayoutPreset.VerticalArrowList, "verticalArrowList", SmartArtFamily.List)]
    [InlineData(SmartArtLayoutPreset.VerticalBulletList, "verticalBulletList", SmartArtFamily.Hierarchy)]
    [InlineData(SmartArtLayoutPreset.HorizontalBulletList, "horizontalBulletList", SmartArtFamily.List)]
    [InlineData(SmartArtLayoutPreset.HorizontalBlockList, "horizontalBlockList", SmartArtFamily.List)]
    [InlineData(SmartArtLayoutPreset.TrapezoidList, "trapezoidList", SmartArtFamily.List)]
    [InlineData(SmartArtLayoutPreset.BasicCycle, "basicCycle", SmartArtFamily.Cycle)]
    [InlineData(SmartArtLayoutPreset.Cycle2, "cycle2", SmartArtFamily.Cycle)]
    [InlineData(SmartArtLayoutPreset.ContinuousCycle, "continuousCycle", SmartArtFamily.Cycle)]
    [InlineData(SmartArtLayoutPreset.GearCycle, "gearCycle", SmartArtFamily.Cycle)]
    [InlineData(SmartArtLayoutPreset.TextCycle, "textCycle", SmartArtFamily.Cycle)]
    [InlineData(SmartArtLayoutPreset.BlockCycle, "blockCycle", SmartArtFamily.Cycle)]
    [InlineData(SmartArtLayoutPreset.NonDirectionalCycle, "nonDirectionalCycle", SmartArtFamily.Cycle)]
    [InlineData(SmartArtLayoutPreset.BasicList, "list1", SmartArtFamily.List)]
    [InlineData(SmartArtLayoutPreset.List2, "list2", SmartArtFamily.List)]
    [InlineData(SmartArtLayoutPreset.BasicBlockList, "basicBlockList", SmartArtFamily.List)]
    [InlineData(SmartArtLayoutPreset.StackedList, "stackedList", SmartArtFamily.List)]
    [InlineData(SmartArtLayoutPreset.DescendingBlockList, "descendingBlockList", SmartArtFamily.List)]
    [InlineData(SmartArtLayoutPreset.BasicPyramid, "basicPyramid", SmartArtFamily.List)]
    [InlineData(SmartArtLayoutPreset.PyramidList, "pyramidList", SmartArtFamily.List)]
    [InlineData(SmartArtLayoutPreset.InvertedPyramid, "invertedPyramid", SmartArtFamily.List)]
    [InlineData(SmartArtLayoutPreset.RadialCycle, "radialCycle", SmartArtFamily.Cycle)]
    [InlineData(SmartArtLayoutPreset.BasicRadial, "radial1", SmartArtFamily.Cycle)]
    [InlineData(SmartArtLayoutPreset.RadialList, "radialList", SmartArtFamily.Cycle)]
    [InlineData(SmartArtLayoutPreset.BasicMatrix, "basicMatrix", SmartArtFamily.Matrix)]
    [InlineData(SmartArtLayoutPreset.TitledMatrix, "titledMatrix", SmartArtFamily.Matrix)]
    [InlineData(SmartArtLayoutPreset.GridMatrix, "gridMatrix", SmartArtFamily.Matrix)]
    [InlineData(SmartArtLayoutPreset.BasicRelationship, "relationship1", SmartArtFamily.Relationship)]
    [InlineData(SmartArtLayoutPreset.OpposingIdeas, "opposingIdeas", SmartArtFamily.Relationship)]
    [InlineData(SmartArtLayoutPreset.ConvergingRadial, "convergingRadial", SmartArtFamily.Relationship)]
    [InlineData(SmartArtLayoutPreset.BasicVenn, "basicVenn", SmartArtFamily.Relationship)]
    [InlineData(SmartArtLayoutPreset.RadialVenn, "radialVenn", SmartArtFamily.Relationship)]
    [InlineData(SmartArtLayoutPreset.TargetList, "targetList", SmartArtFamily.Relationship)]
    [InlineData(SmartArtLayoutPreset.StackedVenn, "stackedVenn", SmartArtFamily.Relationship)]
    [InlineData(SmartArtLayoutPreset.InterlockingRings, "interlockingRings", SmartArtFamily.Relationship)]
    [InlineData(SmartArtLayoutPreset.BasicHierarchy, "basicHierarchy", SmartArtFamily.Hierarchy)]
    [InlineData(SmartArtLayoutPreset.Hierarchy3, "hierarchy3", SmartArtFamily.Hierarchy)]
    [InlineData(SmartArtLayoutPreset.HorizontalHierarchy, "horizontalHierarchy", SmartArtFamily.Hierarchy)]
    [InlineData(SmartArtLayoutPreset.OrgChart, "orgChart", SmartArtFamily.Hierarchy)]
    [InlineData(SmartArtLayoutPreset.NameAndTitleOrgChart, "nameAndTitleOrgChart", SmartArtFamily.Hierarchy)]
    [InlineData(SmartArtLayoutPreset.PictureCaptionList, "pictureCaptionList", SmartArtFamily.List)]
    [InlineData(SmartArtLayoutPreset.PictureAccentList, "pictureAccentList", SmartArtFamily.List)]
    [InlineData(SmartArtLayoutPreset.PictureStack, "pictureStack", SmartArtFamily.List)]
    [InlineData(SmartArtLayoutPreset.PictureLineup, "pictureLineup", SmartArtFamily.List)]
    [InlineData(SmartArtLayoutPreset.PictureStrips, "pictureStrips", SmartArtFamily.List)]
    [InlineData(SmartArtLayoutPreset.ContinuousPictureList, "continuousPictureList", SmartArtFamily.List)]
    [InlineData(SmartArtLayoutPreset.PictureGrid, "pictureGrid", SmartArtFamily.List)]
    [InlineData(SmartArtLayoutPreset.LabeledHierarchy, "labeledHierarchy", SmartArtFamily.Hierarchy)]
    [InlineData(SmartArtLayoutPreset.TableHierarchy, "tableHierarchy", SmartArtFamily.Hierarchy)]
    public void ApplyLayoutPreset_UpdatesLiveModelAndNativeLayoutPart(
        SmartArtLayoutPreset preset,
        string expectedId,
        SmartArtFamily expectedFamily)
    {
        var smartArt = new SmartArtShape
        {
            Data = MakeFlatData(SmartArtFamily.Process, ("n1", "Plan"), ("n2", "Build")),
        };
        if (preset is (SmartArtLayoutPreset.PictureCaptionList or SmartArtLayoutPreset.PictureAccentList or SmartArtLayoutPreset.PictureStack or SmartArtLayoutPreset.PictureLineup or SmartArtLayoutPreset.PictureStrips or SmartArtLayoutPreset.ContinuousPictureList or SmartArtLayoutPreset.PictureGrid))
        {
            foreach (var node in smartArt.Data!.Nodes)
                node.Picture = new ImagePart { Bytes = [0x89, 0x50, 0x4E, 0x47], ContentType = "image/png" };
        }
        var layoutPart = new DiagramPart
        {
            PartPath = "ppt/diagrams/layout1.xml",
            ContentType = "application/vnd.openxmlformats-officedocument.drawingml.diagramLayout+xml",
            Bytes = Encoding.UTF8.GetBytes(
                "<dgm:layoutDef xmlns:dgm=\"http://schemas.openxmlformats.org/drawingml/2006/diagram\" uniqueId=\"old\" />")
        };
        smartArt.Parts[layoutPart.PartPath] = layoutPart;

        var result = SmartArtAuthoringPlanner.ApplyLayoutPreset(smartArt, preset);

        result.Applied.Should().BeTrue();
        result.LayoutUniqueId.Should().EndWith($"/layout/{expectedId}");
        result.Family.Should().Be(expectedFamily);
        smartArt.Data!.LayoutUniqueId.Should().Be(result.LayoutUniqueId);
        smartArt.Data.Family.Should().Be(expectedFamily);
        XDocument.Parse(Encoding.UTF8.GetString(layoutPart.Bytes))
            .Root!.Attribute("uniqueId")!.Value.Should().Be(result.LayoutUniqueId);
    }

    [Fact]
    public void ApplyLayoutPreset_CreatesNativeLayoutPartWhenDataPartExists()
    {
        var smartArt = new SmartArtShape
        {
            Data = MakeFlatData(SmartArtFamily.Process, ("n1", "Plan"))
        };
        smartArt.Parts["ppt/diagrams/data1.xml"] = new DiagramPart
        {
            PartPath = "ppt/diagrams/data1.xml",
            ContentType = "application/vnd.openxmlformats-officedocument.drawingml.diagramData+xml",
            Bytes = Encoding.UTF8.GetBytes(
                "<dgm:dataModel xmlns:dgm=\"http://schemas.openxmlformats.org/drawingml/2006/diagram\" />")
        };

        var result = SmartArtAuthoringPlanner.ApplyLayoutPreset(
            smartArt,
            SmartArtLayoutPreset.BasicCycle);

        result.Applied.Should().BeTrue();
        result.LayoutUniqueId.Should().EndWith("/layout/basicCycle");
        smartArt.DiagramRelIds["lo"].Should().Be("rIdFreePLayout");
        var layoutPart = smartArt.Parts.Values.Single(part =>
            part.ContentType.Contains("diagramLayout", StringComparison.OrdinalIgnoreCase));
        var layout = XDocument.Parse(Encoding.UTF8.GetString(layoutPart.Bytes));
        layout.Root!.Attribute("uniqueId")!.Value.Should().EndWith("/layout/basicCycle");
        layout.Root.Element(XName.Get("layoutNode", "http://schemas.openxmlformats.org/drawingml/2006/diagram"))
            .Should().NotBeNull();
    }

    [Fact]
    public void ApplyLayoutPreset_RequiresNativeDataPartWhenLayoutIsMissing()
    {
        var smartArt = new SmartArtShape
        {
            Data = MakeFlatData(SmartArtFamily.Process, ("n1", "Plan"))
        };

        var result = SmartArtAuthoringPlanner.ApplyLayoutPreset(
            smartArt,
            SmartArtLayoutPreset.BasicCycle);

        result.Applied.Should().BeFalse();
        result.Message.Should().Contain("native diagram data part");
    }

    [Fact]
    public void RegenerateDrawingCache_CreatesMissingDrawingPartAndRelationships()
    {
        var smartArt = new SmartArtShape
        {
            Data = MakeFlatData(SmartArtFamily.Process, ("n1", "Plan")),
        };
        smartArt.Parts["ppt/diagrams/data1.xml"] = new DiagramPart
        {
            PartPath = "ppt/diagrams/data1.xml",
            ContentType = "application/vnd.openxmlformats-officedocument.drawingml.diagramData+xml",
            Bytes = Encoding.UTF8.GetBytes("<dgm:dataModel xmlns:dgm=\"http://schemas.openxmlformats.org/drawingml/2006/diagram\" />"),
        };

        var result = SmartArtEditingPlanner.RegenerateDrawingCache(
            smartArt,
            FrameX,
            FrameY,
            FrameCx,
            FrameCy,
            DefaultTheme());

        result.Applied.Should().BeTrue(result.Message);
        smartArt.DrawingPartPath.Should().Be("ppt/diagrams/drawing1.xml");
        smartArt.Parts[smartArt.DrawingPartPath].Bytes.Should().Contain((byte)'d');
        var dataRels = Encoding.UTF8.GetString(smartArt.PartRels["ppt/diagrams/data1.xml"]);
        dataRels.Should().Contain("/diagramDrawing").And.Contain("drawing1.xml");
        XDocument.Parse(Encoding.UTF8.GetString(smartArt.PartRels[smartArt.DrawingPartPath]))
            .Root!.Name.LocalName.Should().Be("Relationships");
    }

    [Fact]
    public void RegenerateDrawingCache_CreatesPictureRelationshipsWhenDrawingPartIsMissing()
    {
        var data = new SmartArtData
        {
            Family = SmartArtFamily.List,
            LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/pictureGrid",
            IsLiveLayoutSupported = true,
        };
        data.Nodes.Add(new SmartArtNode
        {
            ModelId = "n1",
            Text = "Picture",
            Level = 0,
            Picture = new ImagePart { Bytes = [1, 2, 3], ContentType = "image/png" },
        });
        var smartArt = new SmartArtShape { Data = data };
        smartArt.Parts["ppt/diagrams/data1.xml"] = new DiagramPart
        {
            PartPath = "ppt/diagrams/data1.xml",
            ContentType = "application/vnd.openxmlformats-officedocument.drawingml.diagramData+xml",
            Bytes = Encoding.UTF8.GetBytes("<dgm:dataModel xmlns:dgm=\"http://schemas.openxmlformats.org/drawingml/2006/diagram\" />"),
        };

        var result = SmartArtEditingPlanner.RegenerateDrawingCache(
            smartArt,
            FrameX,
            FrameY,
            FrameCx,
            FrameCy,
            DefaultTheme());

        result.Applied.Should().BeTrue(result.Message);
        var drawingRels = XDocument.Parse(Encoding.UTF8.GetString(smartArt.PartRels[smartArt.DrawingPartPath!]));
        drawingRels.Descendants()
            .Where(element => element.Name.LocalName == "Relationship")
            .Where(element =>
                element.Attribute("Type")?.Value.EndsWith("/image", StringComparison.Ordinal) == true)
            .Should().ContainSingle();
        smartArt.Parts.Keys.Should().Contain(path => path.StartsWith("ppt/media/freep-smartart-picture", StringComparison.Ordinal));
    }

    [Fact]
    public void ApplyLayoutPreset_NewLayoutPartIsWrittenAsDiagramRelationship()
    {
        var smartArt = new SmartArtShape
        {
            Data = MakeFlatData(SmartArtFamily.Process, ("n1", "Plan")),
            DrawingPartPath = "ppt/diagrams/drawing1.xml",
        };
        smartArt.Parts["ppt/diagrams/data1.xml"] = new DiagramPart
        {
            PartPath = "ppt/diagrams/data1.xml",
            ContentType = "application/vnd.openxmlformats-officedocument.drawingml.diagramData+xml",
            Bytes = Encoding.UTF8.GetBytes(
                "<dgm:dataModel xmlns:dgm=\"http://schemas.openxmlformats.org/drawingml/2006/diagram\" />")
        };
        smartArt.Parts[smartArt.DrawingPartPath] = new DiagramPart
        {
            PartPath = smartArt.DrawingPartPath,
            ContentType = "application/vnd.ms-office.drawingml.diagramDrawing+xml",
            Bytes = Encoding.UTF8.GetBytes(
                "<dsp:drawing xmlns:dsp=\"http://schemas.microsoft.com/office/drawing/2008/diagram\" />")
        };
        smartArt.DiagramRelIds["dm"] = "rIdDm1";
        smartArt.PartRels["ppt/diagrams/data1.xml"] = Encoding.UTF8.GetBytes(
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rIdDrawing1\" Type=\"http://schemas.microsoft.com/office/2007/relationships/diagramDrawing\" Target=\"drawing1.xml\" /></Relationships>");

        SmartArtAuthoringPlanner.ApplyLayoutPreset(smartArt, SmartArtLayoutPreset.BasicCycle)
            .Applied.Should().BeTrue();

        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Add(new SlideShape
        {
            Id = 7,
            Kind = SlideShapeKind.SmartArt,
            SmartArt = smartArt,
            ExtentCxEmu = 7_315_200,
            ExtentCyEmu = 3_657_600,
        });
        using var stream = new MemoryStream();
        PptxPackageWriter.Write(presentation, stream);

        using var archive = new ZipArchive(new MemoryStream(stream.ToArray()), ZipArchiveMode.Read);
        archive.GetEntry("ppt/diagrams/layout1.xml").Should().NotBeNull();
        var slideRels = XDocument.Load(archive.GetEntry("ppt/slides/_rels/slide1.xml.rels")!.Open());
        var layoutRelations = slideRels
            .Descendants(XName.Get("Relationship", "http://schemas.openxmlformats.org/package/2006/relationships"))
            .Where(relation =>
                relation.Attribute("Type") is { } type &&
                type.Value.EndsWith("/diagramLayout", StringComparison.Ordinal) &&
                relation.Attribute("Target") is { } target &&
                target.Value == "../diagrams/layout1.xml")
            .ToArray();
        layoutRelations.Should().ContainSingle();

        var slide = XDocument.Load(archive.GetEntry("ppt/slides/slide1.xml")!.Open());
        slide.Descendants(XName.Get("relIds", "http://schemas.openxmlformats.org/drawingml/2006/diagram"))
            .Single()
            .Attributes(XName.Get("lo", "http://schemas.openxmlformats.org/officeDocument/2006/relationships"))
            .Should().ContainSingle();
    }

    [Fact]
    public void ApplyLayoutPreset_PersistsNativeLayoutWhenLiveDataIsMissing()
    {
        var smartArt = new SmartArtShape();
        var layoutPart = new DiagramPart
        {
            PartPath = "ppt/diagrams/layout1.xml",
            ContentType = "application/vnd.openxmlformats-officedocument.drawingml.diagramLayout+xml",
            Bytes = Encoding.UTF8.GetBytes(
                "<dgm:layoutDef xmlns:dgm=\"http://schemas.openxmlformats.org/drawingml/2006/diagram\" uniqueId=\"old\" />")
        };
        smartArt.Parts[layoutPart.PartPath] = layoutPart;

        var result = SmartArtAuthoringPlanner.ApplyLayoutPreset(
            smartArt,
            SmartArtLayoutPreset.BasicProcess);

        result.Applied.Should().BeTrue();
        result.LayoutUniqueId.Should().EndWith("/layout/basicProcess");
        smartArt.Data.Should().BeNull();
        smartArt.DiagramRelIds.Should().ContainKey("lo");
        XDocument.Parse(Encoding.UTF8.GetString(layoutPart.Bytes))
            .Root!.Attribute("uniqueId")!.Value.Should().Be(result.LayoutUniqueId);
    }

    [Fact]
    public void ExistingNativeLayoutPart_RepairsMissingDiagramRelationshipKey()
    {
        var smartArt = new SmartArtShape
        {
            Data = MakeFlatData(SmartArtFamily.Process, ("n1", "Plan")),
        };
        var layoutPart = new DiagramPart
        {
            PartPath = "ppt/diagrams/layout1.xml",
            ContentType = "application/vnd.openxmlformats-officedocument.drawingml.diagramLayout+xml",
            Bytes = Encoding.UTF8.GetBytes(
                "<dgm:layoutDef xmlns:dgm=\"http://schemas.openxmlformats.org/drawingml/2006/diagram\" uniqueId=\"old\" />")
        };
        smartArt.Parts[layoutPart.PartPath] = layoutPart;

        SmartArtAuthoringPlanner.ApplyLayoutPreset(smartArt, SmartArtLayoutPreset.BasicProcess)
            .Applied.Should().BeTrue();

        smartArt.DiagramRelIds["lo"].Should().Be("rIdFreePLayout");
    }

    [Fact]
    public void ApplyPictureCaptionList_AllowsMissingPicturePayloadForPlaceholders()
    {
        var smartArt = new SmartArtShape
        {
            Data = MakeFlatData(SmartArtFamily.List, ("n1", "Plan"), ("n2", "Build")),
        };
        AddLayoutPart(smartArt);

        var result = SmartArtAuthoringPlanner.ApplyLayoutPreset(
            smartArt,
            SmartArtLayoutPreset.PictureCaptionList);

        result.Applied.Should().BeTrue();
        result.LayoutUniqueId.Should().EndWith("/layout/pictureCaptionList");
        smartArt.Data!.Family.Should().Be(SmartArtFamily.List);
    }

    [Fact]
    public void ApplyPictureGrid_AllowsMissingPicturePayloadForPlaceholders()
    {
        var smartArt = new SmartArtShape
        {
            Data = MakeFlatData(SmartArtFamily.List, ("n1", "Plan"), ("n2", "Build")),
        };
        AddLayoutPart(smartArt);

        var result = SmartArtAuthoringPlanner.ApplyLayoutPreset(smartArt, SmartArtLayoutPreset.PictureGrid);

        result.Applied.Should().BeTrue();
        result.LayoutUniqueId.Should().EndWith("/layout/pictureGrid");
        smartArt.Data!.Family.Should().Be(SmartArtFamily.List);
    }

    [Fact]
    public void ApplyPictureAccentList_AllowsMissingPicturePayloadForPlaceholders()
    {
        var smartArt = new SmartArtShape
        {
            Data = MakeFlatData(SmartArtFamily.List, ("n1", "Plan"), ("n2", "Build")),
        };
        AddLayoutPart(smartArt);

        var result = SmartArtAuthoringPlanner.ApplyLayoutPreset(smartArt, SmartArtLayoutPreset.PictureAccentList);

        result.Applied.Should().BeTrue();
        result.LayoutUniqueId.Should().EndWith("/layout/pictureAccentList");
        smartArt.Data!.Family.Should().Be(SmartArtFamily.List);
    }

    [Fact]
    public void ApplyPictureStack_AllowsMissingPicturePayloadForPlaceholders()
    {
        var smartArt = new SmartArtShape
        {
            Data = MakeFlatData(SmartArtFamily.List, ("n1", "Plan"), ("n2", "Build")),
        };
        AddLayoutPart(smartArt);

        var result = SmartArtAuthoringPlanner.ApplyLayoutPreset(smartArt, SmartArtLayoutPreset.PictureStack);

        result.Applied.Should().BeTrue();
        result.LayoutUniqueId.Should().EndWith("/layout/pictureStack");
        smartArt.Data!.Family.Should().Be(SmartArtFamily.List);
    }

    [Fact]
    public void ApplyPictureLineup_AllowsMissingPicturePayloadForPlaceholders()
    {
        var smartArt = new SmartArtShape
        {
            Data = MakeFlatData(SmartArtFamily.List, ("n1", "Plan"), ("n2", "Build")),
        };
        AddLayoutPart(smartArt);

        var result = SmartArtAuthoringPlanner.ApplyLayoutPreset(smartArt, SmartArtLayoutPreset.PictureLineup);

        result.Applied.Should().BeTrue();
        result.LayoutUniqueId.Should().EndWith("/layout/pictureLineup");
        smartArt.Data!.Family.Should().Be(SmartArtFamily.List);
    }

    [Fact]
    public void ApplyContinuousPictureList_AllowsMissingPicturePayloadForPlaceholders()
    {
        var smartArt = new SmartArtShape
        {
            Data = MakeFlatData(SmartArtFamily.List, ("n1", "Plan"), ("n2", "Build")),
        };
        AddLayoutPart(smartArt);

        var result = SmartArtAuthoringPlanner.ApplyLayoutPreset(smartArt, SmartArtLayoutPreset.ContinuousPictureList);

        result.Applied.Should().BeTrue();
        result.LayoutUniqueId.Should().EndWith("/layout/continuousPictureList");
        smartArt.Data!.Family.Should().Be(SmartArtFamily.List);
    }

    [Theory]
    [InlineData(SmartArtQuickStylePreset.SimpleFill, "simple1", "Simple Fill")]
    [InlineData(SmartArtQuickStylePreset.WhiteOutline, "simple2", "White Outline")]
    [InlineData(SmartArtQuickStylePreset.SubtleEffect, "simple3", "Subtle Effect")]
    [InlineData(SmartArtQuickStylePreset.ModerateEffect, "simple4", "Moderate Effect")]
    [InlineData(SmartArtQuickStylePreset.IntenseEffect, "simple5", "Intense Effect")]
    [InlineData(SmartArtQuickStylePreset.Polished, "3d1", "Polished")]
    [InlineData(SmartArtQuickStylePreset.Inset, "3d2", "Inset")]
    [InlineData(SmartArtQuickStylePreset.Cartoon, "3d3", "Cartoon")]
    [InlineData(SmartArtQuickStylePreset.Powder, "3d4", "Powder")]
    [InlineData(SmartArtQuickStylePreset.BrickScene, "3d5", "Brick Scene")]
    [InlineData(SmartArtQuickStylePreset.FlatScene, "3d6", "Flat Scene")]
    [InlineData(SmartArtQuickStylePreset.MetallicScene, "3d7", "Metallic Scene")]
    [InlineData(SmartArtQuickStylePreset.SunsetScene, "3d8", "Sunset Scene")]
    [InlineData(SmartArtQuickStylePreset.BirdsEyeScene, "3d9", "Bird's Eye Scene")]
    public void ApplyQuickStylePreset_UpdatesMetadataAndNativeStylePart(
        SmartArtQuickStylePreset preset,
        string expectedId,
        string expectedTitle)
    {
        var smartArt = new SmartArtShape
        {
            Data = MakeFlatData(SmartArtFamily.Process, ("n1", "Plan")),
            QuickStyle = new SmartArtQuickStyleMetadata { UniqueId = "old-style", Title = "Old" },
        };
        var stylePart = new DiagramPart
        {
            PartPath = "ppt/diagrams/quickStyle1.xml",
            ContentType = "application/vnd.openxmlformats-officedocument.drawingml.diagramStyle+xml",
            Bytes = Encoding.UTF8.GetBytes(
                "<dgm:styleDef xmlns:dgm=\"http://schemas.openxmlformats.org/drawingml/2006/diagram\" uniqueId=\"old-style\"><dgm:title val=\"Old\" /></dgm:styleDef>")
        };
        smartArt.Parts["ppt/diagrams/data1.xml"] = new DiagramPart
        {
            PartPath = "ppt/diagrams/data1.xml",
            ContentType = "application/vnd.openxmlformats-officedocument.drawingml.diagramData+xml",
            Bytes = Encoding.UTF8.GetBytes(
                "<dgm:dataModel xmlns:dgm=\"http://schemas.openxmlformats.org/drawingml/2006/diagram\" />")
        };
        smartArt.Parts[stylePart.PartPath] = stylePart;

        var result = SmartArtAuthoringPlanner.ApplyQuickStylePreset(smartArt, preset);

        result.Applied.Should().BeTrue();
        result.StyleUniqueId.Should().EndWith($"/quickstyle/{expectedId}");
        smartArt.QuickStyle!.UniqueId.Should().Be(result.StyleUniqueId);
        smartArt.QuickStyle.Title.Should().Be(expectedTitle);
        smartArt.QuickStyle.Category.Should().Be(
            preset is SmartArtQuickStylePreset.Polished
                or SmartArtQuickStylePreset.Inset
                or SmartArtQuickStylePreset.Cartoon
                or SmartArtQuickStylePreset.Powder
                or SmartArtQuickStylePreset.BrickScene
                or SmartArtQuickStylePreset.FlatScene
                or SmartArtQuickStylePreset.MetallicScene
                or SmartArtQuickStylePreset.SunsetScene
                or SmartArtQuickStylePreset.BirdsEyeScene
                ? "3D"
                : "simple");
        smartArt.QuickStyle.StyleLabels.Should().Contain("node0");
        smartArt.DiagramRelIds.Should().ContainKey("dm");
        smartArt.DiagramRelIds.Should().ContainKey("qs");
        var root = XDocument.Parse(Encoding.UTF8.GetString(stylePart.Bytes)).Root!;
        root.Attribute("uniqueId")!.Value.Should().Be(result.StyleUniqueId);
        root.Element(XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/diagram") + "title")!
            .Attribute("val")!.Value.Should().Be(expectedTitle);
    }

    [Fact]
    public void ApplyQuickStylePreset_CreatesMissingNativeStylePart()
    {
        var smartArt = new SmartArtShape
        {
            Data = MakeFlatData(SmartArtFamily.Process, ("n1", "Plan"))
        };
        smartArt.Parts["ppt/diagrams/data1.xml"] = new DiagramPart
        {
            PartPath = "ppt/diagrams/data1.xml",
            ContentType = "application/vnd.openxmlformats-officedocument.drawingml.diagramData+xml",
            Bytes = Encoding.UTF8.GetBytes("<dgm:dataModel xmlns:dgm=\"http://schemas.openxmlformats.org/drawingml/2006/diagram\" />")
        };

        var result = SmartArtAuthoringPlanner.ApplyQuickStylePreset(smartArt, SmartArtQuickStylePreset.Intense);

        result.Applied.Should().BeTrue();
        result.PartPath.Should().NotBeNull();
        smartArt.DiagramRelIds.Should().ContainKey("qs");
        smartArt.Parts[result.PartPath!].ContentType.Should().Contain("diagramStyle");
    }

    [Fact]
    public void ChangeText_UpdatesTargetNodeAndLiveLayoutText()
    {
        var data = MakeFlatData(SmartArtFamily.Process, ("n1", "Plan"), ("n2", "Build"));

        var result = SmartArtEditingPlanner.Apply(data, SmartArtNodeEditIntent.ChangeText("n2", "Validate"));

        result.Applied.Should().BeTrue();
        result.SelectedModelId.Should().Be("n2");
        data.Nodes[1].Text.Should().Be("Validate");

        var texts = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme())!
            .Where(shape => shape.AutoShapeKind == DrawingShapeKind.RoundedRectangle)
            .Select(shape => shape.TextBody!.Paragraphs[0].Runs[0].Text);

        texts.Should().Equal("Plan", "Validate");
    }

    [Fact]
    public void AddSiblingAfter_InsertsNodeAfterTargetWithStableGeneratedId()
    {
        var data = MakeFlatData(SmartArtFamily.List, ("n1", "North"), ("n2", "South"));

        var result = SmartArtEditingPlanner.Apply(data, SmartArtNodeEditIntent.AddSiblingAfter("n1", "Center"));

        result.Applied.Should().BeTrue();
        result.SelectedModelId.Should().Be("freep-smartart-node-3");
        data.Nodes.Select(node => node.Text).Should().Equal("North", "Center", "South");
        data.Nodes.Select(node => node.Level).Should().Equal(0, 0, 0);
        result.Outline.Select(item => item.Text).Should().Equal("North", "Center", "South");
    }

    [Fact]
    public void AddChild_AppendsNestedNodeAndNormalizesLevels()
    {
        var root = new SmartArtNode { ModelId = "root", Text = "Leader", Level = 4 };
        var data = new SmartArtData { Family = SmartArtFamily.Hierarchy };
        data.Nodes.Add(root);

        var result = SmartArtEditingPlanner.Apply(data, SmartArtNodeEditIntent.AddChild("root", "Report"));

        result.Applied.Should().BeTrue();
        root.Level.Should().Be(0);
        root.Children.Should().ContainSingle();
        root.Children[0].ModelId.Should().Be("freep-smartart-node-2");
        root.Children[0].Level.Should().Be(1);
        root.Children[0].Text.Should().Be("Report");
        result.Outline.Select(item => item.Level).Should().Equal(0, 1);
    }

    [Fact]
    public void Remove_RemovesSubtreeButKeepsNextSelection()
    {
        var data = MakeFlatData(SmartArtFamily.Process, ("n1", "Draft"), ("n2", "Review"), ("n3", "Ship"));

        var result = SmartArtEditingPlanner.Apply(data, SmartArtNodeEditIntent.Remove("n2"));

        result.Applied.Should().BeTrue();
        result.SelectedModelId.Should().Be("n3");
        data.Nodes.Select(node => node.Text).Should().Equal("Draft", "Ship");
    }

    [Fact]
    public void Remove_LastRemainingNode_IsRejected()
    {
        var data = MakeFlatData(SmartArtFamily.Process, ("n1", "Only"));

        var result = SmartArtEditingPlanner.Apply(data, SmartArtNodeEditIntent.Remove("n1"));

        result.Applied.Should().BeFalse();
        data.Nodes.Should().ContainSingle();
        result.Message.Should().Be("At least one SmartArt node must remain.");
    }

    [Fact]
    public void MoveDown_ReordersFlatNodesAndLiveLayout()
    {
        var data = MakeFlatData(SmartArtFamily.Process, ("n1", "Plan"), ("n2", "Build"), ("n3", "Ship"));

        var result = SmartArtEditingPlanner.Apply(data, SmartArtNodeEditIntent.MoveDown("n1"));

        result.Applied.Should().BeTrue();
        result.SelectedModelId.Should().Be("n1");
        data.Nodes.Select(node => node.ModelId).Should().Equal("n2", "n1", "n3");
        result.Outline.Select(item => item.Text).Should().Equal("Build", "Plan", "Ship");

        var liveTexts = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme())!
            .Where(shape => shape.AutoShapeKind == DrawingShapeKind.RoundedRectangle)
            .OrderBy(shape => shape.OffsetXEmu)
            .Select(shape => shape.TextBody!.Paragraphs[0].Runs[0].Text);

        liveTexts.Should().Equal("Build", "Plan", "Ship");
    }

    [Fact]
    public void MoveUp_FirstSibling_IsRejected()
    {
        var data = MakeFlatData(SmartArtFamily.Process, ("n1", "Only"), ("n2", "Later"));

        var result = SmartArtEditingPlanner.Apply(data, SmartArtNodeEditIntent.MoveUp("n1"));

        result.Applied.Should().BeFalse();
        result.Message.Should().Be("The SmartArt node is already first.");
        data.Nodes.Select(node => node.ModelId).Should().Equal("n1", "n2");
    }

    [Fact]
    public void Promote_ChildBecomesSiblingAfterParentAndNormalizesLevels()
    {
        var root = new SmartArtNode { ModelId = "root", Text = "Leader", Level = 0 };
        var child = new SmartArtNode { ModelId = "child", Text = "Manager", Level = 7 };
        var grandchild = new SmartArtNode { ModelId = "grandchild", Text = "Report", Level = 9 };
        child.Children.Add(grandchild);
        root.Children.Add(child);

        var data = new SmartArtData { Family = SmartArtFamily.Hierarchy };
        data.Nodes.Add(root);

        var result = SmartArtEditingPlanner.Apply(data, SmartArtNodeEditIntent.Promote("child"));

        result.Applied.Should().BeTrue();
        result.SelectedModelId.Should().Be("child");
        data.Nodes.Select(node => node.ModelId).Should().Equal("root", "child");
        root.Children.Should().BeEmpty();
        child.Level.Should().Be(0);
        grandchild.Level.Should().Be(1);
        result.Outline.Select(item => (item.ModelId, item.Level))
            .Should().Equal(("root", 0), ("child", 0), ("grandchild", 1));
    }

    [Fact]
    public void Promote_RootNode_IsRejected()
    {
        var data = MakeFlatData(SmartArtFamily.Hierarchy, ("root", "Leader"));

        var result = SmartArtEditingPlanner.Apply(data, SmartArtNodeEditIntent.Promote("root"));

        result.Applied.Should().BeFalse();
        result.Message.Should().Be("A root SmartArt node cannot be promoted.");
        data.Nodes.Should().ContainSingle();
    }

    [Fact]
    public void Demote_MakesNodeChildOfPreviousSiblingAndUpdatesHierarchyLayout()
    {
        var data = MakeFlatData(SmartArtFamily.Hierarchy, ("n1", "Leader"), ("n2", "Manager"), ("n3", "Peer"));

        var result = SmartArtEditingPlanner.Apply(data, SmartArtNodeEditIntent.Demote("n2"));

        result.Applied.Should().BeTrue();
        result.SelectedModelId.Should().Be("n2");
        data.Nodes.Select(node => node.ModelId).Should().Equal("n1", "n3");
        data.Nodes[0].Children.Should().ContainSingle();
        data.Nodes[0].Children[0].ModelId.Should().Be("n2");
        result.Outline.Select(item => (item.ModelId, item.Level, item.SiblingIndex))
            .Should().Equal(("n1", 0, 0), ("n2", 1, 0), ("n3", 0, 1));

        var liveTexts = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme())!
            .Where(shape => shape.AutoShapeKind == DrawingShapeKind.RoundedRectangle)
            .Select(shape => shape.TextBody!.Paragraphs[0].Runs[0].Text);

        liveTexts.Should().Contain(["Leader", "Manager", "Peer"]);
    }

    [Fact]
    public void Demote_FirstSibling_IsRejected()
    {
        var data = MakeFlatData(SmartArtFamily.Hierarchy, ("n1", "Leader"), ("n2", "Manager"));

        var result = SmartArtEditingPlanner.Apply(data, SmartArtNodeEditIntent.Demote("n1"));

        result.Applied.Should().BeFalse();
        result.Message.Should().Be("The first SmartArt sibling cannot be demoted.");
        data.Nodes.Select(node => node.ModelId).Should().Equal("n1", "n2");
    }

    [Fact]
    public void CloneShape_DeepClonesEditableSmartArtData()
    {
        var shape = new SlideShape
        {
            Id = 12,
            Kind = SlideShapeKind.SmartArt,
            SmartArt = new SmartArtShape
            {
                Data = MakeFlatData(SmartArtFamily.Process, ("n1", "Source"))
            }
        };
        shape.SmartArt.Parts["ppt/diagrams/data1.xml"] = new DiagramPart
        {
            PartPath = "ppt/diagrams/data1.xml",
            ContentType = "application/xml",
            Bytes = [1, 2, 3]
        };

        var clone = SlideCloner.CloneShape(shape);
        SmartArtEditingPlanner.Apply(clone.SmartArt!.Data, SmartArtNodeEditIntent.ChangeText("n1", "Clone"));

        shape.SmartArt!.Data!.Nodes[0].Text.Should().Be("Source");
        clone.SmartArt.Data!.Nodes[0].Text.Should().Be("Clone");
        clone.SmartArt.Parts.Should().ContainKey("ppt/diagrams/data1.xml");
        clone.SmartArt.Parts.Should().NotBeSameAs(shape.SmartArt.Parts);
    }

    [Fact]
    public void CloneShape_DeepClonesSmartArtPicturesAndPackagePayloads()
    {
        var sourcePicture = new ImagePart { Bytes = [1, 2, 3], ContentType = "image/png" };
        var shape = new SlideShape
        {
            Id = 13,
            Kind = SlideShapeKind.SmartArt,
            SmartArt = new SmartArtShape
            {
                Data = new SmartArtData
                {
                    Family = SmartArtFamily.List,
                    LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/pictureCaptionList",
                },
            },
        };
        shape.SmartArt.Data.Nodes.Add(new SmartArtNode
        {
            ModelId = "picture-node",
            Text = "Picture",
            Picture = sourcePicture,
        });
        shape.SmartArt.Parts["ppt/diagrams/drawing1.xml"] = new DiagramPart
        {
            PartPath = "ppt/diagrams/drawing1.xml",
            ContentType = "application/xml",
            Bytes = [4, 5, 6],
        };
        shape.SmartArt.PartRels["ppt/diagrams/drawing1.xml"] = [7, 8, 9];

        var clone = SlideCloner.CloneShape(shape);
        var cloneSmartArt = clone.SmartArt!;

        cloneSmartArt.Data!.Nodes[0].Picture.Should().NotBeSameAs(sourcePicture);
        cloneSmartArt.Parts["ppt/diagrams/drawing1.xml"].Bytes.Should()
            .NotBeSameAs(shape.SmartArt.Parts["ppt/diagrams/drawing1.xml"].Bytes);
        cloneSmartArt.PartRels["ppt/diagrams/drawing1.xml"].Should()
            .NotBeSameAs(shape.SmartArt.PartRels["ppt/diagrams/drawing1.xml"]);

        cloneSmartArt.Data.Nodes[0].Picture!.Bytes[0] = 10;
        cloneSmartArt.Parts["ppt/diagrams/drawing1.xml"].Bytes[0] = 11;
        cloneSmartArt.PartRels["ppt/diagrams/drawing1.xml"][0] = 12;

        sourcePicture.Bytes.Should().Equal(1, 2, 3);
        shape.SmartArt.Parts["ppt/diagrams/drawing1.xml"].Bytes.Should().Equal(4, 5, 6);
        shape.SmartArt.PartRels["ppt/diagrams/drawing1.xml"].Should().Equal(7, 8, 9);
    }

    [Fact]
    public void ApplyTextPaneOutline_RebuildsSharedTreeAndLiveLayout()
    {
        var data = MakeFlatData(SmartArtFamily.Hierarchy, ("root", "Leader"), ("manager", "Manager"));
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/orgChart";

        var result = SmartArtEditingPlanner.ApplyTextPaneOutline(data,
        [
            new("Executive", 0, ModelId: "root"),
            new("Assistant", 1, IsAssistant: true, ModelId: "assistant"),
            new("Platform", 1, ModelId: "manager"),
            new("QA", 2),
            new("Operations", 0, ModelId: "operations")
        ]);

        result.Applied.Should().BeTrue();
        result.RowCount.Should().Be(5);
        result.Outline.Select(item => (item.ModelId, item.Text, item.Level, item.SiblingIndex, item.IsAssistant))
            .Should().Equal(
                ("root", "Executive", 0, 0, false),
                ("assistant", "Assistant", 1, 0, true),
                ("manager", "Platform", 1, 1, false),
                ("freep-smartart-node-4", "QA", 2, 0, false),
                ("operations", "Operations", 0, 1, false));

        data.Nodes.Should().HaveCount(2);
        data.Nodes[0].Children.Should().HaveCount(2);
        data.Nodes[0].Children[0].IsAssistant.Should().BeTrue();
        data.Nodes[0].Children[1].Children.Should().ContainSingle().Which.Text.Should().Be("QA");

        var laidOut = SmartArtLayoutEngine.Layout(data, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme())!;
        var liveTexts = laidOut
            .Where(shape => shape.TextBody is not null)
            .Select(shape => shape.TextBody!.Paragraphs[0].Runs[0].Text);

        liveTexts.Should().Contain(["Executive", "Assistant", "Platform", "QA", "Operations"]);
    }

    [Fact]
    public void ToggleAssistant_ChangesHierarchyNodeTypeAndSupportsUndoIntent()
    {
        var data = MakeFlatData(SmartArtFamily.Hierarchy, ("root", "Leader"));
        data.Nodes[0].Children.Add(new SmartArtNode
        {
            ModelId = "child",
            Text = "Manager",
            Level = 1,
        });
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/orgChart";

        var enable = SmartArtEditingPlanner.Apply(
            data,
            SmartArtNodeEditIntent.ToggleAssistant("child"));

        enable.Applied.Should().BeTrue();
        enable.Kind.Should().Be(SmartArtNodeEditKind.ToggleAssistant);
        data.Nodes[0].Children.Single().IsAssistant.Should().BeTrue();
        enable.Outline.Single(item => item.ModelId == "child").IsAssistant.Should().BeTrue();

        var smartArt = new SmartArtShape { Data = data };
        smartArt.Parts["ppt/diagrams/data1.xml"] = new DiagramPart
        {
            PartPath = "ppt/diagrams/data1.xml",
            ContentType = "application/vnd.openxmlformats-officedocument.drawingml.diagramData+xml",
            Bytes = Encoding.UTF8.GetBytes("<dgm:dataModel xmlns:dgm=\"http://schemas.openxmlformats.org/drawingml/2006/diagram\" />")
        };
        SmartArtEditingPlanner.RewriteDataPart(smartArt).Applied.Should().BeTrue();
        var dgm = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/diagram");
        XDocument.Parse(Encoding.UTF8.GetString(smartArt.Parts["ppt/diagrams/data1.xml"].Bytes))
            .Descendants(dgm + "pt")
            .Single(pt => (string?)pt.Attribute("modelId") == "child")
            .Attribute("type")!.Value.Should().Be("asst");

        var disable = SmartArtEditingPlanner.Apply(
            data,
            SmartArtNodeEditIntent.ToggleAssistant("child"));

        disable.Applied.Should().BeTrue();
        data.Nodes[0].Children.Single().IsAssistant.Should().BeFalse();
    }

    [Fact]
    public void ToggleAssistant_RejectsRootAndNonHierarchyNodes()
    {
        var hierarchy = MakeFlatData(SmartArtFamily.Hierarchy, ("root", "Leader"));
        var rootResult = SmartArtEditingPlanner.Apply(
            hierarchy,
            SmartArtNodeEditIntent.ToggleAssistant("root"));

        rootResult.Applied.Should().BeFalse();
        rootResult.Message.Should().Be("A root SmartArt node cannot be an assistant.");

        var process = MakeFlatData(SmartArtFamily.Process, ("root", "Step"));
        var processResult = SmartArtEditingPlanner.Apply(
            process,
            SmartArtNodeEditIntent.ToggleAssistant("root"));

        processResult.Applied.Should().BeFalse();
        processResult.Message.Should().Be("Assistant nodes are supported only in hierarchy SmartArt.");
    }

    [Fact]
    public void AddAssistant_InsertsAssistantChildBeforeRegularChildrenAndRewritesNodeType()
    {
        var data = MakeFlatData(SmartArtFamily.Hierarchy, ("root", "Leader"));
        data.Nodes[0].Children.Add(new SmartArtNode
        {
            ModelId = "child",
            Text = "Manager",
            Level = 1,
        });
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/orgChart";

        var result = SmartArtEditingPlanner.Apply(
            data,
            SmartArtNodeEditIntent.AddAssistant("root"));

        result.Applied.Should().BeTrue();
        result.Kind.Should().Be(SmartArtNodeEditKind.AddAssistant);
        result.SelectedModelId.Should().StartWith("freep-smartart-node-");
        data.Nodes[0].Children.Select(node => (node.Text, node.IsAssistant))
            .Should().Equal(("Assistant", true), ("Manager", false));
        result.Outline.Should().Contain(item =>
            item.Text == "Assistant" && item.Level == 1 && item.IsAssistant);

        var smartArt = new SmartArtShape { Data = data };
        smartArt.Parts["ppt/diagrams/data1.xml"] = new DiagramPart
        {
            PartPath = "ppt/diagrams/data1.xml",
            ContentType = "application/vnd.openxmlformats-officedocument.drawingml.diagramData+xml",
            Bytes = Encoding.UTF8.GetBytes("<dgm:dataModel xmlns:dgm=\"http://schemas.openxmlformats.org/drawingml/2006/diagram\" />")
        };
        SmartArtEditingPlanner.RewriteDataPart(smartArt).Applied.Should().BeTrue();
        var dgm = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/diagram");
        XDocument.Parse(Encoding.UTF8.GetString(smartArt.Parts["ppt/diagrams/data1.xml"].Bytes))
            .Descendants(dgm + "pt")
            .Single(pt => (string?)pt.Element(dgm + "t")?.Value == "Assistant")
            .Attribute("type")!.Value.Should().Be("asst");
    }

    [Fact]
    public void AddAssistant_RejectsNonHierarchyDataWithoutMutation()
    {
        var data = MakeFlatData(SmartArtFamily.Process, ("root", "Step"));

        var result = SmartArtEditingPlanner.Apply(
            data,
            SmartArtNodeEditIntent.AddAssistant("root"));

        result.Applied.Should().BeFalse();
        result.Message.Should().Be("Assistant nodes are supported only in hierarchy SmartArt.");
        data.Nodes.Should().ContainSingle();
        data.Nodes[0].Children.Should().BeEmpty();
    }

    [Fact]
    public void ApplyTextPaneOutline_SkippedParentLevelIsRejectedWithoutMutation()
    {
        var data = MakeFlatData(SmartArtFamily.Hierarchy, ("root", "Leader"), ("manager", "Manager"));

        var result = SmartArtEditingPlanner.ApplyTextPaneOutline(data,
        [
            new("Executive", 0, ModelId: "root"),
            new("Too Deep", 2, ModelId: "deep")
        ]);

        result.Applied.Should().BeFalse();
        result.Message.Should().Be("SmartArt text-pane levels cannot skip a parent level.");
        data.Nodes.Select(node => node.Text).Should().Equal("Leader", "Manager");
    }

    [Fact]
    public void ApplyTextPaneOutline_PreservesPicturePayloadsByStableNodeId()
    {
        var picture = new ImagePart { Bytes = [0x89, 0x50, 0x4E, 0x47], ContentType = "image/png" };
        var data = MakeFlatData(SmartArtFamily.List, ("a", "Alpha"), ("b", "Beta"));
        data.Nodes[1].Picture = picture;

        var result = SmartArtEditingPlanner.ApplyTextPaneOutline(data,
        [
            new("Beta revised", 0, ModelId: "b"),
            new("Alpha", 0, ModelId: "a")
        ]);

        result.Applied.Should().BeTrue();
        data.Nodes.Select(node => node.ModelId).Should().Equal("b", "a");
        data.Nodes[0].Picture.Should().BeSameAs(picture);
        data.Nodes[1].Picture.Should().BeNull();
    }

    [Fact]
    public void PictureCaptionListCacheRefresh_PreservesPictureRelationshipsAndSchemaRequiredMetadata()
    {
        var data = new SmartArtData
        {
            Family = SmartArtFamily.List,
            LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/pictureCaptionList",
            IsLiveLayoutSupported = true
        };
        data.Nodes.Add(new SmartArtNode
        {
            ModelId = "one",
            Text = "One",
            Level = 0,
            Picture = new ImagePart { Bytes = [1, 2, 3], ContentType = "image/png" }
        });
        data.Nodes.Add(new SmartArtNode
        {
            ModelId = "two",
            Text = "Two",
            Level = 0,
            Picture = new ImagePart { Bytes = [4, 5, 6], ContentType = "image/png" }
        });

        var smartArt = new SmartArtShape
        {
            Data = data,
            DrawingPartPath = "ppt/diagrams/drawing1.xml"
        };
        smartArt.Parts["ppt/diagrams/data1.xml"] = new DiagramPart
        {
            PartPath = "ppt/diagrams/data1.xml",
            ContentType = "application/vnd.openxmlformats-officedocument.drawingml.diagramData+xml",
            Bytes = Encoding.UTF8.GetBytes("<dgm:dataModel xmlns:dgm=\"http://schemas.openxmlformats.org/drawingml/2006/diagram\" />")
        };
        smartArt.Parts["ppt/diagrams/drawing1.xml"] = new DiagramPart
        {
            PartPath = "ppt/diagrams/drawing1.xml",
            ContentType = "application/vnd.ms-office.drawingml.diagramDrawing+xml",
            Bytes = Encoding.UTF8.GetBytes("<dsp:drawing xmlns:dsp=\"http://schemas.microsoft.com/office/drawing/2008/diagram\" />")
        };
        const string relationshipsNamespace = "http://schemas.openxmlformats.org/package/2006/relationships";
        smartArt.PartRels["ppt/diagrams/drawing1.xml"] = Encoding.UTF8.GetBytes($"""
            <Relationships xmlns="{relationshipsNamespace}">
              <Relationship Id="rIdPic1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="../media/one.png" />
              <Relationship Id="rIdPic2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="../media/two.png" />
            </Relationships>
            """);

        SmartArtEditingPlanner.ApplyTextPaneOutline(data,
        [
            new("One revised", 0, ModelId: "one"),
            new("Two revised", 0, ModelId: "two")
        ]).Applied.Should().BeTrue();

        var dataResult = SmartArtEditingPlanner.RewriteDataPart(smartArt);
        var cacheResult = SmartArtEditingPlanner.RegenerateDrawingCache(
            smartArt,
            FrameX,
            FrameY,
            FrameCx,
            FrameCy,
            DefaultTheme());

        dataResult.Applied.Should().BeTrue();
        cacheResult.Applied.Should().BeTrue();
        cacheResult.ShapeCount.Should().Be(4);

        var dgm = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/diagram");
        var a = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/main");
        var dsp = XNamespace.Get("http://schemas.microsoft.com/office/drawing/2008/diagram");
        var r = XNamespace.Get("http://schemas.openxmlformats.org/officeDocument/2006/relationships");
        var dataDocument = XDocument.Parse(Encoding.UTF8.GetString(smartArt.Parts["ppt/diagrams/data1.xml"].Bytes));
        dataDocument.Descendants(dgm + "t")
            .Select(t => t.Element(a + "bodyPr") is not null && t.Element(a + "lstStyle") is not null)
            .Should().OnlyContain(value => value);
        var cacheDocument = XDocument.Parse(Encoding.UTF8.GetString(smartArt.Parts["ppt/diagrams/drawing1.xml"].Bytes));
        cacheDocument.Descendants(dsp + "sp")
            .SelectMany(shape => shape.Descendants(a + "blip"))
            .Select(blip => (string?)blip.Attribute(r + "embed"))
            .Where(id => id is not null)
            .Should().Equal("rIdPic1", "rIdPic2");
    }

    [Fact]
    public void SetPictureAndClearPicture_UpdatesPayloadAndCachedMedia()
    {
        var data = new SmartArtData
        {
            Family = SmartArtFamily.List,
            LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/pictureCaptionList",
            IsLiveLayoutSupported = true,
        };
        data.Nodes.Add(new SmartArtNode
        {
            ModelId = "one",
            Text = "One",
            Picture = new ImagePart { Bytes = [1, 2, 3], ContentType = "image/png" },
        });
        data.Nodes.Add(new SmartArtNode
        {
            ModelId = "two",
            Text = "Two",
            Picture = new ImagePart { Bytes = [4, 5, 6], ContentType = "image/png" },
        });

        var smartArt = new SmartArtShape
        {
            Data = data,
            DrawingPartPath = "ppt/diagrams/drawing1.xml",
        };
        smartArt.Parts["ppt/diagrams/data1.xml"] = new DiagramPart
        {
            PartPath = "ppt/diagrams/data1.xml",
            ContentType = "application/vnd.openxmlformats-officedocument.drawingml.diagramData+xml",
            Bytes = Encoding.UTF8.GetBytes("<dgm:dataModel xmlns:dgm=\"http://schemas.openxmlformats.org/drawingml/2006/diagram\" />"),
        };
        smartArt.Parts["ppt/diagrams/drawing1.xml"] = new DiagramPart
        {
            PartPath = "ppt/diagrams/drawing1.xml",
            ContentType = "application/vnd.ms-office.drawingml.diagramDrawing+xml",
            Bytes = Encoding.UTF8.GetBytes("<dsp:drawing xmlns:dsp=\"http://schemas.microsoft.com/office/drawing/2008/diagram\" />"),
        };
        smartArt.Parts["ppt/media/one.png"] = new DiagramPart
        {
            PartPath = "ppt/media/one.png",
            ContentType = "image/png",
            Bytes = [1, 2, 3],
        };
        smartArt.Parts["ppt/media/two.png"] = new DiagramPart
        {
            PartPath = "ppt/media/two.png",
            ContentType = "image/png",
            Bytes = [4, 5, 6],
        };
        const string relationshipsNamespace = "http://schemas.openxmlformats.org/package/2006/relationships";
        smartArt.PartRels["ppt/diagrams/drawing1.xml"] = Encoding.UTF8.GetBytes($"""
            <Relationships xmlns="{relationshipsNamespace}">
              <Relationship Id="rIdPic1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="../media/one.png" />
              <Relationship Id="rIdPic2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="../media/two.png" />
            </Relationships>
            """);

        var result = SmartArtEditingPlanner.Apply(
            data,
            SmartArtNodeEditIntent.SetPicture(
                "two",
                new ImagePart { Bytes = [9, 8, 7, 6], ContentType = "image/png" }));
        result.Applied.Should().BeTrue();
        data.Nodes[1].Picture!.Bytes.Should().Equal(9, 8, 7, 6);

        SmartArtEditingPlanner.RewriteDataPart(smartArt).Applied.Should().BeTrue();
        SmartArtEditingPlanner.RegenerateDrawingCache(
            smartArt,
            FrameX,
            FrameY,
            FrameCx,
            FrameCy,
            DefaultTheme()).Applied.Should().BeTrue();

        smartArt.Parts["ppt/media/one.png"].Bytes.Should().Equal(1, 2, 3);
        smartArt.Parts["ppt/media/two.png"].Bytes.Should().Equal(9, 8, 7, 6);
        var drawing = XDocument.Parse(Encoding.UTF8.GetString(smartArt.Parts["ppt/diagrams/drawing1.xml"].Bytes));
        var r = XNamespace.Get("http://schemas.openxmlformats.org/officeDocument/2006/relationships");
        drawing.Descendants().Where(element => element.Name.LocalName == "blip")
            .Select(element => (string?)element.Attribute(r + "embed"))
            .Should().Equal("rIdPic1", "rIdPic2");

        var clearOne = SmartArtEditingPlanner.Apply(
            data,
            SmartArtNodeEditIntent.ClearPicture("two"));
        clearOne.Applied.Should().BeTrue(clearOne.Message);
        SmartArtEditingPlanner.RewriteDataPart(smartArt).Applied.Should().BeTrue();
        SmartArtEditingPlanner.RegenerateDrawingCache(
            smartArt,
            FrameX,
            FrameY,
            FrameCx,
            FrameCy,
            DefaultTheme()).Applied.Should().BeTrue();
        data.Nodes[1].Picture.Should().BeNull();
        smartArt.Parts.Should().NotContainKey("ppt/media/two.png");
        drawing = XDocument.Parse(Encoding.UTF8.GetString(smartArt.Parts["ppt/diagrams/drawing1.xml"].Bytes));
        drawing.Descendants().Where(element => element.Name.LocalName == "blip")
            .Select(element => (string?)element.Attribute(r + "embed"))
            .Should().Equal("rIdPic1");

        var clearLast = SmartArtEditingPlanner.Apply(
            data,
            SmartArtNodeEditIntent.ClearPicture("one"));
        clearLast.Applied.Should().BeTrue(clearLast.Message);
        SmartArtEditingPlanner.RewriteDataPart(smartArt).Applied.Should().BeTrue();
        SmartArtEditingPlanner.RegenerateDrawingCache(
            smartArt,
            FrameX,
            FrameY,
            FrameCx,
            FrameCy,
            DefaultTheme()).Applied.Should().BeTrue();
        data.Nodes[0].Picture.Should().BeNull();
        smartArt.Parts.Should().NotContainKey("ppt/media/one.png");
        Encoding.UTF8.GetString(smartArt.PartRels["ppt/diagrams/drawing1.xml"])
            .Should().NotContain("/image");
        drawing = XDocument.Parse(Encoding.UTF8.GetString(smartArt.Parts["ppt/diagrams/drawing1.xml"].Bytes));
        drawing.Descendants().Where(element => element.Name.LocalName == "blip")
            .Should().BeEmpty();
    }

    [Fact]
    public void TextPaneOutline_DataPartAndDrawingCacheRegenerationShareAppliedModel()
    {
        var data = MakeFlatData(SmartArtFamily.Hierarchy, ("root", "Leader"), ("manager", "Manager"));
        var smartArt = new SmartArtShape { Data = data, DrawingPartPath = "ppt/diagrams/drawing1.xml" };
        smartArt.Parts["ppt/diagrams/data1.xml"] = new DiagramPart
        {
            PartPath = "ppt/diagrams/data1.xml",
            ContentType = "application/vnd.openxmlformats-officedocument.drawingml.diagramData+xml",
            Bytes = Encoding.UTF8.GetBytes("<dgm:dataModel xmlns:dgm=\"http://schemas.openxmlformats.org/drawingml/2006/diagram\" />")
        };
        smartArt.Parts["ppt/diagrams/drawing1.xml"] = new DiagramPart
        {
            PartPath = "ppt/diagrams/drawing1.xml",
            ContentType = "application/vnd.ms-office.drawingml.diagramDrawing+xml",
            Bytes = Encoding.UTF8.GetBytes("<dsp:drawing xmlns:dsp=\"http://schemas.microsoft.com/office/drawing/2008/diagram\" />")
        };

        SmartArtEditingPlanner.ApplyTextPaneOutline(data,
        [
            new("Executive", 0, ModelId: "root"),
            new("Delivery Lead", 1, ModelId: "manager")
        ]).Applied.Should().BeTrue();

        var dataPart = SmartArtEditingPlanner.RewriteDataPart(smartArt);
        var cache = SmartArtEditingPlanner.RegenerateDrawingCache(
            smartArt,
            FrameX,
            FrameY,
            FrameCx,
            FrameCy,
            DefaultTheme());

        dataPart.Applied.Should().BeTrue();
        dataPart.ConnectionCount.Should().Be(1);
        cache.Applied.Should().BeTrue();
        cache.ShapeCount.Should().Be(3);

        var dgm = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/diagram");
        var a = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/main");
        var dataDoc = XDocument.Parse(Encoding.UTF8.GetString(smartArt.Parts["ppt/diagrams/data1.xml"].Bytes));
        dataDoc.Descendants(a + "t").Select(t => t.Value)
            .Should().Equal("Executive", "Delivery Lead");
        var connection = dataDoc.Descendants(dgm + "cxn").Should().ContainSingle().Which;
        connection.Attribute("destId")!.Value.Should().Be("manager");
        connection.Attribute("modelId").Should().NotBeNull();
        connection.Attribute("srcOrd").Should().NotBeNull();
        connection.Attribute("destOrd").Should().NotBeNull();

        smartArt.FallbackShapes.Select(shape => shape.PlainText)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Should().Equal("Executive", "Delivery Lead");
    }

    [Theory]
    [InlineData(SmartArtTextPaneShortcutKey.Enter, SmartArtTextPaneShortcutModifiers.None, SmartArtNodeEditKind.AddSiblingAfter, "smartart.text-pane.enter.add-sibling-after")]
    [InlineData(SmartArtTextPaneShortcutKey.Enter, SmartArtTextPaneShortcutModifiers.Control, SmartArtNodeEditKind.AddChild, "smartart.text-pane.ctrl-enter.add-child")]
    [InlineData(SmartArtTextPaneShortcutKey.Tab, SmartArtTextPaneShortcutModifiers.None, SmartArtNodeEditKind.Demote, "smartart.text-pane.tab.demote")]
    [InlineData(SmartArtTextPaneShortcutKey.Tab, SmartArtTextPaneShortcutModifiers.Shift, SmartArtNodeEditKind.Promote, "smartart.text-pane.shift-tab.promote")]
    [InlineData(SmartArtTextPaneShortcutKey.Up, SmartArtTextPaneShortcutModifiers.Alt | SmartArtTextPaneShortcutModifiers.Shift, SmartArtNodeEditKind.MoveUp, "smartart.text-pane.alt-shift-up.move-up")]
    [InlineData(SmartArtTextPaneShortcutKey.Down, SmartArtTextPaneShortcutModifiers.Alt | SmartArtTextPaneShortcutModifiers.Shift, SmartArtNodeEditKind.MoveDown, "smartart.text-pane.alt-shift-down.move-down")]
    [InlineData(SmartArtTextPaneShortcutKey.Delete, SmartArtTextPaneShortcutModifiers.None, SmartArtNodeEditKind.Remove, "smartart.text-pane.delete.remove")]
    public void PlanTextPaneKeyboardRoute_MapsSharedChordsToEditIntents(
        SmartArtTextPaneShortcutKey key,
        SmartArtTextPaneShortcutModifiers modifiers,
        SmartArtNodeEditKind expectedKind,
        string expectedRouteId)
    {
        var route = SmartArtEditingPlanner.PlanTextPaneKeyboardRoute(key, modifiers, " manager ");

        route.Should().NotBeNull();
        route!.RouteId.Should().Be(expectedRouteId);
        route.Key.Should().Be(key);
        route.Modifiers.Should().Be(modifiers);
        route.Intent.Kind.Should().Be(expectedKind);
        route.Intent.TargetModelId.Should().Be("manager");
    }

    [Fact]
    public void PlanTextPaneKeyboardRoute_RejectsUnownedChordsAndMissingSelection()
    {
        SmartArtEditingPlanner.PlanTextPaneKeyboardRoute(
                SmartArtTextPaneShortcutKey.Up,
                SmartArtTextPaneShortcutModifiers.None,
                "manager")
            .Should()
            .BeNull();

        SmartArtEditingPlanner.PlanTextPaneKeyboardRoute(
                SmartArtTextPaneShortcutKey.Tab,
                SmartArtTextPaneShortcutModifiers.None,
                "  ")
            .Should()
            .BeNull();
    }

    [Fact]
    public void PlanTextPaneKeyboardRoute_FeedsSharedModelEditsForHostAdapters()
    {
        var data = MakeFlatData(SmartArtFamily.Hierarchy, ("root", "Leader"), ("peer", "Peer"), ("manager", "Manager"));

        var addChild = SmartArtEditingPlanner.PlanTextPaneKeyboardRoute(
            SmartArtTextPaneShortcutKey.Enter,
            SmartArtTextPaneShortcutModifiers.Control,
            "manager");
        SmartArtEditingPlanner.Apply(data, addChild!.Intent).Applied.Should().BeTrue();

        var moveDown = SmartArtEditingPlanner.PlanTextPaneKeyboardRoute(
            SmartArtTextPaneShortcutKey.Down,
            SmartArtTextPaneShortcutModifiers.Alt | SmartArtTextPaneShortcutModifiers.Shift,
            "peer");
        SmartArtEditingPlanner.Apply(data, moveDown!.Intent).Applied.Should().BeTrue();

        var demote = SmartArtEditingPlanner.PlanTextPaneKeyboardRoute(
            SmartArtTextPaneShortcutKey.Tab,
            SmartArtTextPaneShortcutModifiers.None,
            "peer");
        SmartArtEditingPlanner.Apply(data, demote!.Intent).Applied.Should().BeTrue();

        data.Nodes.Select(node => node.ModelId).Should().Equal("root", "manager");
        data.Nodes[0].Children.Should().BeEmpty();
        data.Nodes[1].Children.Select(node => node.ModelId).Should().Equal("freep-smartart-node-4", "peer");
        data.Nodes[1].Children[0].Text.Should().Be(SmartArtEditingPlanner.DefaultNewNodeText);
        SmartArtEditingPlanner.BuildOutline(data)
            .Select(item => (item.ModelId, item.Level, item.SiblingIndex))
            .Should()
            .Equal(
                ("root", 0, 0),
                ("manager", 0, 1),
                ("freep-smartart-node-4", 1, 0),
                ("peer", 1, 1));
    }

    [Fact]
    public void RewriteDataPart_AfterSharedOutlineEdit_RegeneratesNativeDiagramData()
    {
        var data = MakeFlatData(SmartArtFamily.Hierarchy, ("root", "Leader"), ("manager", "Manager"));
        var smartArt = new SmartArtShape { Data = data };
        smartArt.Parts["ppt/diagrams/data1.xml"] = new DiagramPart
        {
            PartPath = "ppt/diagrams/data1.xml",
            ContentType = "application/vnd.openxmlformats-officedocument.drawingml.diagramData+xml",
            Bytes = Encoding.UTF8.GetBytes("<dgm:dataModel xmlns:dgm=\"http://schemas.openxmlformats.org/drawingml/2006/diagram\" />")
        };

        SmartArtEditingPlanner.Apply(data, SmartArtNodeEditIntent.ChangeText("manager", "Delivery Lead"));
        SmartArtEditingPlanner.Apply(data, SmartArtNodeEditIntent.Demote("manager"));

        var result = SmartArtEditingPlanner.RewriteDataPart(smartArt);

        result.Applied.Should().BeTrue();
        result.DataPartPath.Should().Be("ppt/diagrams/data1.xml");
        result.NodeCount.Should().Be(2);
        result.ConnectionCount.Should().Be(1);

        var dgm = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/diagram");
        var a = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/main");
        var doc = XDocument.Parse(Encoding.UTF8.GetString(smartArt.Parts["ppt/diagrams/data1.xml"].Bytes));

        doc.Descendants(dgm + "pt")
            .Select(pt => (Id: (string?)pt.Attribute("modelId"), Text: pt.Descendants(a + "t").Single().Value))
            .Should().Equal(("root", "Leader"), ("manager", "Delivery Lead"));

        doc.Descendants(dgm + "cxn")
            .Select(cxn => (
                Type: (string?)cxn.Attribute("type"),
                Source: (string?)cxn.Attribute("srcId"),
                Destination: (string?)cxn.Attribute("destId")))
            .Should().ContainSingle()
            .Which.Should().Be(("parOf", "root", "manager"));
    }

    [Fact]
    public void RewriteDataPart_PreservesAuthoredNonTreeConnectionsForLiveNodes()
    {
        var data = MakeFlatData(SmartArtFamily.Hierarchy, ("root", "Leader"), ("manager", "Manager"));
        var smartArt = new SmartArtShape { Data = data };
        smartArt.Parts["ppt/diagrams/data1.xml"] = new DiagramPart
        {
            PartPath = "ppt/diagrams/data1.xml",
            ContentType = "application/vnd.openxmlformats-officedocument.drawingml.diagramData+xml",
            Bytes = Encoding.UTF8.GetBytes("""
                <dgm:dataModel xmlns:dgm="http://schemas.openxmlformats.org/drawingml/2006/diagram">
                  <dgm:ptLst>
                    <dgm:pt modelId="root" type="node" />
                    <dgm:pt modelId="manager" type="node" />
                  </dgm:ptLst>
                  <dgm:cxnLst>
                    <dgm:cxn modelId="tree" type="parOf" srcId="root" destId="manager" />
                    <dgm:cxn modelId="presentation" type="presOf" srcId="manager" destId="root" />
                  </dgm:cxnLst>
                </dgm:dataModel>
                """)
        };

        SmartArtEditingPlanner.Apply(data, SmartArtNodeEditIntent.Demote("manager"));
        var result = SmartArtEditingPlanner.RewriteDataPart(smartArt);

        result.Applied.Should().BeTrue();
        result.ConnectionCount.Should().Be(2);

        var dgm = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/diagram");
        var document = XDocument.Parse(Encoding.UTF8.GetString(
            smartArt.Parts["ppt/diagrams/data1.xml"].Bytes));
        document.Descendants(dgm + "cxn")
            .Select(connection => (
                Id: (string?)connection.Attribute("modelId"),
                Type: (string?)connection.Attribute("type"),
                Source: (string?)connection.Attribute("srcId"),
                Destination: (string?)connection.Attribute("destId")))
            .Should()
            .Contain(("presentation", "presOf", "manager", "root"));
    }

    [Fact]
    public void RegenerateDrawingCache_AfterSharedOutlineEdit_RewritesDspDrawingFromLivePlan()
    {
        var data = MakeFlatData(SmartArtFamily.Hierarchy, ("root", "Leader"), ("manager", "Manager"));
        var smartArt = new SmartArtShape { Data = data, DrawingPartPath = "ppt/diagrams/drawing1.xml" };
        smartArt.Parts["ppt/diagrams/drawing1.xml"] = new DiagramPart
        {
            PartPath = "ppt/diagrams/drawing1.xml",
            ContentType = "application/vnd.ms-office.drawingml.diagramDrawing+xml",
            Bytes = Encoding.UTF8.GetBytes("<dsp:drawing xmlns:dsp=\"http://schemas.microsoft.com/office/drawing/2008/diagram\" />")
        };

        SmartArtEditingPlanner.Apply(data, SmartArtNodeEditIntent.ChangeText("manager", "Delivery Lead"))
            .Applied.Should().BeTrue();
        SmartArtEditingPlanner.Apply(data, SmartArtNodeEditIntent.Demote("manager"))
            .Applied.Should().BeTrue();

        var result = SmartArtEditingPlanner.RegenerateDrawingCache(
            smartArt,
            FrameX,
            FrameY,
            FrameCx,
            FrameCy,
            DefaultTheme());

        result.Applied.Should().BeTrue();
        result.DrawingPartPath.Should().Be("ppt/diagrams/drawing1.xml");
        result.NodeCount.Should().Be(2);
        result.ShapeCount.Should().Be(3, "the shared hierarchy plan emits two node boxes plus one connector");
        smartArt.FallbackShapes.Select(shape => shape.PlainText)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Should().Equal("Leader", "Delivery Lead");

        var dsp = XNamespace.Get("http://schemas.microsoft.com/office/drawing/2008/diagram");
        var a = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/main");
        var doc = XDocument.Parse(Encoding.UTF8.GetString(smartArt.Parts["ppt/diagrams/drawing1.xml"].Bytes));

        doc.Root!.Name.Should().Be(dsp + "drawing");
        doc.Descendants(dsp + "sp").Should().HaveCount(3);
        doc.Descendants(a + "t").Select(t => t.Value)
            .Should().Contain(["Leader", "Delivery Lead"])
            .And.NotContain("Manager");
    }

    [Fact]
    public void RegenerateDrawingCache_Hierarchy3UsesSharedLeftToRightLayout()
    {
        var root = new SmartArtNode { ModelId = "root", Text = "Portfolio", Level = 0 };
        root.Children.Add(new SmartArtNode { ModelId = "product", Text = "Product", Level = 1 });
        root.Children.Add(new SmartArtNode { ModelId = "operations", Text = "Operations", Level = 1 });
        var data = new SmartArtData
        {
            Family = SmartArtFamily.Hierarchy,
            LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/hierarchy3"
        };
        data.Nodes.Add(root);

        var smartArt = new SmartArtShape { Data = data, DrawingPartPath = "ppt/diagrams/drawing1.xml" };
        smartArt.Parts["ppt/diagrams/drawing1.xml"] = new DiagramPart
        {
            PartPath = "ppt/diagrams/drawing1.xml",
            ContentType = "application/vnd.ms-office.drawingml.diagramDrawing+xml",
            Bytes = Encoding.UTF8.GetBytes("<dsp:drawing xmlns:dsp=\"http://schemas.microsoft.com/office/drawing/2008/diagram\" />")
        };

        var result = SmartArtEditingPlanner.RegenerateDrawingCache(
            smartArt,
            FrameX,
            FrameY,
            FrameCx,
            FrameCy,
            DefaultTheme());

        result.Applied.Should().BeTrue(result.Message);
        result.ShapeCount.Should().Be(5, "hierarchy3 emits three boxes and two shared connectors");

        var boxes = smartArt.FallbackShapes
            .Where(shape => shape.AutoShapeKind == Free.Shared.Drawing.DrawingShapeKind.RoundedRectangle)
            .ToDictionary(shape => shape.PlainText, StringComparer.Ordinal);
        boxes["Product"].OffsetXEmu.Should().BeGreaterThan(boxes["Portfolio"].OffsetXEmu);
        boxes["Operations"].OffsetXEmu.Should().Be(boxes["Product"].OffsetXEmu);
    }

    [Fact]
    public void RegenerateDrawingCache_RadialListUsesSharedSpokePlan()
    {
        var data = MakeFlatData(
            SmartArtFamily.Cycle,
            ("one", "Discover"),
            ("two", "Plan"),
            ("three", "Build"),
            ("four", "Review"));
        data.LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/radialList";
        var smartArt = new SmartArtShape { Data = data, DrawingPartPath = "ppt/diagrams/drawing1.xml" };
        smartArt.Parts["ppt/diagrams/drawing1.xml"] = new DiagramPart
        {
            PartPath = "ppt/diagrams/drawing1.xml",
            ContentType = "application/vnd.ms-office.drawingml.diagramDrawing+xml",
            Bytes = Encoding.UTF8.GetBytes("<dsp:drawing xmlns:dsp=\"http://schemas.microsoft.com/office/drawing/2008/diagram\" />")
        };

        var result = SmartArtEditingPlanner.RegenerateDrawingCache(
            smartArt, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        result.Applied.Should().BeTrue(result.Message);
        result.NodeCount.Should().Be(4);
        result.ShapeCount.Should().Be(8, "radialList caches four live item boxes and four center spokes");
        smartArt.FallbackShapes.Count(shape => shape.AutoShapeKind == Free.Shared.Drawing.DrawingShapeKind.Line)
            .Should().Be(4);
        smartArt.FallbackShapes.Select(shape => shape.PlainText)
            .Should().Contain(["Discover", "Plan", "Build", "Review"]);
    }

    [Fact]
    public void RegenerateDrawingCache_TableHierarchyUsesSharedCellPlan()
    {
        var root = new SmartArtNode { ModelId = "root", Text = "Portfolio", Level = 0 };
        var owners = new SmartArtNode { ModelId = "owners", Text = "Owners", Level = 1 };
        owners.Children.Add(new SmartArtNode { ModelId = "owner-detail", Text = "Delivery", Level = 2 });
        var milestones = new SmartArtNode { ModelId = "milestones", Text = "Milestones", Level = 1 };
        milestones.Children.Add(new SmartArtNode { ModelId = "milestone-detail", Text = "Launch", Level = 2 });
        root.Children.Add(owners);
        root.Children.Add(milestones);

        var data = new SmartArtData
        {
            Family = SmartArtFamily.Hierarchy,
            LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/tableHierarchy"
        };
        data.Nodes.Add(root);

        var smartArt = new SmartArtShape { Data = data, DrawingPartPath = "ppt/diagrams/drawing1.xml" };
        smartArt.Parts["ppt/diagrams/drawing1.xml"] = new DiagramPart
        {
            PartPath = "ppt/diagrams/drawing1.xml",
            ContentType = "application/vnd.ms-office.drawingml.diagramDrawing+xml",
            Bytes = Encoding.UTF8.GetBytes("<dsp:drawing xmlns:dsp=\"http://schemas.microsoft.com/office/drawing/2008/diagram\" />")
        };

        var result = SmartArtEditingPlanner.RegenerateDrawingCache(
            smartArt, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        result.Applied.Should().BeTrue(result.Message);
        result.NodeCount.Should().Be(5);
        result.ShapeCount.Should().Be(5, "the cache must mirror the shared table cell plan");
        smartArt.FallbackShapes.Should().OnlyContain(shape =>
            shape.AutoShapeKind == Free.Shared.Drawing.DrawingShapeKind.Rectangle);
        smartArt.FallbackShapes.Should().NotContain(shape =>
            shape.AutoShapeKind == Free.Shared.Drawing.DrawingShapeKind.Line);

        var dsp = XNamespace.Get("http://schemas.microsoft.com/office/drawing/2008/diagram");
        var doc = XDocument.Parse(Encoding.UTF8.GetString(smartArt.Parts["ppt/diagrams/drawing1.xml"].Bytes));
        doc.Descendants(dsp + "sp").Should().HaveCount(5,
            "editing cache regeneration must persist the same five renderer-neutral cells");
    }

    [Fact]
    public void RegenerateDrawingCache_OrgChartUsesDedicatedAssistantBoxPlan()
    {
        var root = new SmartArtNode { ModelId = "root", Text = "CEO", Level = 0 };
        root.Children.Add(new SmartArtNode
        {
            ModelId = "assistant",
            Text = "Assistant",
            Level = 1,
            IsAssistant = true,
        });
        root.Children.Add(new SmartArtNode { ModelId = "director", Text = "Director", Level = 1 });
        var data = new SmartArtData
        {
            Family = SmartArtFamily.Hierarchy,
            LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/orgChart",
        };
        data.Nodes.Add(root);

        var smartArt = new SmartArtShape { Data = data, DrawingPartPath = "ppt/diagrams/drawing1.xml" };
        smartArt.Parts["ppt/diagrams/drawing1.xml"] = new DiagramPart
        {
            PartPath = "ppt/diagrams/drawing1.xml",
            ContentType = "application/vnd.ms-office.drawingml.diagramDrawing+xml",
            Bytes = Encoding.UTF8.GetBytes("<dsp:drawing xmlns:dsp=\"http://schemas.microsoft.com/office/drawing/2008/diagram\" />")
        };

        var result = SmartArtEditingPlanner.RegenerateDrawingCache(
            smartArt, FrameX, FrameY, FrameCx, FrameCy, DefaultTheme());

        result.Applied.Should().BeTrue(result.Message);
        result.NodeCount.Should().Be(3);
        result.ShapeCount.Should().Be(5, "three org-chart boxes plus two shared connectors are cached");
        smartArt.FallbackShapes
            .Where(shape => shape.TextBody is not null)
            .Should().OnlyContain(shape => shape.Name.StartsWith("SmartArt_OrgChartBox_", StringComparison.Ordinal));
        smartArt.FallbackShapes.Single(shape => shape.PlainText == "Assistant")
            .AutoShapeKind.Should().Be(Free.Shared.Drawing.DrawingShapeKind.Rectangle);
        smartArt.FallbackShapes.Select(shape => shape.PlainText)
            .Should().Contain(["CEO", "Assistant", "Director"]);

        var dsp = XNamespace.Get("http://schemas.microsoft.com/office/drawing/2008/diagram");
        var a = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/main");
        var doc = XDocument.Parse(Encoding.UTF8.GetString(smartArt.Parts["ppt/diagrams/drawing1.xml"].Bytes));
        doc.Descendants(dsp + "sp").Should().HaveCount(5);
        doc.Descendants(a + "t").Select(t => t.Value)
            .Should().Contain(["CEO", "Assistant", "Director"]);
    }

    private static PresentationTheme DefaultTheme() =>
        Presentation.CreateEmpty().Theme!;

    private static SmartArtData MakeFlatData(SmartArtFamily family, params (string Id, string Text)[] nodes)
    {
        var data = new SmartArtData { Family = family };
        foreach (var (id, text) in nodes)
            data.Nodes.Add(new SmartArtNode { ModelId = id, Text = text, Level = 0 });
        return data;
    }

    private static void AddLayoutPart(SmartArtShape smartArt) =>
        smartArt.Parts["ppt/diagrams/layout1.xml"] = new DiagramPart
        {
            PartPath = "ppt/diagrams/layout1.xml",
            ContentType = "application/vnd.openxmlformats-officedocument.drawingml.diagramLayout+xml",
            Bytes = Encoding.UTF8.GetBytes(
                "<dgm:root xmlns:dgm=\"http://schemas.openxmlformats.org/drawingml/2006/diagram\"><dgm:layoutDef uniqueId=\"old\" /></dgm:root>"),
        };
}
