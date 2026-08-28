using FreeP.App.Compositor;
using FreeP.Core.IO;
using FreeP.Core.Model;
using System.Xml.Linq;

namespace FreeP.App.Compositor.Tests;

public sealed class SummaryZoomInsertionPlannerTests
{
    [Fact]
    public void SelectOrderedTargets_UsesEditorOrderAndIgnoresUnselectedEntries()
    {
        var ordered = SummaryZoomTargetPlanner.SelectOrderedTargets(
            ["section-c", "section-a", "section-b"],
            ["section-b", "section-c"]);

        ordered.Should().Equal("section-c", "section-b");
    }

    [Fact]
    public void Builds_native_multi_target_summary_zoom_with_fixed_layout()
    {
        var presentation = BuildPresentation();

        SummaryZoomInsertionPlanner.TryBuildPlan(
            presentation,
            new[] { "{SECTION-ONE}", "{SECTION-TWO}", "{SECTION-THREE}" },
            out var plan).Should().BeTrue();
        plan.Targets.Should().HaveCount(3);
        plan.Targets.Select(target => target.SectionId)
            .Should().ContainInOrder("{SECTION-ONE}", "{SECTION-TWO}", "{SECTION-THREE}");

        var shape = SummaryZoomInsertionPlanner.CreateShape(
            presentation,
            plan.Targets.Select(target => target.SectionId));
        shape.PreservedObject!.SummaryZoomTargets.Should().HaveCount(3);
        shape.PreservedObject.RawXml.Should().Contain("summaryzoom");
        shape.PreservedObject.RawXml.Should().Contain("summaryZmObj");
        shape.PreservedObject.RawXml.Should().Contain("fixedLayout");
        XElement.Parse(shape.PreservedObject.RawXml).Descendants()
            .Count(element => element.Name.LocalName == "zmPr")
            .Should().Be(3);
    }

    [Fact]
    public void Insert_summary_zoom_with_control_character_in_section_name_succeeds()
    {
        // Round 165 meta finding F2: a control character embedded inside a word (no adjacent
        // whitespace) survives SlideSectionPlanner.NormalizeSectionName's whitespace-only collapsing
        // and lands in section.Name unchanged. BuildRawXml used to serialize that name straight into
        // an XAttribute and call XElement.ToString(), which validates on write and throws -- so the
        // real Insert Summary Zoom gesture (EditingSession.InsertSummaryZoom) failed outright before
        // AddShape ever ran. This drives the actual gesture and asserts it now succeeds.
        var presentation = new Presentation();
        presentation.Slides.Add(new Slide { Id = "slide-1", Title = "Slide 1" });
        presentation.Slides.Add(new Slide { Id = "slide-2", Title = "Slide 2" });
        AddSection(presentation, "{SECTION-ONE}", "Sec" + (char)0x01 + "tion", "slide-1");
        AddSection(presentation, "{SECTION-TWO}", "Two", "slide-2");
        var session = new EditingSession(presentation, new PresentationCommandBus(presentation));

        var shape = session.InsertSummaryZoom(new[] { "{SECTION-ONE}", "{SECTION-TWO}" });

        presentation.Slides[0].Shapes.Should().Contain(shape);
        var root = XElement.Parse(shape.PreservedObject!.RawXml);
        var titles = root.Descendants().Where(element => element.Name.LocalName == "summaryZmObj")
            .Select(element => element.Attribute("title")!.Value)
            .ToArray();
        titles.Should().Contain("Section");
        titles.Should().NotContain(name => name.Contains((char)0x01));
    }

