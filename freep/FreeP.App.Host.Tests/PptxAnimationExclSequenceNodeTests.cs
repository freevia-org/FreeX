using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using FreeP.Core.IO;
using FreeP.Core.Model;

namespace FreeP.App.Host.Tests;

/// <summary>
/// round171 F1: <c>PptxPackageReader.ReadAnimations</c> walked a <c>p:timing</c> tree's
/// <c>childTnLst</c> nodes by scanning only <c>.Elements(P + "par")</c>, at both the mainSeq/
/// trigger-seq click-group level and the build-item level inside each click group. Per
/// ECMA-376's EG_TLTimeNodeList, a childTnLst may legally contain <c>p:excl</c> or nested
/// <c>p:seq</c> siblings of <c>p:par</c> -- real PowerPoint emits <c>p:excl</c> for a
/// click-triggered "Play Media" animation, to make its playback exclusive. Because the reader
/// never called <c>.Elements(P + "excl")</c> or <c>.Elements(P + "seq")</c>, a build item nested
/// inside one of those wrappers was invisible: not added to <see cref="Slide.Animations"/>, not
/// preserved, not logged -- and because the OUTER click-group p:par (the one carrying the
/// onClick condition) was still visible to the outer scan and still consumed a click slot, every
/// later click-triggered animation on the same slide fired one click earlier than authored.
///
/// The fix makes both traversal levels descend transparently into any <c>p:excl</c>/<c>p:seq</c>
/// wrapper (see <c>PptxPackageReader.EnumerateTimeNodeParElements</c>), so the wrapped animation
/// is read into <see cref="Slide.Animations"/> at the same position it would have occupied as a
/// bare <c>p:par</c>, preserving both the animation itself and the click-step ordering instead of
/// silently discarding the node.
/// </summary>
public sealed class PptxAnimationExclSequenceNodeTests
{
    private static readonly XNamespace P = "http://schemas.openxmlformats.org/presentationml/2006/main";
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PkgRel = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace Ct = "http://schemas.openxmlformats.org/package/2006/content-types";

    /// <summary>
    /// A minimal click-triggered build item ("presetClass=entr" preset animation) targeting the
    /// given shape id. Matches the shape used by FreeP's own writer output closely enough for
    /// <c>ReadBuildItem</c> to accept it (presetClass/presetID attributes + a descendant
    /// <c>p:spTgt</c>).
    /// </summary>
    private static XElement BuildItemPar(uint shapeId, int presetId)
    {
        return new XElement(P + "par",
            new XElement(P + "cTn",
                new XAttribute("presetClass", "entr"),
                new XAttribute("presetID", presetId),
                new XAttribute("presetSubtype", "0"),
                new XAttribute("fill", "hold"),
                new XElement(P + "childTnLst",
                    new XElement(P + "set",
                        new XElement(P + "cBhvr",
                            new XElement(P + "cTn"),
                            new XElement(P + "tgtEl",
                                new XElement(P + "spTgt", new XAttribute("spid", shapeId))))))));
    }

    /// <summary>
    /// One mainSeq click group: <c>p:par[stCondLst/cond delay=indefinite] > p:cTn > p:childTnLst</c>
    /// containing either the build item directly (<paramref name="wrapInExcl"/> = false, the
    /// ordinary case every other click-triggered animation uses) or the build item nested inside a
    /// <c>p:excl</c> wrapper (<paramref name="wrapInExcl"/> = true, mirroring real PowerPoint's
    /// click-triggered "Play Media" nesting).
    /// </summary>
    private static XElement ClickGroup(XElement buildItem, bool wrapInExcl)
    {
        XElement childContent = wrapInExcl
            ? new XElement(P + "excl",
                new XElement(P + "cTn",
                    new XElement(P + "childTnLst", buildItem)))
            : buildItem;

        return new XElement(P + "par",
            new XElement(P + "cTn",
                new XElement(P + "stCondLst",
                    new XElement(P + "cond", new XAttribute("delay", "indefinite"))),
                new XElement(P + "childTnLst", childContent)));
    }

