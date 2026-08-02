using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression coverage for the <c>&lt;controls&gt;</c> shape where the block is a DIRECT child of the
/// worksheet root and each individual <c>&lt;control&gt;</c> is wrapped in its own
/// <c>mc:AlternateContent</c>/<c>mc:Choice</c> (a real, valid x14 forward-compatibility shape Excel
/// writes). <c>XlsxWorksheetOleControlNormalizer</c> previously assumed <c>&lt;control&gt;</c> was
/// always a direct child of <c>&lt;controls&gt;</c>: it stripped every AlternateContent-wrapped
/// control as an "unknown child", then deleted the resulting empty <c>&lt;controls&gt;</c> block —
/// silently destroying every legacy form control on the sheet on the very next save. The parallel
/// relationship rebind silently no-op'd for the same reason. Existing form-control tests only cover
/// the OUTER-AlternateContent-wrapped shape, which the normalizer never descends into at all.
/// </summary>
public sealed class XlsxDirectControlsAlternateContentNormalizationTests
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace McNs = "http://schemas.openxmlformats.org/markup-compatibility/2006";
    private static readonly XNamespace FcNs = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";

    private const string SourceLinkedCell = "$I$4";
    private const string ShiftedLinkedCell = "$I$5";

    [Fact]
    public void Save_DirectControlsBlockWithPerControlAlternateContent_PreservesControlXml()
    {
        using var package = BuildPackageWithDirectControlsBlock();

        using var saved = SaveRoundTrip(package);
        using var archive = new ZipArchive(saved, ZipArchiveMode.Read);
        var worksheetXml = LoadWorksheet(archive);

        var controls = worksheetXml.Root!.Element(WorksheetNs + "controls");
        controls.Should().NotBeNull(
            "the <controls> block must survive a save; stripping AlternateContent-wrapped controls " +
            "empties it and deletes every form control on the sheet");
        EnumerateControls(controls!).Should().HaveCount(1,
            "the AlternateContent-wrapped <control> must survive normalization");
    }

    [Fact]
    public void Save_DirectControlsBlockWithPerControlAlternateContent_ReloadsFormControl()
    {
        using var package = BuildPackageWithDirectControlsBlock();
        var loaded = new XlsxFileAdapter().Load(package);
        loaded.Sheets[0].FormControls.Should().HaveCount(1, "sanity: the source package has one control");

        using var saved = SaveRoundTrip(package);
        var reloaded = new XlsxFileAdapter().Load(saved);

        var control = reloaded.Sheets[0].FormControls.Should().ContainSingle(
            "the checkbox must still be there after a save").Subject;
        control.LinkedCell.Should().Be(ShiftedLinkedCell);
    }

    [Fact]
    public void Save_DirectControlsBlockWithPerControlAlternateContent_RebindsControlPropertiesRelationship()
    {
        using var package = BuildPackageWithDirectControlsBlock();

        using var saved = SaveRoundTrip(package);
        using var archive = new ZipArchive(saved, ZipArchiveMode.Read);
        var worksheetPath = GetWorksheetEntry(archive).FullName;
        var worksheetXml = LoadWorksheet(archive);

        var control = EnumerateControls(worksheetXml.Root!.Element(WorksheetNs + "controls")!).Single();
        var relationshipId = control.Attribute(RelNs + "id")?.Value;
        relationshipId.Should().NotBeNullOrWhiteSpace(
            "the rebind must reach controls nested in mc:AlternateContent, not silently no-op");

        var relsPath = "xl/worksheets/_rels/" + Path.GetFileName(worksheetPath) + ".rels";
        var relsXml = XDocument.Load(archive.GetEntry(relsPath)!.Open());
        var relationship = relsXml.Root!
            .Elements(PackageRelNs + "Relationship")
            .Single(candidate => candidate.Attribute("Id")!.Value == relationshipId);
        relationship.Attribute("Type")!.Value.Should().EndWith("/ctrlProp",
            "the rebound relationship must point at the control-properties part");
    }

    [Fact]
    public void Save_DirectControlsBlockWithUnbindableSecondControl_LeavesNoHollowAlternateContentWrapper()
    {
        // The second control's r:id resolves to nothing, so the rebind drops it. Its now-empty
        // mc:Choice/mc:AlternateContent wrapper must go with it rather than lingering as hollow
        // markup inside a <controls> block the first control keeps alive.
        using var package = BuildPackageWithDirectControlsBlock(includeUnbindableSecondControl: true);

        using var saved = SaveRoundTrip(package);
        using var archive = new ZipArchive(saved, ZipArchiveMode.Read);
        var worksheetXml = LoadWorksheet(archive);

        var controls = worksheetXml.Root!.Element(WorksheetNs + "controls");
        controls.Should().NotBeNull("the bindable control must keep the block alive");
        EnumerateControls(controls!).Should().ContainSingle(
            "only the control whose ctrlProp relationship resolves survives")
            .Which.Attribute("shapeId")!.Value.Should().Be("1025");
        controls!.Descendants(McNs + "AlternateContent")
            .Should().AllSatisfy(wrapper => wrapper.Descendants(WorksheetNs + "control").Should().NotBeEmpty(),
                "no mc:AlternateContent may survive without the control it wrapped");
    }

    /// <summary>
    /// Loads the package, edits the control (the same shape of edit
    /// <c>RowColumnShiftHelpers.AddressState.ShiftFormControls</c> makes), and saves. The edit
    /// matters: <see cref="XlsxWorksheetFormControlPreserver"/> only re-runs
    /// <c>XlsxWorksheetOleControlNormalizer.NormalizePackage</c> when it actually changes something,
    /// so an untouched workbook byte-copies its controls block forward and never reaches the
    /// normalizer at all.
    /// </summary>
    private static MemoryStream SaveRoundTrip(MemoryStream package)
    {
        package.Position = 0;
        var workbook = new XlsxFileAdapter().Load(package);
        workbook.Sheets[0].FormControls.First().LinkedCell = ShiftedLinkedCell;
        var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;
        return saved;
    }

    private static IReadOnlyList<XElement> EnumerateControls(XElement element)
    {
        var matches = new List<XElement>();
        Walk(element);
        return matches;

        void Walk(XElement current)
        {
            foreach (var child in current.Elements())
            {
                if (child.Name == WorksheetNs + "control")
                    matches.Add(child);
                else
                    Walk(child);
            }
        }
    }

    private static ZipArchiveEntry GetWorksheetEntry(ZipArchive archive) =>
        archive.Entries.Single(entry =>
            entry.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase) &&
            entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));

    private static XDocument LoadWorksheet(ZipArchive archive)
    {
        using var read = GetWorksheetEntry(archive).Open();
        return XDocument.Load(read);
    }

    /// <summary>
    /// Builds a package whose <c>&lt;controls&gt;</c> block is a direct worksheet-root child (NOT
    /// itself wrapped in an outer <c>mc:AlternateContent</c>) while each individual
    /// <c>&lt;control&gt;</c> IS wrapped — the exact shape that used to be destroyed on save.
    /// <paramref name="includeUnbindableSecondControl"/> adds a second wrapped control whose
    /// <c>r:id</c> resolves to no relationship, so the rebind must drop it (and its wrapper).
    /// </summary>
    private static MemoryStream BuildPackageWithDirectControlsBlock(bool includeUnbindableSecondControl = false)
    {
        var workbook = new Workbook("T");
        var sheet = workbook.AddSheet("S");
        sheet.SetCell(new CellAddress(sheet.Id, 4, 9), new BoolValue(false)); // I4 linked cell

        var baseStream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, baseStream);
        baseStream.Position = 0;

        var result = new MemoryStream();
        baseStream.CopyTo(result);
        result.Position = 0;

        using (var archive = new ZipArchive(result, ZipArchiveMode.Update, leaveOpen: true))
        {
            var worksheetPath = GetWorksheetEntry(archive).FullName;
            var worksheetXml = LoadWorksheet(archive);
            var root = worksheetXml.Root!;
            root.SetAttributeValue(XNamespace.Xmlns + "r", RelNs.NamespaceName);
            root.SetAttributeValue(XNamespace.Xmlns + "mc", McNs.NamespaceName);
            root.Add(XElement.Parse(
                """
                <controls xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                          xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
                          xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"
                          xmlns:xdr="http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing">
                  <mc:AlternateContent>
                    <mc:Choice Requires="x14">
                      <control shapeId="1025" r:id="rIdCtrl" name="Check Box 1">
                        <controlPr defaultSize="0" autoFill="0" autoLine="0" autoPict="0">
                          <anchor>
                            <from><xdr:col>1</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>4</xdr:row><xdr:rowOff>0</xdr:rowOff></from>
                            <to><xdr:col>2</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>5</xdr:row><xdr:rowOff>0</xdr:rowOff></to>
                          </anchor>
                        </controlPr>
                      </control>
                    </mc:Choice>
                  </mc:AlternateContent>
                </controls>
                """));
            if (includeUnbindableSecondControl)
            {
                root.Element(WorksheetNs + "controls")!.Add(XElement.Parse(
                    """
                    <mc:AlternateContent xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
                                         xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                                         xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"
                                         xmlns:xdr="http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing">
                      <mc:Choice Requires="x14">
                        <control shapeId="1026" r:id="rIdMissing" name="Check Box 2">
                          <controlPr defaultSize="0" autoFill="0" autoLine="0" autoPict="0">
                            <anchor>
                              <from><xdr:col>4</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>4</xdr:row><xdr:rowOff>0</xdr:rowOff></from>
                              <to><xdr:col>5</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>5</xdr:row><xdr:rowOff>0</xdr:rowOff></to>
                            </anchor>
                          </controlPr>
                        </control>
                      </mc:Choice>
                    </mc:AlternateContent>
                    """));
            }

            ReplaceEntry(archive, worksheetPath, worksheetXml);

            var ctrlPropXml = new XDocument(new XElement(FcNs + "formControlPr",
                new XAttribute("objectType", "CheckBox"),
                new XAttribute("checked", "Unchecked"),
                new XAttribute("lockText", "1"),
                new XAttribute("noThreeD", "1"),
                new XAttribute("fmlaLink", SourceLinkedCell)));
            ReplaceEntry(archive, "xl/ctrlProps/ctrlProp1.xml", ctrlPropXml);

            AddCtrlPropRelationshipAndContentTypes(archive, worksheetPath);
        }

        result.Position = 0;
        return result;
    }

    private static void AddCtrlPropRelationshipAndContentTypes(ZipArchive archive, string worksheetPath)
    {
        var relsPath = "xl/worksheets/_rels/" + Path.GetFileName(worksheetPath) + ".rels";
        XDocument relsXml;
        var relsEntry = archive.GetEntry(relsPath);
        if (relsEntry is not null)
        {
            using var read = relsEntry.Open();
            relsXml = XDocument.Load(read);
        }
        else
        {
            relsXml = new XDocument(new XElement(PackageRelNs + "Relationships"));
        }

        relsXml.Root!.Add(
            new XElement(PackageRelNs + "Relationship",
                new XAttribute("Id", "rIdCtrl"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/ctrlProp"),
                new XAttribute("Target", "../ctrlProps/ctrlProp1.xml")));
        ReplaceEntry(archive, relsPath, relsXml);

        XNamespace ctNs = "http://schemas.openxmlformats.org/package/2006/content-types";
        var ctEntry = archive.GetEntry("[Content_Types].xml")!;
        XDocument ctXml;
        using (var read = ctEntry.Open())
            ctXml = XDocument.Load(read);
        ctXml.Root!.Add(new XElement(ctNs + "Override",
            new XAttribute("PartName", "/xl/ctrlProps/ctrlProp1.xml"),
            new XAttribute("ContentType", "application/vnd.ms-excel.controlproperties+xml")));
        ReplaceEntry(archive, "[Content_Types].xml", ctXml);
    }

    private static void ReplaceEntry(ZipArchive archive, string path, XDocument xml)
    {
        archive.GetEntry(path)?.Delete();
        var entry = archive.CreateEntry(path);
        using var stream = entry.Open();
        xml.Save(stream, SaveOptions.DisableFormatting);
    }
}