    [Fact]
    public void Insert_summary_zoom_with_ordinary_section_name_preserves_it_exactly()
    {
        // Sibling no-regression case: an ordinary section name (no illegal characters) must survive
        // the sanitizer unchanged -- the fix must not alter well-formed titles.
        var presentation = BuildPresentation();
        var session = new EditingSession(presentation, new PresentationCommandBus(presentation));

        var shape = session.InsertSummaryZoom(new[] { "{SECTION-ONE}", "{SECTION-TWO}" });

        var root = XElement.Parse(shape.PreservedObject!.RawXml);
        var titles = root.Descendants().Where(element => element.Name.LocalName == "summaryZmObj")
            .Select(element => element.Attribute("title")!.Value)
            .ToArray();
        titles.Should().Contain("One");
        titles.Should().Contain("Two");
    }

    [Fact]
    public void Summary_zoom_requires_two_valid_sections_and_undoes()
    {
        var presentation = BuildPresentation();
        var session = new EditingSession(presentation, new PresentationCommandBus(presentation));

        SummaryZoomInsertionPlanner.TryBuildPlan(
            presentation,
            new[] { "{SECTION-ONE}" },
            out _).Should().BeFalse();
        SummaryZoomInsertionPlanner.TryBuildPlan(
            presentation,
            new[] { "{SECTION-ONE}", "{MISSING}" },
            out _).Should().BeFalse();

        var shape = session.InsertSummaryZoom(new[] { "{SECTION-ONE}", "{SECTION-TWO}" });
        presentation.Slides[0].Shapes.Should().Contain(shape);
        session.Undo();
        presentation.Slides[0].Shapes.Should().NotContain(shape);
        session.Redo();
        presentation.Slides[0].Shapes.Should().ContainSingle(item => item.Kind == SlideShapeKind.Zoom);
    }

    [Fact]
    public void Summary_zoom_navigation_uses_the_clicked_tile()
    {
        var presentation = BuildPresentation();
        var shape = SummaryZoomInsertionPlanner.CreateShape(
            presentation,
            new[] { "{SECTION-ONE}", "{SECTION-TWO}", "{SECTION-THREE}" });

        ZoomNavigationService.TryGetTargetSlideIndex(
            presentation, shape.PreservedObject, 0.75, 0.25, out var targetIndex).Should().BeTrue();
        targetIndex.Should().Be(1);

        ZoomNavigationService.TryGetTargetSlideIndex(
            presentation, shape.PreservedObject, 0.25, 0.75, out targetIndex).Should().BeTrue();
        targetIndex.Should().Be(2);
    }

    [Fact]
    public void Zoom_properties_are_undoable_and_update_every_summary_tile()
    {
        var presentation = BuildPresentation();
        var session = new EditingSession(presentation, new PresentationCommandBus(presentation));
        var shape = session.InsertSummaryZoom(new[] { "{SECTION-ONE}", "{SECTION-TWO}", "{SECTION-THREE}" });
        var properties = new ZoomObjectProperties(
            ReturnToParent: false,
            ImageType: "cover",
            TransitionDuration: "120000",
            ShowBackground: false);

        session.SetZoomObjectProperties(shape.Id, properties).Should().BeTrue();
        shape.PreservedObject!.ZoomProperties.Should().Be(properties);
        var xml = XElement.Parse(shape.PreservedObject.RawXml);
        xml.Descendants().Where(element => element.Name.LocalName == "zmPr")
            .Should().AllSatisfy(element =>
            {
                element.Attribute("returnToParent")!.Value.Should().Be("0");
                element.Attribute("imageType")!.Value.Should().Be("cover");
                element.Attribute("transitionDur")!.Value.Should().Be("120000");
                element.Attribute("showBg")!.Value.Should().Be("0");
            });

        session.Undo();
        shape.PreservedObject.ZoomProperties.Should()
            .Be(new ZoomObjectProperties(true, "preview", null, true));
        shape.PreservedObject.RawXml.Should().Contain("imageType=\"preview\"");

        session.Redo();
        shape.PreservedObject.ZoomProperties.Should().Be(properties);
        shape.PreservedObject.RawXml.Should().Contain("imageType=\"cover\"");
    }

