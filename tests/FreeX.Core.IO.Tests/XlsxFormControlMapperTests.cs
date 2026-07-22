using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public class XlsxFormControlMapperTests
{
    private static readonly XNamespace FormControlNs =
        "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

    [Fact]
    public void ReadControlProperties_CheckBoxChecked_ParsesTypeCheckedAndLinkedCell()
    {
        var formControlPr = XElement.Parse(
            """
            <formControlPr xmlns="http://schemas.microsoft.com/office/spreadsheetml/2009/9/main"
                           objectType="CheckBox" checked="Checked" fmlaLink="I4"/>
            """);

        var control = XlsxFormControlMapper.ReadControlProperties(formControlPr);

        control.Should().NotBeNull();
        control!.Kind.Should().Be(FormControlKind.CheckBox);
        control.IsChecked.Should().BeTrue();
        control.LinkedCell.Should().Be("I4");
    }

    [Fact]
    public void ReadControlProperties_ScrollBar_ParsesMinMaxValueIncrementPageAndLinkedCell()
    {
        var formControlPr = XElement.Parse(
            """
            <formControlPr xmlns="http://schemas.microsoft.com/office/spreadsheetml/2009/9/main"
                           objectType="Scroll" fmlaLink="'Calc (2)'!$D$14" max="12" min="1" page="3" val="12"/>
            """);

        var control = XlsxFormControlMapper.ReadControlProperties(formControlPr);

        control.Should().NotBeNull();
        control!.Kind.Should().Be(FormControlKind.ScrollBar);
        control.Min.Should().Be(1);
        control.Max.Should().Be(12);
        control.Value.Should().Be(12);
        control.PageChange.Should().Be(3);
        control.LinkedCell.Should().Be("'Calc (2)'!$D$14");
    }

    [Fact]
    public void ReadControlProperties_OptionButtonUnchecked_ParsesKindAndUncheckedState()
    {
        var formControlPr = XElement.Parse(
            """
            <formControlPr xmlns="http://schemas.microsoft.com/office/spreadsheetml/2009/9/main"
                           objectType="Radio"/>
            """);

        var control = XlsxFormControlMapper.ReadControlProperties(formControlPr);

        control.Should().NotBeNull();
        control!.Kind.Should().Be(FormControlKind.OptionButton);
        control.IsChecked.Should().BeFalse();
        // No "checked" attribute at all (the common Excel default) must not be conflated with an
        // explicit tri-state "Unchecked": Value stays null rather than being coerced to 0.
        control.Value.Should().BeNull();
    }

    [Fact]
    public void ReadControlProperties_CheckBoxMixed_PreservesTriStateDistinctFromUnchecked()
    {
        // R38-io-vml-form-controls-2-1: a real Excel tri-state (indeterminate) checkbox value,
        // ctrlProp checked="Mixed". IsChecked (a plain bool) cannot represent the third state and
        // stays false, matching its existing documented Checked-only semantics, but Value must now
        // carry Excel's ST_Checked numeric encoding (2) so "Mixed" is distinguishable from an
        // explicit "Unchecked" (which would read as Value == 0) instead of being silently
        // collapsed into the same false/no-signal result.
        var formControlPr = XElement.Parse(
            """
            <formControlPr xmlns="http://schemas.microsoft.com/office/spreadsheetml/2009/9/main"
                           objectType="CheckBox" checked="Mixed" fmlaLink="I4"/>
            """);

        var control = XlsxFormControlMapper.ReadControlProperties(formControlPr);

        control.Should().NotBeNull();
        control!.Kind.Should().Be(FormControlKind.CheckBox);
        control.IsChecked.Should().BeFalse();
        control.Value.Should().Be(2);
        control.LinkedCell.Should().Be("I4");
    }

    [Fact]
    public void ReadControlProperties_OptionButtonMixed_PreservesTriStateDistinctFromUnchecked()
    {
        // Sibling of the CheckBox case: option buttons share the same objectType-independent
        // "checked" tri-state handling, so Mixed must round-trip identically for Radio/Option.
        var formControlPr = XElement.Parse(
            """
            <formControlPr xmlns="http://schemas.microsoft.com/office/spreadsheetml/2009/9/main"
                           objectType="Radio" checked="Mixed"/>
            """);

        var control = XlsxFormControlMapper.ReadControlProperties(formControlPr);

        control.Should().NotBeNull();
        control!.Kind.Should().Be(FormControlKind.OptionButton);
        control.IsChecked.Should().BeFalse();
        control.Value.Should().Be(2);
    }

    [Fact]
    public void ReadControlProperties_CheckBoxExplicitlyUnchecked_ValueIsZeroNotNull()
    {
        // No-regression sibling: an explicit checked="Unchecked" (as opposed to the attribute
        // being entirely absent) is a distinct, legal ST_Checked value and must read as Value == 0,
        // not null -- otherwise "explicitly Unchecked" and "Mixed" (2) would remain the only two
        // distinguishable states, defeating the point of preserving the tri-state signal.
        var formControlPr = XElement.Parse(
            """
            <formControlPr xmlns="http://schemas.microsoft.com/office/spreadsheetml/2009/9/main"
                           objectType="CheckBox" checked="Unchecked"/>
            """);

        var control = XlsxFormControlMapper.ReadControlProperties(formControlPr);

        control.Should().NotBeNull();
        control!.IsChecked.Should().BeFalse();
        control.Value.Should().Be(0);
    }

    [Fact]
    public void ReadControlProperties_CheckBoxChecked_ValueIsOne()
    {
        // No-regression sibling: the plain "Checked" case must also carry the matching numeric
        // encoding (1) through Value now that CheckBox/OptionButton populate it, on top of the
        // pre-existing IsChecked bool (asserted separately above).
        var formControlPr = XElement.Parse(
            """
            <formControlPr xmlns="http://schemas.microsoft.com/office/spreadsheetml/2009/9/main"
                           objectType="CheckBox" checked="Checked"/>
            """);

        var control = XlsxFormControlMapper.ReadControlProperties(formControlPr);

        control.Should().NotBeNull();
        control!.IsChecked.Should().BeTrue();
        control.Value.Should().Be(1);
    }

    [Fact]
    public void ParseVmlAnchor_ConvertsPixelOffsetsToEmuZeroBasedRange()
    {
        // VML x:Anchor: leftCol,leftColOff,topRow,topRowOff,rightCol,rightColOff,bottomRow,bottomRowOff
        // (cells are 0-based; offsets are PIXELS). Mirrors the todo-sheet Check Box 1: 5,18,8,18,5,50,10,2.
        var anchor = XlsxFormControlMapper.ParseVmlAnchor("5,18,8,18,5,50,10,2");

        anchor.Should().NotBeNull();
        anchor!.From.Column.Should().Be(5);
        anchor.From.Row.Should().Be(8);
        anchor.From.ColumnOffsetEmu.Should().Be(18 * 9525);
        anchor.From.RowOffsetEmu.Should().Be(18 * 9525);
        anchor.To.Column.Should().Be(5);
        anchor.To.Row.Should().Be(10);
        anchor.To.ColumnOffsetEmu.Should().Be(50 * 9525);
        anchor.To.RowOffsetEmu.Should().Be(2 * 9525);
    }

    [Fact]
    public void ParseVmlAnchor_ReturnsNullForMalformedInput()
    {
        XlsxFormControlMapper.ParseVmlAnchor("not,enough").Should().BeNull();
        XlsxFormControlMapper.ParseVmlAnchor(null).Should().BeNull();
        XlsxFormControlMapper.ParseVmlAnchor("a,b,c,d,e,f,g,h").Should().BeNull();
    }

    [Fact]
    public void ReadAnchorWithOffsets_ControlPrAnchor_PreservesEmuSubCellOffsets()
    {
        // The worksheet controlPr/anchor carries from/to col/row plus xdr:colOff/rowOff in EMU.
        var anchor = XElement.Parse(
            """
            <anchor xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                    xmlns:xdr="http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing"
                    moveWithCells="1">
              <from><xdr:col>5</xdr:col><xdr:colOff>171450</xdr:colOff><xdr:row>8</xdr:row><xdr:rowOff>171450</xdr:rowOff></from>
              <to><xdr:col>5</xdr:col><xdr:colOff>476250</xdr:colOff><xdr:row>10</xdr:row><xdr:rowOff>19050</xdr:rowOff></to>
            </anchor>
            """);

        var offsets = XlsxFormControlMapper.ReadAnchorOffsets(anchor);

        offsets.Should().NotBeNull();
        offsets!.From.Column.Should().Be(5);
        offsets.From.Row.Should().Be(8);
        offsets.From.ColumnOffsetEmu.Should().Be(171450);
        offsets.From.RowOffsetEmu.Should().Be(171450);
        offsets.To.Column.Should().Be(5);
        offsets.To.Row.Should().Be(10);
        offsets.To.ColumnOffsetEmu.Should().Be(476250);
        offsets.To.RowOffsetEmu.Should().Be(19050);
    }

    [Fact]
    public void ReadAnchorOffsets_ReturnsNullWhenOffsetsAbsent()
    {
        // No colOff/rowOff -> offsets default to 0 but still represent the cell range; absent col/row -> null.
        var anchor = XElement.Parse(
            """
            <anchor xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                    xmlns:xdr="http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing">
              <from><xdr:col>2</xdr:col><xdr:row>3</xdr:row></from>
            </anchor>
            """);

        XlsxFormControlMapper.ReadAnchorOffsets(anchor).Should().BeNull();
    }

    [Fact]
    public void ReadVmlCaption_ReturnsTextboxText()
    {
        // Legacy form-control caption lives in the VML shape's <v:textbox> content (one or more <div> lines).
        var shape = XElement.Parse(
            """
            <v:shape xmlns:v="urn:schemas-microsoft-com:vml">
              <v:textbox><div style="text-align:left">Include weekends</div></v:textbox>
            </v:shape>
            """);

        XlsxFormControlMapper.ReadVmlCaption(shape).Should().Be("Include weekends");
    }

    [Fact]
    public void ReadVmlCaption_ReturnsNullForEmptyOrMissingTextbox()
    {
        // The todo-sheet checkboxes have an empty <div> — Excel shows no label, so caption is null
        // (the renderer must NOT fall back to the shape Name).
        var emptyTextbox = XElement.Parse(
            """
            <v:shape xmlns:v="urn:schemas-microsoft-com:vml">
              <v:textbox style="mso-direction-alt:auto"><div style="text-align:left;direction:ltr"></div></v:textbox>
            </v:shape>
            """);
        XlsxFormControlMapper.ReadVmlCaption(emptyTextbox).Should().BeNull();

        var noTextbox = XElement.Parse("""<v:shape xmlns:v="urn:schemas-microsoft-com:vml" />""");
        XlsxFormControlMapper.ReadVmlCaption(noTextbox).Should().BeNull();
    }

    [Fact]
    public void ReadControlProperties_DropDown_ParsesSelectionAndListFillRange()
    {
        var formControlPr = XElement.Parse(
            """
            <formControlPr xmlns="http://schemas.microsoft.com/office/spreadsheetml/2009/9/main"
                           objectType="Drop" fmlaLink="$M$5" fmlaRange="high.choices" sel="2" val="0"/>
            """);

        var control = XlsxFormControlMapper.ReadControlProperties(formControlPr);

        control.Should().NotBeNull();
        control!.Kind.Should().Be(FormControlKind.DropDown);
        control.SelectedIndex.Should().Be(2);
        control.ListFillRange.Should().Be("high.choices");
        control.LinkedCell.Should().Be("$M$5");
    }

    [Fact]
    public void ReadWorksheet_ControlPrFallbackWithNoCtrlProp_RecoversLinkedCellAndListFillRange()
    {
        // R69-io-form-controls-6-1: when the control's ctrlProp rel is absent/broken, the worksheet
        // controlPr fallback must read the CT_ControlPr attribute names ("linkedCell"/"listFillRange"),
        // not the ctrlProps-only formControlPr names ("fmlaLink"/"fmlaRange").
        var worksheetXml = XDocument.Parse(
            $$"""
            <worksheet xmlns="{{WorksheetNs}}" xmlns:r="{{RelNs}}">
              <controls>
                <control shapeId="1025" name="List Box 1">
                  <controlPr defaultSize="0" linkedCell="$B$2" listFillRange="Sheet1!$A$1:$A$3" />
                </control>
              </controls>
            </worksheet>
            """);

        using var archiveStream = new MemoryStream();
        using (var archive = new ZipArchive(archiveStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            // No ctrlProps part and no worksheet _rels part: the control's r:id is absent, so the
            // ctrlProp lookup must short-circuit and fall through to the controlPr attributes.
        }
        archiveStream.Position = 0;
        using var readArchive = new ZipArchive(archiveStream, ZipArchiveMode.Read);

        var controls = XlsxFormControlMapper.ReadWorksheet(readArchive, "xl/worksheets/sheet1.xml", worksheetXml);

        controls.Should().ContainSingle();
        controls[0].LinkedCell.Should().Be("$B$2");
        controls[0].ListFillRange.Should().Be("Sheet1!$A$1:$A$3");
    }

    [Fact]
    public void ReadWorksheet_ControlWithCtrlProp_UsesCtrlPropValuesNotControlPrFallback()
    {
        // No-regression sibling: when a valid ctrlProp part IS resolved, its fmlaLink/fmlaRange
        // values must win -- the controlPr linkedCell/listFillRange fallback (now read for the
        // no-ctrlProp case above) must not override an already-populated value.
        const string worksheetPath = "xl/worksheets/sheet1.xml";
        var worksheetXml = XDocument.Parse(
            $$"""
            <worksheet xmlns="{{WorksheetNs}}" xmlns:r="{{RelNs}}">
              <controls>
                <control shapeId="1026" r:id="rIdCtrl" name="List Box 2">
                  <controlPr defaultSize="0" linkedCell="$Z$99" listFillRange="Sheet1!$Z$1:$Z$9" />
                </control>
              </controls>
            </worksheet>
            """);

        using var archiveStream = new MemoryStream();
        using (var archive = new ZipArchive(archiveStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var ctrlPropXml = new XDocument(new XElement(FormControlNs + "formControlPr",
                new XAttribute("objectType", "List"),
                new XAttribute("fmlaLink", "$B$2"),
                new XAttribute("fmlaRange", "Sheet1!$A$1:$A$3")));
            var ctrlPropEntry = archive.CreateEntry("xl/ctrlProps/ctrlProp1.xml");
            using (var stream = ctrlPropEntry.Open())
                ctrlPropXml.Save(stream, SaveOptions.DisableFormatting);

            var relsXml = new XDocument(
                new XElement(PackageRelNs + "Relationships",
                    new XElement(PackageRelNs + "Relationship",
                        new XAttribute("Id", "rIdCtrl"),
                        new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/ctrlProp"),
                        new XAttribute("Target", "../ctrlProps/ctrlProp1.xml"))));
            var relsEntry = archive.CreateEntry("xl/worksheets/_rels/sheet1.xml.rels");
            using (var stream = relsEntry.Open())
                relsXml.Save(stream, SaveOptions.DisableFormatting);
        }
        archiveStream.Position = 0;
        using var readArchive = new ZipArchive(archiveStream, ZipArchiveMode.Read);

        var controls = XlsxFormControlMapper.ReadWorksheet(readArchive, worksheetPath, worksheetXml);

        controls.Should().ContainSingle();
        controls[0].LinkedCell.Should().Be("$B$2");
        controls[0].ListFillRange.Should().Be("Sheet1!$A$1:$A$3");
    }
}
