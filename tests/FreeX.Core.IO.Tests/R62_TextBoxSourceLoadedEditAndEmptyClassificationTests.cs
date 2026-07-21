using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R62-io-drawing-textbox-6-1: rotating or recoloring a loaded (source-passthrough) text box must
/// not be silently discarded on save -- RotateTextBoxCommand/SetTextBoxColorsCommand must clear
/// <see cref="TextBoxModel.IsSourceLoaded"/> (mirroring DrawingShapeFormatCommands' identical fix for
/// shapes) so the full writer emits the edited object instead of relying on the stale preserved XML.
///
/// R62-io-drawing-textbox-6-3: an emptied (text-deleted) text box must still be classified as a
/// TextBox on load, not silently reclassified as a plain Rectangle shape, because Excel authors
/// empty text boxes with the same cNvSpPr txBox="1" marker plus a rect prstGeom as populated ones.
/// </summary>
public sealed class R62_TextBoxSourceLoadedEditAndEmptyClassificationTests
{
    private static void PrepareLoadedWorkbookForEdit(Workbook workbook) =>
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

    [Fact]
    public void RotateAndRecolorLoadedTextBox_ClearIsSourceLoadedSoWriterEmitsTheEdit()
    {
        // R62-io-drawing-textbox-6-1: mirrors Round51CommandsBucketTests'
        // SetDrawingShapeColorsCommand_OnSourceLoadedShape_ClearsIsSourceLoadedSoWriterEmitsIt for the
        // identical class of bug on TextBoxModel. IsSupportedTextBox (XlsxWorksheetDrawingObjectWriter)
        // requires !IsSourceLoaded before it will emit a text box at all, and the source-geometry
        // rewriter never patches rotation/fill/outline into the preserved passthrough XML -- so without
        // clearing the flag, a rotate or recolor edit on a loaded text box has nowhere to go and is
        // silently dropped on save.
        var workbook = new Workbook();
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var textBox = new TextBoxModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 1),
            Text = "Warning",
            HasFill = true,
            FillColor = new CellColor(0xFF, 0x00, 0x00), // red, as if authored in Excel
            IsSourceLoaded = true,
        };
        sheet.TextBoxes.Add(textBox);

        var rotateCommand = new RotateTextBoxCommand(sheet.Id, textBox.Id, 45);
        rotateCommand.Apply(ctx).Success.Should().BeTrue();

        textBox.RotationDegrees.Should().Be(45);
        textBox.IsSourceLoaded.Should().BeFalse(
            "RotateTextBoxCommand must clear IsSourceLoaded so the xlsx writer emits the edited rotation " +
            "instead of skipping the text box and relying on the stale preserved source XML");

        var recolorCommand = new SetTextBoxColorsCommand(
            sheet.Id,
            textBox.Id,
            fillColor: new CellColor(0x00, 0xFF, 0x00),
            outlineColor: null);
        // Re-mark as source-loaded to independently verify SetTextBoxColorsCommand also clears the flag
        // (not just RotateTextBoxCommand, which already cleared it above).
        textBox.IsSourceLoaded = true;
        recolorCommand.Apply(ctx).Success.Should().BeTrue();

        textBox.FillColor.Should().Be(new CellColor(0x00, 0xFF, 0x00));
        textBox.IsSourceLoaded.Should().BeFalse(
            "SetTextBoxColorsCommand must clear IsSourceLoaded so the xlsx writer emits the edited fill " +
            "instead of skipping the text box and relying on the stale preserved source XML");
    }

    [Fact]
    public void RevertRotateTextBoxCommand_RestoresIsSourceLoadedAndOriginalRotation()
    {
        // Sibling no-regression guard: undo of the rotate edit must put the text box back into its
        // original source-passthrough state (IsSourceLoaded=true, original rotation), not leave it
        // permanently forced through the full-writer path after a revert.
        var sourceBytes = CreateTextBoxSourcePackage(text: "Warning");
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet = workbook.GetSheetAt(0);
        var textBox = sheet.TextBoxes.Should().ContainSingle().Which;

        var ctx = new TestCommandContext(workbook);
        var rotateCommand = new RotateTextBoxCommand(sheet.Id, textBox.Id, 45);
        rotateCommand.Apply(ctx).Success.Should().BeTrue();
        textBox.IsSourceLoaded.Should().BeFalse();

        rotateCommand.Revert(ctx);

        textBox.RotationDegrees.Should().Be(0);
        textBox.IsSourceLoaded.Should().BeTrue("reverting the edit should restore the original source-passthrough state");
    }

    [Fact]
    public void LoadEmptiedTextBox_IsClassifiedAsTextBoxNotRectangleShape()
    {
        var sourceBytes = CreateTextBoxSourcePackage(text: "");
        var adapter = new XlsxFileAdapter();

        using var source = new MemoryStream(sourceBytes, writable: false);
        var sheet = adapter.Load(source).GetSheetAt(0);

        // The bug: ReadSpElement only routed to sheet.TextBoxes when isTxBox AND text non-empty, so an
        // emptied text box (still marked cNvSpPr txBox="1") fell through to the generic prstGeom="rect"
        // handling and was permanently reclassified as a plain Rectangle DrawingShape.
        sheet.TextBoxes.Should().ContainSingle().Which.Text.Should().BeEmpty();
        sheet.DrawingShapes.Should().BeEmpty();
    }

    [Fact]
    public void LoadPlainRectangleShapeWithNoText_IsStillClassifiedAsShapeNotTextBox()
    {
        // Sibling no-regression guard: a genuine (non-txBox) Rectangle shape with no text must still
        // be classified as a DrawingShape -- the loosened txBox gate must not sweep in shapes that were
        // never authored with cNvSpPr txBox="1".
        var sourceBytes = CreatePlainRectangleShapeSourcePackage();
        var adapter = new XlsxFileAdapter();

        using var source = new MemoryStream(sourceBytes, writable: false);
        var sheet = adapter.Load(source).GetSheetAt(0);

        sheet.TextBoxes.Should().BeEmpty();
        sheet.DrawingShapes.Should().ContainSingle().Which.Kind.Should().Be(DrawingShapeKind.Rectangle);
    }

    [Fact]
    public void Save_LoadedEmptiedTextBoxWithUnrelatedCellEdit_PatchesSourcePackageAndPreservesTextBoxIdentity()
    {
        var sourceBytes = CreateTextBoxSourcePackage(text: "");
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet = workbook.GetSheetAt(0);
        sheet.TextBoxes.Should().ContainSingle();
        sheet.SetCell(new CellAddress(sheet.Id, 4, 4), new TextValue("unrelated patched"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch, adapter.LastSaveDiagnostics.Reason);

        using var reloadStream = new MemoryStream(savedBytes, writable: false);
        var reloadedSheet = adapter.Load(reloadStream).GetSheetAt(0);
        reloadedSheet.GetCell(4, 4)!.Value.Should().Be(new TextValue("unrelated patched"));
        reloadedSheet.TextBoxes.Should().ContainSingle().Which.Text.Should().BeEmpty();
        reloadedSheet.DrawingShapes.Should().BeEmpty();
    }

    private static byte[] CreateTextBoxSourcePackage(string text)
    {
        var txBodyRun = string.IsNullOrEmpty(text)
            ? "<a:p><a:endParaRPr/></a:p>"
            : $"<a:p><a:r><a:t>{text}</a:t></a:r></a:p>";

        using var package = XlsxPackageTestFixtures.CreatePackage(
            (
                "[Content_Types].xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                  <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
                  <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                  <Override PartName="/xl/drawings/drawing1.xml" ContentType="application/vnd.openxmlformats-officedocument.drawing+xml"/>
                </Types>
                """),
            (
                "_rels/.rels",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
                </Relationships>
                """),
            (
                "xl/workbook.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets>
                    <sheet name="Data" sheetId="1" r:id="rId1"/>
                  </sheets>
                </workbook>
                """),
            (
                "xl/_rels/workbook.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
                  <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
                </Relationships>
                """),
            (
                "xl/styles.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <fonts count="1"><font><sz val="11"/><color theme="1"/><name val="Calibri"/><family val="2"/><scheme val="minor"/></font></fonts>
                  <fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills>
                  <borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders>
                  <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
                  <cellXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/></cellXfs>
                  <cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles>
                  <dxfs count="0"/>
                  <tableStyles count="0" defaultTableStyle="TableStyleMedium2" defaultPivotStyle="PivotStyleLight16"/>
                </styleSheet>
                """),
            (
                "xl/worksheets/sheet1.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <dimension ref="A1:D4"/>
                  <sheetData>
                    <row r="1"><c r="A1" t="inlineStr"><is><t>Header</t></is></c></row>
                    <row r="4"><c r="D4" t="inlineStr"><is><t>outside</t></is></c></row>
                  </sheetData>
                  <drawing r:id="rId1"/>
                </worksheet>
                """),
            (
                "xl/worksheets/_rels/sheet1.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing" Target="../drawings/drawing1.xml"/>
                </Relationships>
                """),
            (
                "xl/drawings/drawing1.xml",
                $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <xdr:wsDr xmlns:xdr="http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
                  <xdr:twoCellAnchor>
                    <xdr:from><xdr:col>0</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>0</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from>
                    <xdr:to><xdr:col>2</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>3</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:to>
                    <xdr:sp>
                      <xdr:nvSpPr>
                        <xdr:cNvPr id="2" name="Warning Box" title="Warning title" descr="Warning alt"/>
                        <xdr:cNvSpPr txBox="1"/>
                      </xdr:nvSpPr>
                      <xdr:spPr>
                        <a:prstGeom prst="rect"><a:avLst/></a:prstGeom>
                        <a:solidFill><a:srgbClr val="FF0000"/></a:solidFill>
                      </xdr:spPr>
                      <xdr:txBody><a:bodyPr/><a:lstStyle/>{txBodyRun}</xdr:txBody>
                    </xdr:sp>
                    <xdr:clientData/>
                  </xdr:twoCellAnchor>
                </xdr:wsDr>
                """),
            (
                "xl/drawings/_rels/drawing1.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"/>
                """));

        return package.ToArray();
    }

    private static byte[] CreatePlainRectangleShapeSourcePackage()
    {
        using var package = XlsxPackageTestFixtures.CreatePackage(
            (
                "[Content_Types].xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                  <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
                  <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                  <Override PartName="/xl/drawings/drawing1.xml" ContentType="application/vnd.openxmlformats-officedocument.drawing+xml"/>
                </Types>
                """),
            (
                "_rels/.rels",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
                </Relationships>
                """),
            (
                "xl/workbook.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets>
                    <sheet name="Data" sheetId="1" r:id="rId1"/>
                  </sheets>
                </workbook>
                """),
            (
                "xl/_rels/workbook.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
                  <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
                </Relationships>
                """),
            (
                "xl/styles.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <fonts count="1"><font><sz val="11"/><color theme="1"/><name val="Calibri"/><family val="2"/><scheme val="minor"/></font></fonts>
                  <fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills>
                  <borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders>
                  <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
                  <cellXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/></cellXfs>
                  <cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles>
                  <dxfs count="0"/>
                  <tableStyles count="0" defaultTableStyle="TableStyleMedium2" defaultPivotStyle="PivotStyleLight16"/>
                </styleSheet>
                """),
            (
                "xl/worksheets/sheet1.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <dimension ref="A1:D4"/>
                  <sheetData>
                    <row r="1"><c r="A1" t="inlineStr"><is><t>Header</t></is></c></row>
                  </sheetData>
                  <drawing r:id="rId1"/>
                </worksheet>
                """),
            (
                "xl/worksheets/_rels/sheet1.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing" Target="../drawings/drawing1.xml"/>
                </Relationships>
                """),
            (
                "xl/drawings/drawing1.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <xdr:wsDr xmlns:xdr="http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
                  <xdr:twoCellAnchor>
                    <xdr:from><xdr:col>0</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>0</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from>
                    <xdr:to><xdr:col>2</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>3</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:to>
                    <xdr:sp>
                      <xdr:nvSpPr>
                        <xdr:cNvPr id="2" name="Approval Shape"/>
                        <xdr:cNvSpPr/>
                      </xdr:nvSpPr>
                      <xdr:spPr><a:prstGeom prst="rect"><a:avLst/></a:prstGeom></xdr:spPr>
                    </xdr:sp>
                    <xdr:clientData/>
                  </xdr:twoCellAnchor>
                </xdr:wsDr>
                """),
            (
                "xl/drawings/_rels/drawing1.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"/>
                """));

        return package.ToArray();
    }
}