    [Fact]
    public void Summary_zoom_all_tile_format_scope_normalizes_divergent_tiles_and_undoes_atomically()
    {
        var presentation = BuildPresentation();
        var session = new EditingSession(presentation, new PresentationCommandBus(presentation));
        var shape = session.InsertSummaryZoom(new[] { "{SECTION-ONE}", "{SECTION-TWO}" });
        var firstTile = new ZoomObjectProperties(ReturnToParent: false, ImageType: "cover");
        var allTiles = new ZoomObjectProperties(ReturnToParent: true, ImageType: "preview");

        session.SetSummaryZoomTileProperties(shape.Id, "{SECTION-ONE}", firstTile).Should().BeTrue();
        session.SetZoomObjectProperties(shape.Id, allTiles).Should().BeTrue();

        var tiles = XElement.Parse(shape.PreservedObject!.RawXml).Descendants()
            .Where(element => element.Name.LocalName == "summaryZmObj")
            .ToArray();
        tiles.Should().HaveCount(2);
        tiles.Should().AllSatisfy(tile =>
            tile.Descendants().Single(element => element.Name.LocalName == "zmPr")
                .Attribute("imageType")!.Value.Should().Be("preview"));

        session.Undo();
        XElement.Parse(shape.PreservedObject.RawXml).Descendants()
            .Single(element => element.Name.LocalName == "summaryZmObj"
                && element.Attribute("sectionId")?.Value == "{SECTION-ONE}")
            .Descendants().Single(element => element.Name.LocalName == "zmPr")
            .Attribute("imageType")!.Value.Should().Be("cover");
    }

    [Fact]
    public void Summary_zoom_tile_properties_are_individual_and_drive_navigation()
    {
        var presentation = BuildPresentation();
        var session = new EditingSession(presentation, new PresentationCommandBus(presentation));
        var shape = session.InsertSummaryZoom(new[] { "{SECTION-ONE}", "{SECTION-TWO}" });
        var firstProperties = new ZoomObjectProperties(
            ReturnToParent: false,
            ImageType: "cover",
            TransitionDuration: "90000",
            ShowBackground: false,
            CropLeft: 5000);

        session.SetSummaryZoomTileProperties(
            shape.Id, "{SECTION-ONE}", firstProperties).Should().BeTrue();

        var root = XElement.Parse(shape.PreservedObject!.RawXml);
        var firstTile = root.Descendants().Single(element =>
            element.Name.LocalName == "summaryZmObj"
            && element.Attribute("sectionId")?.Value == "{SECTION-ONE}");
        var secondTile = root.Descendants().Single(element =>
            element.Name.LocalName == "summaryZmObj"
            && element.Attribute("sectionId")?.Value == "{SECTION-TWO}");
        firstTile.Descendants().Single(element => element.Name.LocalName == "zmPr")
            .Attribute("imageType")!.Value.Should().Be("cover");
        firstTile.Descendants().Single(element => element.Name.LocalName == "zmPr")
            .Descendants().Single(element => element.Name.LocalName == "srcRect")
            .Attribute("l")!.Value.Should().Be("5000");
        secondTile.Descendants().Single(element => element.Name.LocalName == "zmPr")
            .Attribute("imageType")!.Value.Should().Be("preview");

        ZoomNavigationService.TryGetTargetSlideIndex(
            presentation,
            shape.PreservedObject,
            0.25,
            0.25,
            out _,
            out var returnToParent,
            out var transitionDuration,
            out var showBackground).Should().BeTrue();
        returnToParent.Should().BeFalse();
        transitionDuration.Should().Be(90000);
        showBackground.Should().BeFalse();

        session.Undo();
        XElement.Parse(shape.PreservedObject.RawXml).Descendants()
            .Single(element => element.Name.LocalName == "summaryZmObj"
                && element.Attribute("sectionId")?.Value == "{SECTION-ONE}")
            .Descendants().Single(element => element.Name.LocalName == "zmPr")
            .Attribute("imageType")!.Value.Should().Be("preview");
    }

