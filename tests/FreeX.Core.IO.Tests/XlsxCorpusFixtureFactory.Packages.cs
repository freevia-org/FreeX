using System.IO.Compression;
using System.Xml.Linq;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

internal static partial class XlsxCorpusFixtureFactory
{
    public static MemoryStream CreateKnownGapPackage(string id) => id switch
    {
        "generated-text-boxes-shapes-001" => CreatePackage(("xl/drawings/drawing1.xml", """
            <xdr:wsDr xmlns:xdr="http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing">
              <xdr:twoCellAnchor>
                <xdr:sp/>
              </xdr:twoCellAnchor>
            </xdr:wsDr>
            """)),
        "generated-threaded-comments-001" => CreatePackage(
            ("xl/threadedComments/threadedComment1.xml", "<threadedComments/>"),
            ("xl/persons/person.xml", "<persons/>")),
        "generated-track-changes-001" => CreatePackage(
            ("xl/revisionHeaders/revisionHeader1.xml", "<revisionHeader/>"),
            ("xl/revisions/revisionLog1.xml", "<revisionLog/>")),
        "generated-form-controls-001" => CreatePackage(
            ("xl/activeX/activeX1.xml", "<activeX/>"),
            ("xl/activeX/activeX1.bin", "FreeX generated ActiveX placeholder"),
            ("xl/ctrlProps/ctrlProp1.xml", "<controlProperties/>")),
        "generated-digital-signatures-001" => CreatePackage(
            ("_xmlsignatures/origin.sigs", "FreeX generated signature origin placeholder"),
            ("_xmlsignatures/sig1.xml", "<Signature/>")),
        "generated-custom-ribbon-ui-001" => CreatePackage(("customUI/customUI.xml", """
            <customUI xmlns="http://schemas.microsoft.com/office/2006/01/customui">
              <ribbon/>
            </customUI>
            """)),
        "generated-office-addins-001" => CreatePackage(
            ("xl/webextensions/taskpanes.xml", "<taskpanes/>"),
            ("xl/webextensions/webextension1.xml", "<webextension/>")),
        "generated-live-web-queries-001" => CreatePackage(
            ("xl/webPublishItems.xml", "<webPublishItems/>"),
            ("xl/connections.xml", """
                <connections xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <connection id="1" name="FreeX Web Query" type="4" refreshedVersion="6">
                    <webPr sourceData="1" parsePre="1" consecutive="1" firstRow="1" url="https://example.com/freex-web-query.html"/>
                  </connection>
                </connections>
                """)),
        "generated-sensitivity-labels-001" => CreatePackage(("docProps/custom.xml", """
            <Properties xmlns="http://schemas.openxmlformats.org/officeDocument/2006/custom-properties"
                        xmlns:vt="http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes">
              <property name="MSIP_Label_01234567-89ab-cdef-0123-456789abcdef_Enabled">
                <vt:lpwstr>true</vt:lpwstr>
              </property>
            </Properties>
            """)),
        "generated-smartart-diagrams-001" => CreatePackage(
            ("xl/diagrams/data1.xml", "<dgm:dataModel/>"),
            ("xl/diagrams/layout1.xml", "<dgm:layoutDef/>"),
            ("xl/diagrams/quickStyle1.xml", "<dgm:styleDef/>")),
        "generated-printer-settings-001" => CreatePackage(("xl/printerSettings/printerSettings1.bin", "FreeX generated printer settings placeholder")),
        "generated-calc-chain-001" => CreatePackage(("xl/calcChain.xml", """
            <calcChain xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <c r="A1" i="1"/>
            </calcChain>
            """)),
        "generated-document-properties-001" => CreatePackage(
            ("docProps/core.xml", """
                <cp:coreProperties xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties"
                                   xmlns:dc="http://purl.org/dc/elements/1.1/"
                                   xmlns:dcterms="http://purl.org/dc/terms/"
                                   xmlns:dcmitype="http://purl.org/dc/dcmitype/"
                                   xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
                  <dc:title>FreeX document property corpus</dc:title>
                  <dc:subject>Stable document properties retained</dc:subject>
                  <cp:keywords>xlsx parity</cp:keywords>
                  <cp:lastModifiedBy>FreeX Fixture</cp:lastModifiedBy>
                </cp:coreProperties>
                """),
            ("docProps/app.xml", """
                <Properties xmlns="http://schemas.openxmlformats.org/officeDocument/2006/extended-properties">
                  <Application>Microsoft Excel</Application>
                  <Company>FreeX Test Lab</Company>
                  <Manager>Workbook Fidelity</Manager>
                </Properties>
                """)),
        "generated-header-footer-legacy-drawing-001" => CreatePackage(
            ("xl/drawings/vmlDrawing1.vml", """
                <xml xmlns:v="urn:schemas-microsoft-com:vml"
                     xmlns:o="urn:schemas-microsoft-com:office:office"
                     xmlns:x="urn:schemas-microsoft-com:office:excel">
                  <v:shape id="LH" type="#_x0000_t75">
                    <v:imagedata o:relid="rIdImage1" o:title="Header"/>
                  </v:shape>
                </xml>
                """),
            ("xl/drawings/_rels/vmlDrawing1.vml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdImage1"
                                Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image"
                                Target="../media/headerFooterImage1.png"/>
                </Relationships>
                """),
            ("xl/media/headerFooterImage1.png", "FreeX generated header footer image placeholder")),
        "generated-worksheet-legacy-drawing-001" => CreatePackage(
            ("xl/worksheets/sheet1.xml", """
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                           xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <legacyDrawing r:id="rIdFreeXLegacyDrawing"/>
                </worksheet>
                """),
            ("xl/worksheets/_rels/sheet1.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdFreeXLegacyDrawing"
                                Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/vmlDrawing"
                                Target="../drawings/vmlDrawing1.vml"/>
                </Relationships>
                """),
            ("xl/drawings/vmlDrawing1.vml", """
                <xml xmlns:v="urn:schemas-microsoft-com:vml"
                     xmlns:o="urn:schemas-microsoft-com:office:office"
                     xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"
                     xmlns:x="urn:schemas-microsoft-com:office:excel">
                  <v:shape id="FreeXLegacyDrawingShape" type="#_x0000_t201">
                    <v:imagedata r:id="rIdFreeXVmlImage"/>
                    <x:ClientData ObjectType="Note"/>
                  </v:shape>
                </xml>
                """),
            ("xl/drawings/_rels/vmlDrawing1.vml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdFreeXVmlImage"
                                Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image"
                                Target="../media/vmlImage1.png"/>
                </Relationships>
                """),
            ("xl/media/vmlImage1.png", "FreeX generated VML image placeholder")),
        "generated-workbook-extension-list-001" => CreatePackage(("xl/workbook.xml", """
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <extLst>
                <ext uri="{00112233-4455-6677-8899-AABBCCDDEEFF}">
                  <x15:futureMetadata xmlns:x15="http://schemas.microsoft.com/office/spreadsheetml/2010/11/main"
                                      name="FreeXUnknownWorkbookExtension"/>
                </ext>
              </extLst>
            </workbook>
            """)),
        "generated-workbook-properties-001" => CreatePackage(("xl/workbook.xml", """
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:fx="urn:freex:test">
              <workbookPr date1904="1" defaultThemeVersion="166925">
                <fx:workbookPrNativeChild id="first"/>
                <fx:workbookPrNativeChild id="second"/>
              </workbookPr>
            </workbook>
            """)),
        "generated-workbook-calculation-001" => CreatePackage(("xl/workbook.xml", """
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <calcPr calcMode="manual" iterate="1" iterateCount="50" calcId="191029" refMode="A1" fullPrecision="0" concurrentCalc="1"/>
            </workbook>
            """)),
        "generated-workbook-file-version-001" => CreatePackage(("xl/workbook.xml", """
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <fileVersion appName="xl" lastEdited="7" lowestEdited="7" rupBuild="28129" customVersionFlag="keep"/>
            </workbook>
            """)),
        "generated-workbook-file-recovery-001" => CreatePackage(("xl/workbook.xml", """
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <fileRecoveryPr autoRecover="1" crashSave="1" customRecoveryFlag="keep" repairLoad="0"/>
              <fileRecoveryPr dataExtractLoad="1" repairLoad="1"/>
            </workbook>
            """)),
        "generated-workbook-file-sharing-001" => CreatePackage(("xl/workbook.xml", """
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <fileSharing readOnlyRecommended="1" userName="FreeXTest" revisionsPassword="1234"/>
            </workbook>
            """)),
        "generated-workbook-protection-native-001" => CreatePackage(("xl/workbook.xml", """
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                      xmlns:fx="urn:freex:test">
              <workbookProtection lockStructure="1"
                                  lockWindows="1"
                                  workbookPassword="83AF"
                                  algorithmName="SHA-512"
                                  hashValue="def456"
                                  saltValue="salt456"
                                  spinCount="100000">
                <fx:workbookProtectionNativeChild id="first"/>
                <fx:workbookProtectionNativeChild id="second"/>
              </workbookProtection>
            </workbook>
            """)),
        "generated-workbook-smart-tags-001" => CreatePackage(("xl/workbook.xml", """
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <smartTagPr embed="1" show="all" customSmartTagFlag="keep"/>
              <smartTagTypes customSmartTagTypesFlag="keep">
                <smartTagType namespaceUri="urn:schemas-microsoft-com:office:smarttags" name="place" customSmartTagTypeFlag="keep"/>
              </smartTagTypes>
            </workbook>
            """)),
        "generated-workbook-function-groups-001" => CreatePackage(("xl/workbook.xml", """
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <functionGroups builtInGroupCount="16" customFunctionGroupFlag="keep">
                <functionGroup name="FreeXNativeFunctions" customGroupFlag="keep"/>
              </functionGroups>
            </workbook>
            """)),
        "generated-workbook-views-001" => CreatePackage(("xl/workbook.xml", """
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <bookViews>
                <workbookView visibility="visible" showSheetTabs="0" tabRatio="700" firstSheet="0" activeTab="0"/>
                <workbookView visibility="hidden" minimized="1" showHorizontalScroll="0" showVerticalScroll="0" showSheetTabs="0" tabRatio="700" firstSheet="0" activeTab="0" customWorkbookViewFlag="kept"/>
              </bookViews>
              <customWorkbookViews>
                <customWorkbookView name="FreeXView" guid="{22222222-2222-2222-2222-222222222222}" autoUpdate="0" mergeInterval="0" personalView="0" includePrintSettings="1" includeHiddenRowCol="1"/>
              </customWorkbookViews>
            </workbook>
            """)),
        "generated-workbook-defined-names-native-001" => CreatePackage(("xl/workbook.xml", """
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <definedNames>
                <definedName name="DynamicSalesRange" hidden="1">1+1</definedName>
              </definedNames>
            </workbook>
            """)),
        "generated-workbook-theme-native-schemes-001" => CreatePackage(("xl/theme/theme1.xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <a:theme xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" name="FreeX Native Scheme Theme">
              <a:themeElements>
                <a:clrScheme name="FreeX Native Colors">
                  <a:dk1><a:srgbClr val="010203"/></a:dk1>
                  <a:lt1><a:srgbClr val="FAFBFC"/></a:lt1>
                  <a:dk2><a:srgbClr val="44546A"/></a:dk2>
                  <a:lt2><a:srgbClr val="E7E6E6"/></a:lt2>
                  <a:accent1><a:srgbClr val="0C2238"><a:lumMod val="75000"/></a:srgbClr></a:accent1>
                  <a:accent2><a:srgbClr val="E97132"/></a:accent2>
                  <a:accent3><a:srgbClr val="196B24"/></a:accent3>
                  <a:accent4><a:srgbClr val="0F9ED5"/></a:accent4>
                  <a:accent5><a:srgbClr val="A02B93"/></a:accent5>
                  <a:accent6><a:srgbClr val="4EA72E"/></a:accent6>
                  <a:hlink><a:srgbClr val="0563C1"/></a:hlink>
                  <a:folHlink><a:srgbClr val="954F72"/></a:folHlink>
                </a:clrScheme>
                <a:fontScheme name="FreeX Native Fonts">
                  <a:majorFont>
                    <a:latin typeface="Major Native"/>
                    <a:ea typeface="Major East Asia"/>
                    <a:cs typeface="Major Complex"/>
                    <a:font script="Jpan" typeface="Yu Gothic"/>
                  </a:majorFont>
                  <a:minorFont>
                    <a:latin typeface="Minor Native"/>
                    <a:ea typeface="Minor East Asia"/>
                    <a:cs typeface="Minor Complex"/>
                  </a:minorFont>
                </a:fontScheme>
                <a:fmtScheme name="Effects Test"/>
              </a:themeElements>
            </a:theme>
            """)),
        "generated-stylesheet-native-metadata-001" => CreatePackage(("xl/styles.xml", """
            <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                        xmlns:fx="urn:freex:test">
              <colors>
                <indexedColors>
                  <rgbColor rgb="FF010203"/>
                </indexedColors>
              </colors>
              <dxfs count="1">
                <dxf nativePivotDxf="kept">
                  <fill>
                    <patternFill patternType="solid">
                      <fgColor rgb="FFABCDEF"/>
                    </patternFill>
                  </fill>
                  <fx:pivotStyleDxfNativeChild value="kept"/>
                </dxf>
              </dxfs>
              <tableStyles defaultPivotStyle="PivotStyleMedium9">
                <fx:tableStylesNativeChild value="kept"/>
                <tableStyle name="FreeXNativeTableStyle" pivot="0" table="1" count="1">
                  <tableStyleElement type="wholeTable" dxfId="0"/>
                </tableStyle>
                <tableStyle name="FreeXNativePivotStyle" pivot="1" table="0" count="1">
                  <tableStyleElement type="wholeTable" dxfId="0"/>
                </tableStyle>
              </tableStyles>
              <extLst>
                <ext uri="{FFEEDDCC-7788-6655-4433-22110099AABB}">
                  <FreeXNativeStylesExtension/>
                </ext>
              </extLst>
            </styleSheet>
            """)),
        "generated-worksheet-ignored-errors-001" => CreatePackage(("xl/worksheets/sheet1.xml", """
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <ignoredErrors>
                <ignoredError sqref="A1" numberStoredAsText="1" twoDigitTextYear="1"/>
              </ignoredErrors>
            </worksheet>
            """)),
        "generated-worksheet-cell-watches-001" => CreatePackage(("xl/worksheets/sheet1.xml", """
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <cellWatches nativeContainer="kept">
                <cellWatch r="A1" nativeWatch="kept"/>
              </cellWatches>
            </worksheet>
            """)),
        "generated-worksheet-single-xml-cells-001" => CreatePackage(("xl/worksheets/sheet1.xml", """
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <singleXmlCells nativeSingleXmlCellsAttr="kept">
                <singleXmlCell id="1" r="A1" xmlCellPrId="1" nativeSingleXmlCellAttr="cell-kept"/>
              </singleXmlCells>
            </worksheet>
            """)),
        "generated-worksheet-calculation-properties-001" => CreatePackage(("xl/worksheets/sheet1.xml", """
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheetCalcPr fullCalcOnLoad="1" calcId="999"/>
            </worksheet>
            """)),
        "generated-worksheet-sheet-views-001" => CreatePackage(("xl/worksheets/sheet1.xml", """
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheetViews nativeSheetViewsAttr="kept">
                <sheetView workbookViewId="0" showZeros="0" rightToLeft="1">
                  <pivotSelection pane="topRight"/>
                </sheetView>
              </sheetViews>
            </worksheet>
            """)),
        "generated-worksheet-sheet-format-001" => CreatePackage(("xl/worksheets/sheet1.xml", """
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheetFormatPr baseColWidth="12" zeroHeight="1" thickTop="1" outlineLevelRow="3">
                <nativeSheetFormatChild value="kept"/>
              </sheetFormatPr>
            </worksheet>
            """)),
        "generated-worksheet-page-breaks-001" => CreatePackage(("xl/worksheets/sheet1.xml", """
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <rowBreaks count="1" manualBreakCount="1">
                <brk id="20" max="16383" man="1" pt="1" customAttr="row-native"/>
              </rowBreaks>
              <colBreaks count="1" manualBreakCount="1">
                <brk id="5" max="1048575" man="1" pt="1" customAttr="col-native"/>
              </colBreaks>
            </worksheet>
            """)),
        "generated-worksheet-print-options-001" => CreatePackage(("xl/worksheets/sheet1.xml", """
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                       xmlns:fx="urn:freex:test">
              <printOptions gridLinesSet="1" customAttr="print-native">
                <fx:nativePrintOptionsChild value="kept"/>
              </printOptions>
            </worksheet>
            """)),
        "generated-worksheet-page-setup-native-001" => CreatePackage(("xl/worksheets/sheet1.xml", """
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <pageSetup usePrinterDefaults="1" copies="3" customAttr="page-setup-native">
                <nativePageSetupChild value="kept"/>
              </pageSetup>
            </worksheet>
            """)),
        "generated-worksheet-header-footer-native-001" => CreatePackage(("xl/worksheets/sheet1.xml", """
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <headerFooter nativeHeaderFooterAttr="kept">
                <oddHeader>&amp;LLeft&amp;CCenter&amp;RRight</oddHeader>
                <nativeHeaderFooterChild value="kept"/>
              </headerFooter>
            </worksheet>
            """)),
        "generated-worksheet-dimension-native-001" => CreatePackage(("xl/worksheets/sheet1.xml", """
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <dimension ref="A1" nativeDimensionAttr="kept"/>
            </worksheet>
            """)),
        "generated-worksheet-sheet-properties-001" => CreatePackage(("xl/worksheets/sheet1.xml", """
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                       xmlns:fx="urn:freex:test">
              <sheetPr filterMode="1">
                <pageSetUpPr fitToPage="1" autoPageBreaks="0"/>
                <fx:sheetPrNativeChild id="first"/>
                <fx:sheetPrNativeChild id="second"/>
              </sheetPr>
            </worksheet>
            """)),
        "generated-worksheet-protection-native-001" => CreatePackage(("xl/worksheets/sheet1.xml", """
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                       xmlns:fx="urn:freex:test">
              <sheetProtection sheet="1"
                               algorithmName="SHA-512"
                               hashValue="abc123"
                               saltValue="salt123"
                               spinCount="100000"
                               objects="1"
                               scenarios="1">
                <fx:sheetProtectionNativeChild id="first"/>
                <fx:sheetProtectionNativeChild id="second"/>
              </sheetProtection>
            </worksheet>
            """)),
        "generated-worksheet-protected-ranges-001" => CreatePackage(("xl/worksheets/sheet1.xml", """
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                       xmlns:fx="urn:freex:test">
              <sheetData>
                <row r="1"><c r="A1" t="str"><v>locked</v></c></row>
              </sheetData>
              <protectedRanges>
                <protectedRange name="NativeEditableRange" sqref="B2:C3" password="ABCD" securityDescriptor="D:PAI">
                  <extLst><ext uri="{FREEX-PROTECTED-RANGE-TEST}"/></extLst>
                  <fx:protectedRangeNativeChild id="first"/>
                  <fx:protectedRangeNativeChild id="second"/>
                </protectedRange>
                <protectedRange name="NativeMultiAreaRange" sqref="B2 C3" password="1234"/>
              </protectedRanges>
            </worksheet>
            """)),
        "generated-worksheet-cell-structure-native-001" => CreatePackage(("xl/worksheets/sheet1.xml", """
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                       xmlns:fx="urn:freex:test">
              <cols nativeColsAttr="kept">
                <col min="2" max="2" width="14" customWidth="1" bestFit="1" phonetic="1" customAttr="column-native"/>
              </cols>
              <sheetData nativeSheetDataAttr="kept">
                <row r="1"><c r="A1"><v>3.14</v></c></row>
                <row r="2" thickTop="1" ph="1" customAttr="row-native">
                  <c r="A2" cm="2" vm="1" ph="1" customAttr="cell-native">
                    <f t="array" ref="A2:A2" ca="1" customAttr="formula-native">A1*2</f>
                    <v>6.28</v>
                    <fx:cellNativeChild value="kept"/>
                    <extLst>
                      <ext uri="{FREEX-CELL-EXT}">
                        <fx:cellExt value="cell-extension"/>
                      </ext>
                    </extLst>
                  </c>
                  <fx:rowNativeChild value="kept"/>
                  <extLst>
                    <ext uri="{FREEX-ROW-EXT}">
                      <fx:rowExt value="row-extension"/>
                    </ext>
                  </extLst>
                </row>
                <row r="4"><c r="A4" t="str"><v>merged</v></c></row>
              </sheetData>
              <mergeCells count="1" nativeMergeContainerAttr="kept">
                <mergeCell ref="A4:B5" nativeMergeCellAttr="kept"/>
              </mergeCells>
            </worksheet>
            """)),
        "generated-worksheet-phonetic-properties-001" => CreatePackage(("xl/worksheets/sheet1.xml", """
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <phoneticPr fontId="1" type="fullwidthKatakana" alignment="center" nativeOnly="kept"/>
            </worksheet>
            """)),
        "generated-worksheet-sort-state-001" => CreatePackage(("xl/worksheets/sheet1.xml", """
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <autoFilter ref="A1:B3">
                <filterColumn colId="0">
                  <filters>
                    <filter val="A"/>
                  </filters>
                </filterColumn>
              </autoFilter>
              <sortState ref="A1:A3" caseSensitive="1" sortMethod="stroke" customSortStateFlag="keep">
                <sortCondition ref="A2:A3" descending="1" sortBy="cellColor" customSortConditionFlag="keep"/>
              </sortState>
            </worksheet>
            """)),
        "generated-worksheet-data-consolidation-001" => CreatePackage(("xl/worksheets/sheet1.xml", """
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <dataConsolidate function="sum" leftLabels="1" topLabels="1" link="1" customDataConsolidationFlag="keep">
                <dataRefs count="1">
                  <dataRef ref="A1:B2" sheet="Data" customDataRefFlag="keep"/>
                </dataRefs>
              </dataConsolidate>
            </worksheet>
            """)),
        "generated-worksheet-auto-filter-metadata-001" => CreatePackage(("xl/worksheets/sheet1.xml", """
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheetData>
                <row r="1"><c r="A1" t="str"><v>Category</v></c><c r="B1" t="str"><v>Amount</v></c></row>
                <row r="2"><c r="A2" t="str"><v>A</v></c><c r="B2"><v>10</v></c></row>
                <row r="3"><c r="A3" t="str"><v>B</v></c><c r="B3"><v>20</v></c></row>
              </sheetData>
              <autoFilter ref="A1:B3">
                <filterColumn colId="0">
                  <filters blank="1">
                    <filter val="A"/>
                  </filters>
                </filterColumn>
              </autoFilter>
            </worksheet>
            """)),
        "generated-worksheet-custom-properties-001" => CreatePackage(("xl/worksheets/sheet1.xml", """
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <customProperties>
                <customPr name="FreeXNativeProperty" id="1" unsupportedAttr="kept"/>
              </customProperties>
            </worksheet>
            """)),
        "generated-worksheet-smart-tags-001" => CreatePackage(("xl/worksheets/sheet1.xml", """
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <smartTags>
                <cellSmartTags r="A1">
                  <cellSmartTag type="0" deleted="0">
                    <cellSmartTagPr key="place" val="Seattle" customSmartTagPropertyFlag="keep"/>
                  </cellSmartTag>
                </cellSmartTags>
              </smartTags>
            </worksheet>
            """)),
        "generated-worksheet-scenarios-001" => CreatePackage(("xl/worksheets/sheet1.xml", """
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <scenarios current="0" show="0">
                <scenario name="BestCase" comment="Scenario comment" hidden="1" locked="1" count="1" user="FreeXTest">
                  <inputCells r="A1" val="42"/>
                </scenario>
              </scenarios>
            </worksheet>
            """)),
        "generated-worksheet-custom-sheet-views-001" => CreatePackage(("xl/worksheets/sheet1.xml", """
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <customSheetViews>
                <customSheetView guid="{11111111-1111-1111-1111-111111111111}" scale="120" showGridLines="0" showRowCol="0" state="visible">
                  <pane xSplit="1" ySplit="1" topLeftCell="B2" activePane="bottomRight"/>
                </customSheetView>
              </customSheetViews>
            </worksheet>
            """)),
        "generated-worksheet-extension-list-001" => CreatePackage(("xl/worksheets/sheet1.xml", """
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                       xmlns:x14="http://schemas.microsoft.com/office/spreadsheetml/2009/9/main"
                       xmlns:x15="http://schemas.microsoft.com/office/spreadsheetml/2010/11/main"
                       xmlns:xm="http://schemas.microsoft.com/office/excel/2006/main">
              <sheetData>
                <row r="1"><c r="A1"><v>1</v></c><c r="B1"><v>2</v></c><c r="C1"><v>3</v></c></row>
              </sheetData>
              <extLst>
                <ext uri="{05C60535-1F16-4fd2-B633-F4F36F0B64E0}">
                  <x14:sparklineGroups>
                    <x14:sparklineGroup type="column">
                      <x14:sparklines>
                        <x14:sparkline>
                          <xm:f>Sheet1!A1:C1</xm:f>
                          <xm:sqref>D1</xm:sqref>
                        </x14:sparkline>
                      </x14:sparklines>
                    </x14:sparklineGroup>
                  </x14:sparklineGroups>
                </ext>
                <ext uri="{FFEEDDCC-BBAA-9988-7766-554433221100}">
                  <x15:futureMetadata name="FreeXUnknownWorksheetExtension"/>
                </ext>
              </extLst>
            </worksheet>
            """)),
        "generated-unsupported-sheet-types-001" => CreatePackage(
            ("xl/chartsheets/sheet1.xml", "<chartsheet/>"),
            ("xl/dialogSheets/sheet2.xml", "<dialogsheet/>"),
            ("xl/macroSheets/sheet3.xml", "<macrosheet/>")),
        "generated-unsupported-chart-001" => CreatePackage(("xl/charts/chart1.xml", """
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart">
              <c:chart>
                <c:plotArea>
                  <c:mapChart/>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """)),
        "generated-vba-macros-001" => CreatePackage(("xl/vbaProject.bin", "FreeX generated macro placeholder")),
        "generated-pivots-001" => CreatePackage(
            ("xl/pivotTables/pivotTable1.xml", "<pivotTableDefinition/>"),
            ("xl/pivotCache/pivotCacheDefinition1.xml", "<pivotCacheDefinition/>")),
        "generated-power-query-001" => CreatePackage(
            ("xl/connections.xml", "<connections/>"),
            ("xl/queries/query1.xml", "<query/>"),
            ("xl/queryTables/queryTable1.xml", """
                <queryTable xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                            name="FreeXQueryTable"
                            connectionId="1"
                            autoFormatId="16"
                            applyNumberFormats="0"
                            applyBorderFormats="0"
                            applyFontFormats="0"
                            applyPatternFormats="0"
                            applyAlignmentFormats="0"
                            applyWidthHeightFormats="0"/>
                """),
            ("xl/worksheets/sheet1.xml", """
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                           xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheetData/>
                  <queryTableParts count="1">
                    <queryTablePart r:id="rIdFreeXQueryTable"/>
                  </queryTableParts>
                </worksheet>
                """),
            ("xl/worksheets/_rels/sheet1.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdFreeXQueryTable"
                                Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/queryTable"
                                Target="../queryTables/queryTable1.xml"/>
                </Relationships>
                """)),
        "generated-data-model-001" => CreatePackage(
            ("xl/model/item.data", "FreeX generated data model placeholder"),
            ("xl/model/item.xml", "<dataModel/>")),
        "generated-linked-data-types-001" => CreatePackage(
            ("xl/richData/rdrichvalue.xml", "<rvData/>"),
            ("xl/richData/rdRichValueStructure.xml", "<rvStructures/>"),
            ("xl/richData/rdRichValueTypes.xml", "<rvTypes/>"),
            ("xl/richData/richValueRel.xml", "<richValueRels/>"),
            ("xl/richData/_rels/rdrichvalue.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdRichValueStructure"
                                Type="http://schemas.microsoft.com/office/2017/06/relationships/rdRichValueStructure"
                                Target="rdRichValueStructure.xml"/>
                </Relationships>
                """)),
        "generated-slicers-001" => CreatePackage(
            ("xl/slicers/slicer1.xml", "<slicer/>"),
            ("xl/slicerCaches/slicerCache1.xml", "<slicerCacheDefinition/>")),
        "generated-timelines-001" => CreatePackage(
            ("xl/timelines/timeline1.xml", "<timeline/>"),
            ("xl/timelineCaches/timelineCache1.xml", "<timelineCacheDefinition/>")),
        "generated-external-links-001" => CreatePackage(
            ("xl/workbook.xml", """
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                          xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets>
                    <sheet name="Sheet1" sheetId="1" r:id="rId1"/>
                  </sheets>
                  <externalReferences>
                    <externalReference r:id="rIdExternalLink1"/>
                  </externalReferences>
                </workbook>
                """),
            ("xl/_rels/workbook.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1"
                                Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"
                                Target="worksheets/sheet1.xml"/>
                  <Relationship Id="rIdExternalLink1"
                                Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/externalLink"
                                Target="externalLinks/externalLink1.xml"/>
                </Relationships>
                """),
            ("xl/externalLinks/externalLink1.xml", """
                <externalLink xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                              xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <externalBook r:id="rIdExternalBook1">
                    <sheetNames>
                      <sheetName val="ExternalSheet"/>
                    </sheetNames>
                  </externalBook>
                </externalLink>
                """),
            ("xl/externalLinks/_rels/externalLink1.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdExternalBook1"
                                Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/externalLinkPath"
                                Target="file:///C:/FreeX/ExternalWorkbook.xlsx"
                                TargetMode="External"/>
                </Relationships>
                """)),
        "generated-embedded-objects-001" => CreatePackage(("xl/embeddings/oleObject1.bin", "FreeX generated OLE placeholder")),
        "generated-custom-xml-001" => CreatePackage(
            ("customXml/item1.xml", "<freexGeneratedCustomXml/>"),
            ("customXml/itemProps1.xml", """
                <ds:datastoreItem ds:itemID="{11111111-2222-3333-4444-555555555555}"
                                  xmlns:ds="http://schemas.openxmlformats.org/officeDocument/2006/customXml"/>
                """),
            ("customXml/_rels/item1.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdCustomXmlProps1"
                                Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXmlProps"
                                Target="itemProps1.xml"/>
                </Relationships>
                """)),
        "generated-custom-docprops-001" => CreatePackage(("docProps/custom.xml", """
            <Properties xmlns="http://schemas.openxmlformats.org/officeDocument/2006/custom-properties"
                        xmlns:vt="http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes">
              <property fmtid="{D5CDD505-2E9C-101B-9397-08002B2CF9AE}" pid="2" name="Department">
                <vt:lpwstr>Compliance</vt:lpwstr>
              </property>
              <property fmtid="{D5CDD505-2E9C-101B-9397-08002B2CF9AE}" pid="3" name="MSIP_Label_01234567-89ab-cdef-0123-456789abcdef_Enabled">
                <vt:lpwstr>true</vt:lpwstr>
              </property>
            </Properties>
            """)),
        "generated-cf-retention-package-003" => CreatePackage(("xl/worksheets/sheet1.xml", """
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheetData>
                <row r="1"><c r="A1"><v>10</v></c></row>
                <row r="2"><c r="A2"><v>20</v></c></row>
                <row r="3"><c r="A3"><v>30</v></c></row>
              </sheetData>
              <conditionalFormatting sqref="A1:A3">
                <cfRule type="cellIs" priority="1" operator="greaterThan" dxfId="0"><formula>25</formula></cfRule>
                <cfRule type="cellIs" priority="2" operator="lessThan" dxfId="1"><formula>15</formula></cfRule>
                <cfRule type="cellIs" priority="3" operator="equal" dxfId="0"><formula>20</formula></cfRule>
                <cfRule type="formula" priority="4" dxfId="1"><formula>MOD(ROW(),2)=0</formula></cfRule>
                <cfRule type="top10" priority="5" dxfId="0" rank="1"/>
                <cfRule type="top10" priority="6" dxfId="1" rank="1" bottom="1"/>
                <cfRule type="duplicateValues" priority="7" dxfId="0"/>
                <cfRule type="uniqueValues" priority="8" dxfId="1"/>
                <cfRule type="containsText" priority="9" operator="containsText" text="A" dxfId="0"><formula>NOT(ISERROR(SEARCH("A",A1)))</formula></cfRule>
                <cfRule type="timePeriod" priority="10" timePeriod="last7Days" dxfId="1"/>
                <cfRule type="aboveAverage" priority="11" dxfId="0"/>
                <cfRule type="aboveAverage" priority="12" dxfId="1" aboveAverage="0"/>
                <cfRule type="colorScale" priority="13"><colorScale><cfvo type="min"/><cfvo type="max"/><color rgb="FFF8696B"/><color rgb="FF63BE7B"/></colorScale></cfRule>
                <cfRule type="dataBar" priority="14"><dataBar><cfvo type="min"/><cfvo type="max"/><color rgb="FF638EC6"/></dataBar></cfRule>
                <cfRule type="iconSet" priority="15"><iconSet iconSet="3Arrows"/></cfRule>
                <cfRule type="cellIs" priority="16" operator="notEqual" dxfId="0"><formula>0</formula></cfRule>
              </conditionalFormatting>
            </worksheet>
            """)),
        "generated-chart-series-count-003" => CreatePackage(("xl/charts/chart1.xml", """
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart">
              <c:chart>
                <c:plotArea>
                  <c:barChart>
                    <c:barDir val="col"/>
                    <c:grouping val="clustered"/>
                    <c:ser><c:idx val="0"/><c:order val="0"/><c:tx><c:strRef><c:f>Sheet1!$B$1</c:f></c:strRef></c:tx></c:ser>
                    <c:ser><c:idx val="1"/><c:order val="1"/><c:tx><c:strRef><c:f>Sheet1!$C$1</c:f></c:strRef></c:tx></c:ser>
                    <c:ser><c:idx val="2"/><c:order val="2"/><c:tx><c:strRef><c:f>Sheet1!$D$1</c:f></c:strRef></c:tx></c:ser>
                    <c:ser><c:idx val="3"/><c:order val="3"/><c:tx><c:strRef><c:f>Sheet1!$E$1</c:f></c:strRef></c:tx></c:ser>
                    <c:ser><c:idx val="4"/><c:order val="4"/><c:tx><c:strRef><c:f>Sheet1!$F$1</c:f></c:strRef></c:tx></c:ser>
                  </c:barChart>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """)),
        "generated-dv-count-package-003" => CreatePackage(("xl/worksheets/sheet1.xml", """
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheetData><row r="1"><c r="A1" t="str"><v>Data</v></c></row></sheetData>
              <dataValidations count="10">
                <dataValidation type="list" sqref="B2:B10"><formula1>"A,B,C"</formula1></dataValidation>
                <dataValidation type="whole" operator="between" sqref="C2:C10"><formula1>1</formula1><formula2>100</formula2></dataValidation>
                <dataValidation type="decimal" operator="greaterThan" sqref="D2:D10"><formula1>0</formula1></dataValidation>
                <dataValidation type="date" operator="greaterThanOrEqual" sqref="E2:E10"><formula1>DATE(2026,1,1)</formula1></dataValidation>
                <dataValidation type="time" operator="between" sqref="F2:F10"><formula1>TIME(8,0,0)</formula1><formula2>TIME(18,0,0)</formula2></dataValidation>
                <dataValidation type="textLength" operator="lessThanOrEqual" sqref="G2:G10"><formula1>50</formula1></dataValidation>
                <dataValidation type="custom" sqref="H2:H10"><formula1>LEN(H2)>0</formula1></dataValidation>
                <dataValidation type="list" sqref="I2:I10"><formula1>"Yes,No"</formula1></dataValidation>
                <dataValidation type="whole" operator="greaterThan" sqref="J2:J10"><formula1>0</formula1></dataValidation>
                <dataValidation type="decimal" operator="lessThan" sqref="K2:K10"><formula1>1000</formula1></dataValidation>
              </dataValidations>
            </worksheet>
            """)),
        "generated-table-ref-formulas-package-003" => CreatePackage(("xl/worksheets/sheet1.xml", """
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheetData>
                <row r="1">
                  <c r="A1" t="str"><v>Item</v></c>
                  <c r="B1" t="str"><v>Price</v></c>
                  <c r="C1" t="str"><v>Qty</v></c>
                  <c r="D1" t="str"><v>Total</v></c>
                </row>
                <row r="2">
                  <c r="A2" t="str"><v>Alpha</v></c>
                  <c r="B2"><v>10</v></c>
                  <c r="C2"><v>5</v></c>
                  <c r="D2"><f>SalesTable[@Price]*SalesTable[@Qty]</f><v>50</v></c>
                </row>
                <row r="3">
                  <c r="A3" t="str"><v>Beta</v></c>
                  <c r="B3"><v>20</v></c>
                  <c r="C3"><v>3</v></c>
                  <c r="D3"><f>SalesTable[@Price]*SalesTable[@Qty]</f><v>60</v></c>
                </row>
              </sheetData>
            </worksheet>
            """)),
        "generated-cross-sheet-range-package-003" => CreatePackage(("xl/worksheets/sheet1.xml", """
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheetData>
                <row r="1"><c r="A1" t="str"><v>Region</v></c><c r="B1" t="str"><v>Sales</v></c></row>
                <row r="2"><c r="A2" t="str"><v>North</v></c><c r="B2"><v>100</v></c></row>
                <row r="3"><c r="A3" t="str"><v>South</v></c><c r="B3"><v>125</v></c></row>
                <row r="5"><c r="A5" t="str"><v>Summary</v></c></row>
                <row r="6"><c r="A6"><f>SUMIF(A2:A3,"North",B2:B3)</f><v>100</v></c></row>
                <row r="7"><c r="A7"><f>SUMIF(A2:A3,"South",B2:B3)</f><v>125</v></c></row>
              </sheetData>
            </worksheet>
            """)),
        "generated-named-range-count-package-003" => CreatePackage(("xl/workbook.xml", """
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <definedNames>
                <definedName name="Range01">Sheet1!$A$1:$A$3</definedName>
                <definedName name="Range02">Sheet1!$B$1:$B$3</definedName>
                <definedName name="Range03">Sheet1!$C$1:$C$3</definedName>
                <definedName name="Range04">Sheet1!$D$1:$D$3</definedName>
                <definedName name="Range05">Sheet1!$E$1:$E$3</definedName>
                <definedName name="Range06">Sheet1!$F$1:$F$3</definedName>
                <definedName name="Range07">Sheet1!$G$1:$G$3</definedName>
                <definedName name="Range08">Sheet1!$H$1:$H$3</definedName>
                <definedName name="Range09">Sheet1!$I$1:$I$3</definedName>
                <definedName name="Range10">Sheet1!$J$1:$J$3</definedName>
                <definedName name="Range11">Sheet1!$K$1:$K$3</definedName>
                <definedName name="Range12">Sheet1!$L$1:$L$3</definedName>
              </definedNames>
            </workbook>
            """)),
        _ => throw new ArgumentOutOfRangeException(nameof(id), id, "No generated known-gap XLSX package fixture exists for this id.")
    };

    public static MemoryStream CreateKnownGapRetentionPackage(string id)
    {
        using var knownGapPackage = CreateKnownGapPackage(id);
        var workbook = NewWorkbook($"retention-{id}");
        var sheet = workbook.AddSheet("Sheet1");
        Set(sheet, "A1", new TextValue(id));
        Set(sheet, "B1", new NumberValue(1));

        var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;

        using (var sourceArchive = new ZipArchive(knownGapPackage, ZipArchiveMode.Read, leaveOpen: true))
        using (var targetArchive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            var mergedSourceParts = new List<string>();
            foreach (var sourceEntry in sourceArchive.Entries)
            {
                if (ShouldMergeThroughFixup(id, sourceEntry.FullName))
                    continue;

                targetArchive.GetEntry(sourceEntry.FullName)?.Delete();
                var targetEntry = targetArchive.CreateEntry(sourceEntry.FullName);
                using var sourceStream = sourceEntry.Open();
                using var targetStream = targetEntry.Open();
                sourceStream.CopyTo(targetStream);
                mergedSourceParts.Add(sourceEntry.FullName.Replace('\\', '/'));
            }

            EnsureKnownGapContentTypeOverrides(targetArchive, mergedSourceParts);
            ApplyPackageFixups(id, targetArchive);
        }

        stream.Position = 0;
        return stream;
    }

    private static bool ShouldMergeThroughFixup(string id, string packagePart) =>
        (string.Equals(id, "generated-external-links-001", StringComparison.OrdinalIgnoreCase) &&
         (string.Equals(packagePart, "xl/workbook.xml", StringComparison.OrdinalIgnoreCase) ||
          string.Equals(packagePart, "xl/_rels/workbook.xml.rels", StringComparison.OrdinalIgnoreCase))) ||
        (string.Equals(id, "generated-workbook-extension-list-001", StringComparison.OrdinalIgnoreCase) &&
         string.Equals(packagePart, "xl/workbook.xml", StringComparison.OrdinalIgnoreCase)) ||
        (string.Equals(id, "generated-worksheet-legacy-drawing-001", StringComparison.OrdinalIgnoreCase) &&
         (string.Equals(packagePart, "xl/worksheets/sheet1.xml", StringComparison.OrdinalIgnoreCase) ||
          string.Equals(packagePart, "xl/worksheets/_rels/sheet1.xml.rels", StringComparison.OrdinalIgnoreCase))) ||
        (string.Equals(id, "generated-workbook-properties-001", StringComparison.OrdinalIgnoreCase) &&
         string.Equals(packagePart, "xl/workbook.xml", StringComparison.OrdinalIgnoreCase)) ||
        (string.Equals(id, "generated-workbook-calculation-001", StringComparison.OrdinalIgnoreCase) &&
         string.Equals(packagePart, "xl/workbook.xml", StringComparison.OrdinalIgnoreCase)) ||
        (string.Equals(id, "generated-workbook-file-version-001", StringComparison.OrdinalIgnoreCase) &&
         string.Equals(packagePart, "xl/workbook.xml", StringComparison.OrdinalIgnoreCase)) ||
        (string.Equals(id, "generated-workbook-file-recovery-001", StringComparison.OrdinalIgnoreCase) &&
         string.Equals(packagePart, "xl/workbook.xml", StringComparison.OrdinalIgnoreCase)) ||
        (string.Equals(id, "generated-workbook-file-sharing-001", StringComparison.OrdinalIgnoreCase) &&
         string.Equals(packagePart, "xl/workbook.xml", StringComparison.OrdinalIgnoreCase)) ||
        (string.Equals(id, "generated-workbook-protection-native-001", StringComparison.OrdinalIgnoreCase) &&
         string.Equals(packagePart, "xl/workbook.xml", StringComparison.OrdinalIgnoreCase)) ||
        (string.Equals(id, "generated-workbook-smart-tags-001", StringComparison.OrdinalIgnoreCase) &&
         string.Equals(packagePart, "xl/workbook.xml", StringComparison.OrdinalIgnoreCase)) ||
        (string.Equals(id, "generated-workbook-function-groups-001", StringComparison.OrdinalIgnoreCase) &&
         string.Equals(packagePart, "xl/workbook.xml", StringComparison.OrdinalIgnoreCase)) ||
        (string.Equals(id, "generated-workbook-views-001", StringComparison.OrdinalIgnoreCase) &&
         string.Equals(packagePart, "xl/workbook.xml", StringComparison.OrdinalIgnoreCase)) ||
        (string.Equals(id, "generated-workbook-defined-names-native-001", StringComparison.OrdinalIgnoreCase) &&
         string.Equals(packagePart, "xl/workbook.xml", StringComparison.OrdinalIgnoreCase)) ||
        (string.Equals(id, "generated-stylesheet-native-metadata-001", StringComparison.OrdinalIgnoreCase) &&
         string.Equals(packagePart, "xl/styles.xml", StringComparison.OrdinalIgnoreCase)) ||
        (string.Equals(id, "generated-worksheet-ignored-errors-001", StringComparison.OrdinalIgnoreCase) &&
         string.Equals(packagePart, "xl/worksheets/sheet1.xml", StringComparison.OrdinalIgnoreCase)) ||
        (string.Equals(id, "generated-worksheet-cell-watches-001", StringComparison.OrdinalIgnoreCase) &&
         string.Equals(packagePart, "xl/worksheets/sheet1.xml", StringComparison.OrdinalIgnoreCase)) ||
        (string.Equals(id, "generated-worksheet-single-xml-cells-001", StringComparison.OrdinalIgnoreCase) &&
         string.Equals(packagePart, "xl/worksheets/sheet1.xml", StringComparison.OrdinalIgnoreCase)) ||
        (string.Equals(id, "generated-worksheet-calculation-properties-001", StringComparison.OrdinalIgnoreCase) &&
         string.Equals(packagePart, "xl/worksheets/sheet1.xml", StringComparison.OrdinalIgnoreCase)) ||
        (string.Equals(id, "generated-worksheet-sheet-views-001", StringComparison.OrdinalIgnoreCase) &&
         string.Equals(packagePart, "xl/worksheets/sheet1.xml", StringComparison.OrdinalIgnoreCase)) ||
        (string.Equals(id, "generated-worksheet-sheet-format-001", StringComparison.OrdinalIgnoreCase) &&
         string.Equals(packagePart, "xl/worksheets/sheet1.xml", StringComparison.OrdinalIgnoreCase)) ||
        (string.Equals(id, "generated-worksheet-page-breaks-001", StringComparison.OrdinalIgnoreCase) &&
         string.Equals(packagePart, "xl/worksheets/sheet1.xml", StringComparison.OrdinalIgnoreCase)) ||
        (string.Equals(id, "generated-worksheet-print-options-001", StringComparison.OrdinalIgnoreCase) &&
         string.Equals(packagePart, "xl/worksheets/sheet1.xml", StringComparison.OrdinalIgnoreCase)) ||
        (string.Equals(id, "generated-worksheet-page-setup-native-001", StringComparison.OrdinalIgnoreCase) &&
         string.Equals(packagePart, "xl/worksheets/sheet1.xml", StringComparison.OrdinalIgnoreCase)) ||
        (string.Equals(id, "generated-worksheet-header-footer-native-001", StringComparison.OrdinalIgnoreCase) &&
         string.Equals(packagePart, "xl/worksheets/sheet1.xml", StringComparison.OrdinalIgnoreCase)) ||
        (string.Equals(id, "generated-worksheet-dimension-native-001", StringComparison.OrdinalIgnoreCase) &&
         string.Equals(packagePart, "xl/worksheets/sheet1.xml", StringComparison.OrdinalIgnoreCase)) ||
        (string.Equals(id, "generated-worksheet-sheet-properties-001", StringComparison.OrdinalIgnoreCase) &&
         string.Equals(packagePart, "xl/worksheets/sheet1.xml", StringComparison.OrdinalIgnoreCase)) ||
        (string.Equals(id, "generated-worksheet-protection-native-001", StringComparison.OrdinalIgnoreCase) &&
         string.Equals(packagePart, "xl/worksheets/sheet1.xml", StringComparison.OrdinalIgnoreCase)) ||
        (string.Equals(id, "generated-worksheet-protected-ranges-001", StringComparison.OrdinalIgnoreCase) &&
         string.Equals(packagePart, "xl/worksheets/sheet1.xml", StringComparison.OrdinalIgnoreCase)) ||
        (string.Equals(id, "generated-worksheet-cell-structure-native-001", StringComparison.OrdinalIgnoreCase) &&
         string.Equals(packagePart, "xl/worksheets/sheet1.xml", StringComparison.OrdinalIgnoreCase)) ||
        (string.Equals(id, "generated-worksheet-phonetic-properties-001", StringComparison.OrdinalIgnoreCase) &&
         string.Equals(packagePart, "xl/worksheets/sheet1.xml", StringComparison.OrdinalIgnoreCase)) ||
        (string.Equals(id, "generated-worksheet-sort-state-001", StringComparison.OrdinalIgnoreCase) &&
         string.Equals(packagePart, "xl/worksheets/sheet1.xml", StringComparison.OrdinalIgnoreCase)) ||
        (string.Equals(id, "generated-worksheet-data-consolidation-001", StringComparison.OrdinalIgnoreCase) &&
         string.Equals(packagePart, "xl/worksheets/sheet1.xml", StringComparison.OrdinalIgnoreCase)) ||
        (string.Equals(id, "generated-worksheet-auto-filter-metadata-001", StringComparison.OrdinalIgnoreCase) &&
         string.Equals(packagePart, "xl/worksheets/sheet1.xml", StringComparison.OrdinalIgnoreCase)) ||
        (string.Equals(id, "generated-worksheet-custom-properties-001", StringComparison.OrdinalIgnoreCase) &&
         string.Equals(packagePart, "xl/worksheets/sheet1.xml", StringComparison.OrdinalIgnoreCase)) ||
        (string.Equals(id, "generated-worksheet-smart-tags-001", StringComparison.OrdinalIgnoreCase) &&
         string.Equals(packagePart, "xl/worksheets/sheet1.xml", StringComparison.OrdinalIgnoreCase)) ||
        (string.Equals(id, "generated-worksheet-scenarios-001", StringComparison.OrdinalIgnoreCase) &&
         string.Equals(packagePart, "xl/worksheets/sheet1.xml", StringComparison.OrdinalIgnoreCase)) ||
        (string.Equals(id, "generated-worksheet-custom-sheet-views-001", StringComparison.OrdinalIgnoreCase) &&
         string.Equals(packagePart, "xl/worksheets/sheet1.xml", StringComparison.OrdinalIgnoreCase)) ||
        (string.Equals(id, "generated-worksheet-extension-list-001", StringComparison.OrdinalIgnoreCase) &&
         string.Equals(packagePart, "xl/worksheets/sheet1.xml", StringComparison.OrdinalIgnoreCase)) ||
        (string.Equals(id, "generated-table-ref-formulas-package-003", StringComparison.OrdinalIgnoreCase) &&
         string.Equals(packagePart, "xl/worksheets/sheet1.xml", StringComparison.OrdinalIgnoreCase)) ||
        (string.Equals(id, "generated-cross-sheet-range-package-003", StringComparison.OrdinalIgnoreCase) &&
         string.Equals(packagePart, "xl/worksheets/sheet1.xml", StringComparison.OrdinalIgnoreCase)) ||
        (string.Equals(id, "generated-named-range-count-package-003", StringComparison.OrdinalIgnoreCase) &&
         string.Equals(packagePart, "xl/workbook.xml", StringComparison.OrdinalIgnoreCase));

    private static void EnsureKnownGapContentTypeOverrides(ZipArchive archive, IReadOnlyCollection<string> partNames)
    {
        var contentTypesEntry = archive.GetEntry("[Content_Types].xml");
        if (contentTypesEntry is null)
            return;

        XDocument contentTypes;
        using (var stream = contentTypesEntry.Open())
            contentTypes = XDocument.Load(stream);

        foreach (var partName in partNames.Where(part => !part.EndsWith(".rels", StringComparison.OrdinalIgnoreCase)))
        {
            var contentType = ContentTypeForKnownGapPart(partName);
            if (!string.IsNullOrWhiteSpace(contentType))
                EnsureContentTypeOverride(contentTypes, "/" + partName.TrimStart('/'), contentType);
        }

        ReplacePackageXml(archive, "[Content_Types].xml", contentTypes);
    }

    private static string ContentTypeForKnownGapPart(string partName)
    {
        var path = partName.Replace('\\', '/');
        return path switch
        {
            "xl/drawings/drawing1.xml" => "application/vnd.openxmlformats-officedocument.drawing+xml",
            "xl/threadedComments/threadedComment1.xml" => "application/vnd.ms-excel.threadedcomments+xml",
            "xl/persons/person.xml" => "application/vnd.ms-excel.person+xml",
            "xl/revisionHeaders/revisionHeader1.xml" => "application/vnd.openxmlformats-officedocument.spreadsheetml.revisionHeaders+xml",
            "xl/revisions/revisionLog1.xml" => "application/vnd.openxmlformats-officedocument.spreadsheetml.revisionLog+xml",
            "xl/activeX/activeX1.xml" => "application/vnd.ms-office.activeX+xml",
            "xl/activeX/activeX1.bin" => "application/vnd.ms-office.activeX",
            "xl/ctrlProps/ctrlProp1.xml" => "application/vnd.ms-excel.controlproperties+xml",
            "_xmlsignatures/origin.sigs" => "application/vnd.openxmlformats-package.digital-signature-origin",
            "_xmlsignatures/sig1.xml" => "application/vnd.openxmlformats-package.digital-signature-xmlsignature+xml",
            "customUI/customUI.xml" => "application/xml",
            "xl/webextensions/taskpanes.xml" => "application/vnd.ms-office.webextensiontaskpanes+xml",
            "xl/webextensions/webextension1.xml" => "application/vnd.ms-office.webextension+xml",
            "xl/webPublishItems.xml" => "application/vnd.openxmlformats-officedocument.spreadsheetml.webPublishItems+xml",
            "docProps/core.xml" => "application/vnd.openxmlformats-package.core-properties+xml",
            "docProps/app.xml" => "application/vnd.openxmlformats-officedocument.extended-properties+xml",
            "docProps/custom.xml" => "application/vnd.openxmlformats-officedocument.custom-properties+xml",
            "xl/drawings/vmlDrawing1.vml" => "application/vnd.openxmlformats-officedocument.vmlDrawing",
            "xl/media/headerFooterImage1.png" => "image/png",
            "xl/media/vmlImage1.png" => "image/png",
            "xl/diagrams/data1.xml" => "application/vnd.openxmlformats-officedocument.drawingml.diagramData+xml",
            "xl/diagrams/layout1.xml" => "application/vnd.openxmlformats-officedocument.drawingml.diagramLayout+xml",
            "xl/diagrams/quickStyle1.xml" => "application/vnd.openxmlformats-officedocument.drawingml.diagramStyle+xml",
            "xl/printerSettings/printerSettings1.bin" => "application/vnd.openxmlformats-officedocument.spreadsheetml.printerSettings",
            "xl/calcChain.xml" => "application/vnd.openxmlformats-officedocument.spreadsheetml.calcChain+xml",
            "xl/chartsheets/sheet1.xml" => "application/vnd.openxmlformats-officedocument.spreadsheetml.chartsheet+xml",
            "xl/dialogSheets/sheet2.xml" => "application/vnd.openxmlformats-officedocument.spreadsheetml.dialogsheet+xml",
            "xl/macroSheets/sheet3.xml" => "application/vnd.ms-excel.macrosheet+xml",
            "xl/charts/chart1.xml" => "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
            "xl/theme/theme1.xml" => "application/vnd.openxmlformats-officedocument.theme+xml",
            "xl/vbaProject.bin" => "application/vnd.ms-office.vbaProject",
            "xl/pivotTables/pivotTable1.xml" => "application/vnd.openxmlformats-officedocument.spreadsheetml.pivotTable+xml",
            "xl/pivotCache/pivotCacheDefinition1.xml" => "application/vnd.openxmlformats-officedocument.spreadsheetml.pivotCacheDefinition+xml",
            "xl/connections.xml" => "application/vnd.openxmlformats-officedocument.spreadsheetml.connections+xml",
            "xl/queries/query1.xml" => "application/vnd.ms-excel.queryTable+xml",
            "xl/queryTables/queryTable1.xml" => "application/vnd.openxmlformats-officedocument.spreadsheetml.queryTable+xml",
            "xl/model/item.xml" => "application/xml",
            "xl/model/item.data" => "application/vnd.ms-excel.model",
            "xl/richData/rdrichvalue.xml" => "application/vnd.ms-excel.rdrichvalue+xml",
            "xl/richData/rdRichValueStructure.xml" => "application/vnd.ms-excel.rdRichValueStructure+xml",
            "xl/richData/rdRichValueTypes.xml" => "application/vnd.ms-excel.rdrichvaluetypes+xml",
            "xl/richData/richValueRel.xml" => "application/vnd.ms-excel.richvaluerel+xml",
            "xl/slicers/slicer1.xml" => "application/vnd.ms-excel.slicer+xml",
            "xl/slicerCaches/slicerCache1.xml" => "application/vnd.ms-excel.slicerCache+xml",
            "xl/timelines/timeline1.xml" => "application/vnd.ms-excel.timeline+xml",
            "xl/timelineCaches/timelineCache1.xml" => "application/vnd.ms-excel.timelineCache+xml",
            "xl/externalLinks/externalLink1.xml" => "application/vnd.openxmlformats-officedocument.spreadsheetml.externalLink+xml",
            "xl/embeddings/oleObject1.bin" => "application/vnd.openxmlformats-officedocument.oleObject",
            "customXml/item1.xml" => "application/xml",
            "customXml/itemProps1.xml" => "application/vnd.openxmlformats-officedocument.customXmlProperties+xml",
            _ => path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ? "application/xml" : ""
        };
    }

}