    /// <summary>
    /// Builds the smallest valid PPTX zip with one slide (two shapes, id=2 and id=3) whose
    /// p:timing mainSeq contains the three click steps from the finding's repro, in order:
    /// (1) OnClick Fade on shape 2 (bare p:par); (2) OnClick group whose sole child is
    /// &lt;p:excl&gt; wrapping an Entrance/Appear build item on shape 3; (3) OnClick Fade on
    /// shape 3 (bare p:par).
    /// </summary>
    private static byte[] BuildPptxWithExclWrappedClickStep()
    {
        var clickGroup1 = ClickGroup(BuildItemPar(shapeId: 2, presetId: 10), wrapInExcl: false); // Fade
        var clickGroup2 = ClickGroup(BuildItemPar(shapeId: 3, presetId: 1), wrapInExcl: true);    // Appear, excl-wrapped
        var clickGroup3 = ClickGroup(BuildItemPar(shapeId: 3, presetId: 10), wrapInExcl: false);  // Fade

        var timingEl = new XElement(P + "timing",
            new XElement(P + "tnLst",
                new XElement(P + "par",
                    new XElement(P + "cTn",
                        new XElement(P + "childTnLst",
                            new XElement(P + "seq",
                                new XElement(P + "cTn",
                                    new XAttribute("nodeType", "mainSeq"),
                                    new XElement(P + "childTnLst", clickGroup1, clickGroup2, clickGroup3))))))));

        var slideXml = new XDocument(
            new XElement(P + "sld",
                new XAttribute(XNamespace.Xmlns + "p", P.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "a", A.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "r", R.NamespaceName),
                new XElement(P + "cSld",
                    new XElement(P + "spTree",
                        new XElement(P + "nvGrpSpPr",
                            new XElement(P + "cNvPr", new XAttribute("id", "1"), new XAttribute("name", "Group 1")),
                            new XElement(P + "cNvGrpSpPr"),
                            new XElement(P + "nvPr")),
                        new XElement(P + "grpSpPr"),
                        BuildSp(2, "Shape2"),
                        BuildSp(3, "Shape3"))),
                timingEl));

        var presXml = new XDocument(
            new XElement(P + "presentation",
                new XAttribute(XNamespace.Xmlns + "p", P.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "r", R.NamespaceName),
                new XElement(P + "sldSz", new XAttribute("cx", "9144000"), new XAttribute("cy", "6858000")),
                new XElement(P + "sldIdLst",
                    new XElement(P + "sldId", new XAttribute("id", "256"), new XAttribute(R + "id", "rId1")))));

        var contentTypes = new XDocument(
            new XElement(Ct + "Types",
                new XElement(Ct + "Default", new XAttribute("Extension", "rels"),
                    new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
                new XElement(Ct + "Default", new XAttribute("Extension", "xml"),
                    new XAttribute("ContentType", "application/xml")),
                new XElement(Ct + "Override", new XAttribute("PartName", "/ppt/presentation.xml"),
                    new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml")),
                new XElement(Ct + "Override", new XAttribute("PartName", "/ppt/slides/slide1.xml"),
                    new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.presentationml.slide+xml"))));

        var rootRels = new XDocument(
            new XElement(PkgRel + "Relationships",
                new XElement(PkgRel + "Relationship", new XAttribute("Id", "rId1"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"),
                    new XAttribute("Target", "ppt/presentation.xml"))));

        var presRels = new XDocument(
            new XElement(PkgRel + "Relationships",
                new XElement(PkgRel + "Relationship", new XAttribute("Id", "rId1"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/slide"),
                    new XAttribute("Target", "slides/slide1.xml"))));

        var slideRels = new XDocument(new XElement(PkgRel + "Relationships"));

        using var outMs = new MemoryStream();
        using (var zip = new ZipArchive(outMs, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(zip, "[Content_Types].xml", contentTypes);
            WriteEntry(zip, "_rels/.rels", rootRels);
            WriteEntry(zip, "ppt/presentation.xml", presXml);
            WriteEntry(zip, "ppt/_rels/presentation.xml.rels", presRels);
            WriteEntry(zip, "ppt/slides/slide1.xml", slideXml);
            WriteEntry(zip, "ppt/slides/_rels/slide1.xml.rels", slideRels);
        }
        return outMs.ToArray();
    }

    private static XElement BuildSp(uint id, string name)
    {
        return new XElement(P + "sp",
            new XElement(P + "nvSpPr",
                new XElement(P + "cNvPr", new XAttribute("id", id), new XAttribute("name", name)),
                new XElement(P + "cNvSpPr"),
                new XElement(P + "nvPr")),
            new XElement(P + "spPr",
                new XElement(A + "xfrm",
                    new XElement(A + "off", new XAttribute("x", "0"), new XAttribute("y", "0")),
                    new XElement(A + "ext", new XAttribute("cx", "914400"), new XAttribute("cy", "914400"))),
                new XElement(A + "prstGeom", new XAttribute("prst", "rect"))),
            new XElement(P + "txBody",
                new XElement(A + "bodyPr"),
                new XElement(A + "p")));
    }

    private static void WriteEntry(ZipArchive zip, string path, XDocument doc)
    {
        var entry = zip.CreateEntry(path);
        using var stream = entry.Open();
        doc.Save(stream);
    }

    [Fact]
    public void Read_ClickStepWrappedInExcl_IsNotSilentlyDropped()
    {
        var bytes = BuildPptxWithExclWrappedClickStep();
        using var ms = new MemoryStream(bytes);

        var loaded = PptxPackageReader.Read(ms);

        // Before the fix: only 2 animations survive (shape2 Fade, shape3 Fade) -- the excl-wrapped
        // Appear on shape3 vanishes entirely.
        Assert.Equal(3, loaded.Slides[0].Animations.Count);
    }

    [Fact]
    public void Read_ClickStepWrappedInExcl_PreservesClickOrderAndContent()
    {
        var bytes = BuildPptxWithExclWrappedClickStep();
        using var ms = new MemoryStream(bytes);

        var loaded = PptxPackageReader.Read(ms);
        var animations = loaded.Slides[0].Animations;

        Assert.Equal(3, animations.Count);

        // Step 1: OnClick Fade on shape 2 -- unaffected sibling case.
        Assert.Equal(2u, animations[0].ShapeId);
        Assert.Equal(AnimationPreset.Fade, animations[0].Preset);
        Assert.Equal(AnimationTrigger.OnClick, animations[0].Trigger);

        // Step 2: the excl-wrapped Appear on shape 3 -- must be preserved, not dropped, and must
        // still occupy its own click slot (its own onClick trigger), not silently merge into an
        // adjacent step.
        Assert.Equal(3u, animations[1].ShapeId);
        Assert.Equal(AnimationPreset.Appear, animations[1].Preset);
        Assert.Equal(AnimationTrigger.OnClick, animations[1].Trigger);

        // Step 3: OnClick Fade on shape 3 -- must still report OnClick (not desynced to fire
        // alongside/instead of step 2 because step 2's click slot was silently eaten).
        Assert.Equal(3u, animations[2].ShapeId);
        Assert.Equal(AnimationPreset.Fade, animations[2].Preset);
        Assert.Equal(AnimationTrigger.OnClick, animations[2].Trigger);
    }

    /// <summary>
    /// Sibling no-regression check: the ordinary, un-wrapped click-triggered animation path (every
    /// click group whose childTnLst holds a bare p:par, with no p:excl/p:seq involved at all) must
    /// keep behaving exactly as before -- both count and per-step content.
    /// </summary>
    [Fact]
    public void Read_OrdinaryUnwrappedClickSteps_StillReadCorrectly_NoRegression()
    {
        var clickGroup1 = ClickGroup(BuildItemPar(shapeId: 2, presetId: 10), wrapInExcl: false);
        var clickGroup2 = ClickGroup(BuildItemPar(shapeId: 3, presetId: 1), wrapInExcl: false);

        var timingEl = new XElement(P + "timing",
            new XElement(P + "tnLst",
                new XElement(P + "par",
                    new XElement(P + "cTn",
                        new XElement(P + "childTnLst",
                            new XElement(P + "seq",
                                new XElement(P + "cTn",
                                    new XAttribute("nodeType", "mainSeq"),
                                    new XElement(P + "childTnLst", clickGroup1, clickGroup2))))))));

        var bytes = BuildMinimalPptxForTiming(timingEl);
        using var ms = new MemoryStream(bytes);

        var loaded = PptxPackageReader.Read(ms);
        var animations = loaded.Slides[0].Animations;

        Assert.Equal(2, animations.Count);
        Assert.Equal(2u, animations[0].ShapeId);
        Assert.Equal(AnimationPreset.Fade, animations[0].Preset);
        Assert.Equal(3u, animations[1].ShapeId);
        Assert.Equal(AnimationPreset.Appear, animations[1].Preset);
    }

    private static byte[] BuildMinimalPptxForTiming(XElement timingEl)
    {
        var slideXml = new XDocument(
            new XElement(P + "sld",
                new XAttribute(XNamespace.Xmlns + "p", P.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "a", A.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "r", R.NamespaceName),
                new XElement(P + "cSld",
                    new XElement(P + "spTree",
                        new XElement(P + "nvGrpSpPr",
                            new XElement(P + "cNvPr", new XAttribute("id", "1"), new XAttribute("name", "Group 1")),
                            new XElement(P + "cNvGrpSpPr"),
                            new XElement(P + "nvPr")),
                        new XElement(P + "grpSpPr"),
                        BuildSp(2, "Shape2"),
                        BuildSp(3, "Shape3"))),
                timingEl));

        var presXml = new XDocument(
            new XElement(P + "presentation",
                new XAttribute(XNamespace.Xmlns + "p", P.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "r", R.NamespaceName),
                new XElement(P + "sldSz", new XAttribute("cx", "9144000"), new XAttribute("cy", "6858000")),
                new XElement(P + "sldIdLst",
                    new XElement(P + "sldId", new XAttribute("id", "256"), new XAttribute(R + "id", "rId1")))));

        var contentTypes = new XDocument(
            new XElement(Ct + "Types",
                new XElement(Ct + "Default", new XAttribute("Extension", "rels"),
                    new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
                new XElement(Ct + "Default", new XAttribute("Extension", "xml"),
                    new XAttribute("ContentType", "application/xml")),
                new XElement(Ct + "Override", new XAttribute("PartName", "/ppt/presentation.xml"),
                    new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml")),
                new XElement(Ct + "Override", new XAttribute("PartName", "/ppt/slides/slide1.xml"),
                    new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.presentationml.slide+xml"))));

        var rootRels = new XDocument(
            new XElement(PkgRel + "Relationships",
                new XElement(PkgRel + "Relationship", new XAttribute("Id", "rId1"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"),
                    new XAttribute("Target", "ppt/presentation.xml"))));

        var presRels = new XDocument(
            new XElement(PkgRel + "Relationships",
                new XElement(PkgRel + "Relationship", new XAttribute("Id", "rId1"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/slide"),
                    new XAttribute("Target", "slides/slide1.xml"))));

        var slideRels = new XDocument(new XElement(PkgRel + "Relationships"));

        using var outMs = new MemoryStream();
        using (var zip = new ZipArchive(outMs, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(zip, "[Content_Types].xml", contentTypes);
            WriteEntry(zip, "_rels/.rels", rootRels);
            WriteEntry(zip, "ppt/presentation.xml", presXml);
            WriteEntry(zip, "ppt/_rels/presentation.xml.rels", presRels);
            WriteEntry(zip, "ppt/slides/slide1.xml", slideXml);
            WriteEntry(zip, "ppt/slides/_rels/slide1.xml.rels", slideRels);
        }
        return outMs.ToArray();
    }
}