    [Fact]
    public void Summary_zoom_targets_can_be_reordered_and_removed_without_losing_retained_tile_state()
    {
        var presentation = BuildPresentation();
        var session = new EditingSession(presentation, new PresentationCommandBus(presentation));
        var shape = session.InsertSummaryZoom(new[] { "{SECTION-ONE}", "{SECTION-TWO}", "{SECTION-THREE}" });

        session.SetSummaryZoomTileProperties(
            shape.Id,
            "{SECTION-ONE}",
            new ZoomObjectProperties(ImageType: "cover")).Should().BeTrue();

        session.SetSummaryZoomTargets(
            shape.Id,
            new[] { "{SECTION-THREE}", "{SECTION-ONE}" }).Should().BeTrue();

        shape.PreservedObject!.SummaryZoomTargets.Select(target => target.SectionId)
            .Should().ContainInOrder("{SECTION-THREE}", "{SECTION-ONE}");
        var tiles = XElement.Parse(shape.PreservedObject.RawXml)
            .Descendants().Where(element => element.Name.LocalName == "summaryZmObj").ToArray();
        tiles.Select(tile => tile.Attribute("sectionId")!.Value)
            .Should().ContainInOrder("{SECTION-THREE}", "{SECTION-ONE}");
        tiles.Should().NotContain(tile => tile.Attribute("sectionId")!.Value == "{SECTION-TWO}");
        tiles.Single(tile => tile.Attribute("sectionId")!.Value == "{SECTION-ONE}")
            .Descendants().Single(element => element.Name.LocalName == "zmPr")
            .Attribute("imageType")!.Value.Should().Be("cover");

        session.Undo();
        shape.PreservedObject.SummaryZoomTargets.Select(target => target.SectionId)
            .Should().ContainInOrder("{SECTION-ONE}", "{SECTION-TWO}", "{SECTION-THREE}");
        session.Redo();
        shape.PreservedObject.SummaryZoomTargets.Select(target => target.SectionId)
            .Should().ContainInOrder("{SECTION-THREE}", "{SECTION-ONE}");
    }

    [Fact]
    public void Edited_summary_zoom_target_order_reopens_from_pptx()
    {
        var presentation = BuildPresentation();
        var session = new EditingSession(presentation, new PresentationCommandBus(presentation));
        var shape = session.InsertSummaryZoom(new[] { "{SECTION-ONE}", "{SECTION-TWO}", "{SECTION-THREE}" });

        session.SetSummaryZoomTargets(
            shape.Id,
            new[] { "{SECTION-THREE}", "{SECTION-ONE}" }).Should().BeTrue();

        using var stream = new MemoryStream();
        PptxPackageWriter.Write(presentation, stream);
        var reopened = PptxPackageReader.Read(new MemoryStream(stream.ToArray()));
        reopened.Slides[0].Shapes.Single(candidate => candidate.Kind == SlideShapeKind.Zoom)
            .PreservedObject!.SummaryZoomTargets.Select(target => target.SectionId)
            .Should().ContainInOrder("{SECTION-THREE}", "{SECTION-ONE}");
    }

    [Fact]
    public void Summary_zoom_tile_cover_images_are_individual_and_undoable()
    {
        var presentation = BuildPresentation();
        var session = new EditingSession(presentation, new PresentationCommandBus(presentation));
        var shape = session.InsertSummaryZoom(new[] { "{SECTION-ONE}", "{SECTION-TWO}" });
        var firstImage = new byte[] { 1, 2, 3 };
        var secondImage = new byte[] { 4, 5, 6 };

        session.SetSummaryZoomTileCoverImage(
            shape.Id, "{SECTION-ONE}", firstImage, "image/png").Should().BeTrue();
        session.SetSummaryZoomTileCoverImage(
            shape.Id, "{SECTION-TWO}", secondImage, "image/jpeg").Should().BeTrue();

        var root = XElement.Parse(shape.PreservedObject!.RawXml);
        var tiles = root.Descendants().Where(element => element.Name.LocalName == "summaryZmObj").ToArray();
        tiles.Should().HaveCount(2);
        var firstTile = tiles.Single(tile => tile.Attribute("sectionId")?.Value == "{SECTION-ONE}");
        firstTile.Descendants().Any(element =>
            element.Name.LocalName == "zmPr"
            && element.Attribute("imageType")?.Value == "cover").Should().BeTrue();
        var secondTile = tiles.Single(tile => tile.Attribute("sectionId")?.Value == "{SECTION-TWO}");
        secondTile.Descendants().Any(element =>
            element.Name.LocalName == "zmPr"
            && element.Attribute("imageType")?.Value == "cover").Should().BeTrue();
        shape.PreservedObject.Parts.Values.Should().Contain(bytes => bytes.SequenceEqual(firstImage));
        shape.PreservedObject.Parts.Values.Should().Contain(bytes => bytes.SequenceEqual(secondImage));
        shape.PreservedObject.Parts.Keys.Should().OnlyHaveUniqueItems();

        session.Undo();
        XElement.Parse(shape.PreservedObject.RawXml).Descendants()
            .Single(element => element.Name.LocalName == "summaryZmObj"
                && element.Attribute("sectionId")?.Value == "{SECTION-TWO}")
            .Descendants().Single(element => element.Name.LocalName == "zmPr")
            .Attribute("imageType")!.Value.Should().Be("preview");
        shape.PreservedObject.Parts.Values.Should().ContainSingle().Which.Should().BeEquivalentTo(firstImage);

        session.Redo();
        shape.PreservedObject.Parts.Values.Should().Contain(bytes => bytes.SequenceEqual(firstImage));
        shape.PreservedObject.Parts.Values.Should().Contain(bytes => bytes.SequenceEqual(secondImage));
    }

    private static Presentation BuildPresentation()
    {
        var presentation = new Presentation();
        for (var index = 1; index <= 4; index++)
            presentation.Slides.Add(new Slide { Id = $"slide-{index}", Title = $"Slide {index}" });

        AddSection(presentation, "{SECTION-ONE}", "One", "slide-1");
        AddSection(presentation, "{SECTION-TWO}", "Two", "slide-2");
        AddSection(presentation, "{SECTION-THREE}", "Three", "slide-3");
        AddSection(presentation, "{SECTION-EMPTY}", "Empty");
        return presentation;
    }

    [Fact]
    public void Retargeting_a_summary_zoom_with_a_control_character_in_a_section_name_succeeds()
    {
        // r165 remediation. The insertion planner was fixed for this, but Edit Summary Zoom Targets
        // goes through a SECOND, hand-written serializer (SummaryZoomTargetPlanner.SetTileAttributes)
        // that the insertion fix did not reach -- so the same section name that could no longer break
        // Insert still broke retargeting. Same defect, same file family, different command.
        var presentation = new Presentation();
        presentation.Slides.Add(new Slide { Id = "slide-1", Title = "Slide 1" });
        presentation.Slides.Add(new Slide { Id = "slide-2", Title = "Slide 2" });
        AddSection(presentation, "{SECTION-ONE}", "Sec" + (char)0x01 + "tion", "slide-1");
        AddSection(presentation, "{SECTION-TWO}", "Two", "slide-2");
        var session = new EditingSession(presentation, new PresentationCommandBus(presentation));
        var shape = session.InsertSummaryZoom(new[] { "{SECTION-ONE}", "{SECTION-TWO}" });

        var act = () => session.SetSummaryZoomTargets(shape.Id, new[] { "{SECTION-TWO}", "{SECTION-ONE}" });

        act.Should().NotThrow();
        var root = XElement.Parse(shape.PreservedObject!.RawXml);
        root.Descendants().Where(element => element.Name.LocalName == "summaryZmObj")
            .Select(element => element.Attribute("title")!.Value)
            .Should().NotContain(name => name.Contains((char)0x01));
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
}
