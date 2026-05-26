using System.IO.Compression;
using System.Xml.Linq;
using Freexcel.Core.IO;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO.Tests;

internal static class XlsxCorpusFixtureFactory
{
    private static readonly HashSet<string> SupportedIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "generated-grid-basic-001",
        "generated-formulas-001",
        "generated-cross-sheet-001",
        "generated-formatting-001",
        "generated-structure-001",
        "generated-validation-001",
        "generated-conditional-formatting-001",
        "generated-color-scales-001",
        "generated-data-bars-001",
        "generated-icon-sets-001",
        "generated-text-boxes-shapes-001",
        "generated-images-sparklines-001",
        "generated-comments-hyperlinks-002",
        "generated-merged-freeze-002",
        "generated-print-titles-breaks-001",
        "generated-named-ranges-formulas-002",
        "generated-validation-custom-002",
        "generated-style-only-cells-002",
        "generated-charts-combo-002",
        "generated-pivots-filters-002",
        "generated-structured-table-totals-002",
        "generated-images-sparklines-002",
        "generated-objects-001",
        "generated-charts-001",
        "generated-pivots-001",
        "generated-structured-tables-001",
        "generated-protection-page-setup-001"
    };

    public static bool CanCreate(string id) => SupportedIds.Contains(id);

    public static bool CanCreateKnownGapPackage(string id) => id switch
    {
        "generated-threaded-comments-001" => true,
        "generated-track-changes-001" => true,
        "generated-form-controls-001" => true,
        "generated-digital-signatures-001" => true,
        "generated-custom-ribbon-ui-001" => true,
        "generated-office-addins-001" => true,
        "generated-live-web-queries-001" => true,
        "generated-sensitivity-labels-001" => true,
        "generated-smartart-diagrams-001" => true,
        "generated-printer-settings-001" => true,
        "generated-calc-chain-001" => true,
        "generated-document-properties-001" => true,
        "generated-header-footer-legacy-drawing-001" => true,
        "generated-worksheet-legacy-drawing-001" => true,
        "generated-workbook-extension-list-001" => true,
        "generated-workbook-properties-001" => true,
        "generated-workbook-calculation-001" => true,
        "generated-workbook-file-version-001" => true,
        "generated-workbook-file-recovery-001" => true,
        "generated-workbook-file-sharing-001" => true,
        "generated-workbook-smart-tags-001" => true,
        "generated-workbook-function-groups-001" => true,
        "generated-workbook-views-001" => true,
        "generated-worksheet-ignored-errors-001" => true,
        "generated-worksheet-cell-watches-001" => true,
        "generated-worksheet-single-xml-cells-001" => true,
        "generated-worksheet-sheet-views-001" => true,
        "generated-worksheet-sheet-format-001" => true,
        "generated-worksheet-phonetic-properties-001" => true,
        "generated-worksheet-sort-state-001" => true,
        "generated-worksheet-data-consolidation-001" => true,
        "generated-worksheet-custom-properties-001" => true,
        "generated-worksheet-smart-tags-001" => true,
        "generated-worksheet-scenarios-001" => true,
        "generated-worksheet-custom-sheet-views-001" => true,
        "generated-unsupported-sheet-types-001" => true,
        "generated-unsupported-chart-001" => true,
        "generated-vba-macros-001" => true,
        "generated-power-query-001" => true,
        "generated-data-model-001" => true,
        "generated-linked-data-types-001" => true,
        "generated-slicers-001" => true,
        "generated-timelines-001" => true,
        "generated-external-links-001" => true,
        "generated-embedded-objects-001" => true,
        "generated-custom-xml-001" => true,
        "generated-custom-docprops-001" => true,
        _ => false
    };

    public static bool CanCreateKnownGapRetentionPackage(string id) => CanCreateKnownGapPackage(id);

    public static Workbook Create(string id) => id switch
    {
        "generated-grid-basic-001" => CreateGridBasic(),
        "generated-formulas-001" => CreateFormulas(),
        "generated-cross-sheet-001" => CreateCrossSheet(),
        "generated-formatting-001" => CreateFormatting(),
        "generated-structure-001" => CreateStructure(),
        "generated-validation-001" => CreateValidation(),
        "generated-conditional-formatting-001" => CreateConditionalFormatting(),
        "generated-color-scales-001" => CreateColorScales(),
        "generated-data-bars-001" => CreateDataBars(),
        "generated-icon-sets-001" => CreateIconSets(),
        "generated-text-boxes-shapes-001" => CreateTextBoxesAndShapes(),
        "generated-images-sparklines-001" => CreateImagesAndSparklines(),
        "generated-comments-hyperlinks-002" => CreateCommentsAndHyperlinks(),
        "generated-merged-freeze-002" => CreateMergedFreeze(),
        "generated-print-titles-breaks-001" => CreatePrintTitlesAndBreaks(),
        "generated-named-ranges-formulas-002" => CreateNamedRangesAndFormulas(),
        "generated-validation-custom-002" => CreateValidationCustom(),
        "generated-style-only-cells-002" => CreateStyleOnlyCells(),
        "generated-charts-combo-002" => CreateChartsCombo(),
        "generated-pivots-filters-002" => CreatePivotsWithFilters(),
        "generated-structured-table-totals-002" => CreateStructuredTableTotals(),
        "generated-images-sparklines-002" => CreateImagesAndSparklinesVariant(),
        "generated-objects-001" => CreateObjects(),
        "generated-charts-001" => CreateCharts(),
        "generated-pivots-001" => CreatePivots(),
        "generated-structured-tables-001" => CreateStructuredTables(),
        "generated-protection-page-setup-001" => CreateProtectionAndPageSetup(),
        _ => throw new ArgumentOutOfRangeException(nameof(id), id, "No generated XLSX corpus fixture exists for this id.")
    };

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
            ("xl/activeX/activeX1.bin", "Freexcel generated ActiveX placeholder"),
            ("xl/ctrlProps/ctrlProp1.xml", "<controlProperties/>")),
        "generated-digital-signatures-001" => CreatePackage(
            ("_xmlsignatures/origin.sigs", "Freexcel generated signature origin placeholder"),
            ("_xmlsignatures/sig1.xml", "<Signature/>")),
        "generated-custom-ribbon-ui-001" => CreatePackage(("customUI/customUI.xml", """
            <customUI xmlns="http://schemas.microsoft.com/office/2006/01/customui">
              <ribbon/>
            </customUI>
            """)),
        "generated-office-addins-001" => CreatePackage(
            ("xl/webextensions/taskpanes.xml", "<taskpanes/>"),
            ("xl/webextensions/webextension1.xml", "<webextension/>")),
        "generated-live-web-queries-001" => CreatePackage(("xl/webPublishItems.xml", "<webPublishItems/>")),
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
        "generated-printer-settings-001" => CreatePackage(("xl/printerSettings/printerSettings1.bin", "Freexcel generated printer settings placeholder")),
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
                  <dc:title>Freexcel document property corpus</dc:title>
                  <dc:subject>Stable document properties retained</dc:subject>
                  <cp:keywords>xlsx parity</cp:keywords>
                  <cp:lastModifiedBy>Freexcel Fixture</cp:lastModifiedBy>
                </cp:coreProperties>
                """),
            ("docProps/app.xml", """
                <Properties xmlns="http://schemas.openxmlformats.org/officeDocument/2006/extended-properties">
                  <Application>Microsoft Excel</Application>
                  <Company>Freexcel Test Lab</Company>
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
            ("xl/media/headerFooterImage1.png", "Freexcel generated header footer image placeholder")),
        "generated-worksheet-legacy-drawing-001" => CreatePackage(
            ("xl/worksheets/sheet1.xml", """
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                           xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <legacyDrawing r:id="rIdFreexcelLegacyDrawing"/>
                </worksheet>
                """),
            ("xl/worksheets/_rels/sheet1.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdFreexcelLegacyDrawing"
                                Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/vmlDrawing"
                                Target="../drawings/vmlDrawing1.vml"/>
                </Relationships>
                """),
            ("xl/drawings/vmlDrawing1.vml", """
                <xml xmlns:v="urn:schemas-microsoft-com:vml"
                     xmlns:o="urn:schemas-microsoft-com:office:office"
                     xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"
                     xmlns:x="urn:schemas-microsoft-com:office:excel">
                  <v:shape id="FreexcelLegacyDrawingShape" type="#_x0000_t201">
                    <v:imagedata r:id="rIdFreexcelVmlImage"/>
                    <x:ClientData ObjectType="Note"/>
                  </v:shape>
                </xml>
                """),
            ("xl/drawings/_rels/vmlDrawing1.vml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdFreexcelVmlImage"
                                Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image"
                                Target="../media/vmlImage1.png"/>
                </Relationships>
                """),
            ("xl/media/vmlImage1.png", "Freexcel generated VML image placeholder")),
        "generated-workbook-extension-list-001" => CreatePackage(("xl/workbook.xml", """
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <extLst>
                <ext uri="{00112233-4455-6677-8899-AABBCCDDEEFF}">
                  <x15:futureMetadata xmlns:x15="http://schemas.microsoft.com/office/spreadsheetml/2010/11/main"
                                      name="FreexcelUnknownWorkbookExtension"/>
                </ext>
              </extLst>
            </workbook>
            """)),
        "generated-workbook-properties-001" => CreatePackage(("xl/workbook.xml", """
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:fx="urn:freexcel:test">
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
              <fileSharing readOnlyRecommended="1" userName="FreexcelTest" revisionsPassword="1234"/>
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
                <functionGroup name="FreexcelNativeFunctions" customGroupFlag="keep"/>
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
                <customWorkbookView name="FreexcelView" guid="{22222222-2222-2222-2222-222222222222}" autoUpdate="0" mergeInterval="0" personalView="0" includePrintSettings="1" includeHiddenRowCol="1"/>
              </customWorkbookViews>
            </workbook>
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
        "generated-worksheet-custom-properties-001" => CreatePackage(("xl/worksheets/sheet1.xml", """
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <customProperties>
                <customPr name="FreexcelNativeProperty" id="1" unsupportedAttr="kept"/>
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
                <scenario name="BestCase" comment="Scenario comment" hidden="1" locked="1" count="1" user="FreexcelTest">
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
        "generated-vba-macros-001" => CreatePackage(("xl/vbaProject.bin", "Freexcel generated macro placeholder")),
        "generated-pivots-001" => CreatePackage(
            ("xl/pivotTables/pivotTable1.xml", "<pivotTableDefinition/>"),
            ("xl/pivotCache/pivotCacheDefinition1.xml", "<pivotCacheDefinition/>")),
        "generated-power-query-001" => CreatePackage(
            ("xl/connections.xml", "<connections/>"),
            ("xl/queries/query1.xml", "<query/>"),
            ("xl/queryTables/queryTable1.xml", "<queryTable/>")),
        "generated-data-model-001" => CreatePackage(
            ("xl/model/item.data", "Freexcel generated data model placeholder"),
            ("xl/model/item.xml", "<dataModel/>")),
        "generated-linked-data-types-001" => CreatePackage(
            ("xl/richData/rdrichvalue.xml", "<rvData/>"),
            ("xl/richData/rdRichValueTypes.xml", "<rvTypes/>"),
            ("xl/richData/richValueRel.xml", "<richValueRels/>")),
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
                                Target="file:///C:/Freexcel/ExternalWorkbook.xlsx"
                                TargetMode="External"/>
                </Relationships>
                """)),
        "generated-embedded-objects-001" => CreatePackage(("xl/embeddings/oleObject1.bin", "Freexcel generated OLE placeholder")),
        "generated-custom-xml-001" => CreatePackage(
            ("customXml/item1.xml", "<freexcelGeneratedCustomXml/>"),
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
        (string.Equals(id, "generated-workbook-smart-tags-001", StringComparison.OrdinalIgnoreCase) &&
         string.Equals(packagePart, "xl/workbook.xml", StringComparison.OrdinalIgnoreCase)) ||
        (string.Equals(id, "generated-workbook-function-groups-001", StringComparison.OrdinalIgnoreCase) &&
         string.Equals(packagePart, "xl/workbook.xml", StringComparison.OrdinalIgnoreCase)) ||
        (string.Equals(id, "generated-workbook-views-001", StringComparison.OrdinalIgnoreCase) &&
         string.Equals(packagePart, "xl/workbook.xml", StringComparison.OrdinalIgnoreCase)) ||
        (string.Equals(id, "generated-worksheet-ignored-errors-001", StringComparison.OrdinalIgnoreCase) &&
         string.Equals(packagePart, "xl/worksheets/sheet1.xml", StringComparison.OrdinalIgnoreCase)) ||
        (string.Equals(id, "generated-worksheet-cell-watches-001", StringComparison.OrdinalIgnoreCase) &&
         string.Equals(packagePart, "xl/worksheets/sheet1.xml", StringComparison.OrdinalIgnoreCase)) ||
        (string.Equals(id, "generated-worksheet-single-xml-cells-001", StringComparison.OrdinalIgnoreCase) &&
         string.Equals(packagePart, "xl/worksheets/sheet1.xml", StringComparison.OrdinalIgnoreCase)) ||
        (string.Equals(id, "generated-worksheet-sheet-views-001", StringComparison.OrdinalIgnoreCase) &&
         string.Equals(packagePart, "xl/worksheets/sheet1.xml", StringComparison.OrdinalIgnoreCase)) ||
        (string.Equals(id, "generated-worksheet-sheet-format-001", StringComparison.OrdinalIgnoreCase) &&
         string.Equals(packagePart, "xl/worksheets/sheet1.xml", StringComparison.OrdinalIgnoreCase)) ||
        (string.Equals(id, "generated-worksheet-phonetic-properties-001", StringComparison.OrdinalIgnoreCase) &&
         string.Equals(packagePart, "xl/worksheets/sheet1.xml", StringComparison.OrdinalIgnoreCase)) ||
        (string.Equals(id, "generated-worksheet-sort-state-001", StringComparison.OrdinalIgnoreCase) &&
         string.Equals(packagePart, "xl/worksheets/sheet1.xml", StringComparison.OrdinalIgnoreCase)) ||
        (string.Equals(id, "generated-worksheet-data-consolidation-001", StringComparison.OrdinalIgnoreCase) &&
         string.Equals(packagePart, "xl/worksheets/sheet1.xml", StringComparison.OrdinalIgnoreCase)) ||
        (string.Equals(id, "generated-worksheet-custom-properties-001", StringComparison.OrdinalIgnoreCase) &&
         string.Equals(packagePart, "xl/worksheets/sheet1.xml", StringComparison.OrdinalIgnoreCase)) ||
        (string.Equals(id, "generated-worksheet-smart-tags-001", StringComparison.OrdinalIgnoreCase) &&
         string.Equals(packagePart, "xl/worksheets/sheet1.xml", StringComparison.OrdinalIgnoreCase)) ||
        (string.Equals(id, "generated-worksheet-scenarios-001", StringComparison.OrdinalIgnoreCase) &&
         string.Equals(packagePart, "xl/worksheets/sheet1.xml", StringComparison.OrdinalIgnoreCase)) ||
        (string.Equals(id, "generated-worksheet-custom-sheet-views-001", StringComparison.OrdinalIgnoreCase) &&
         string.Equals(packagePart, "xl/worksheets/sheet1.xml", StringComparison.OrdinalIgnoreCase));

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
            "xl/vbaProject.bin" => "application/vnd.ms-office.vbaProject",
            "xl/pivotTables/pivotTable1.xml" => "application/vnd.openxmlformats-officedocument.spreadsheetml.pivotTable+xml",
            "xl/pivotCache/pivotCacheDefinition1.xml" => "application/vnd.openxmlformats-officedocument.spreadsheetml.pivotCacheDefinition+xml",
            "xl/connections.xml" => "application/vnd.openxmlformats-officedocument.spreadsheetml.connections+xml",
            "xl/queries/query1.xml" => "application/vnd.ms-excel.queryTable+xml",
            "xl/queryTables/queryTable1.xml" => "application/vnd.openxmlformats-officedocument.spreadsheetml.queryTable+xml",
            "xl/model/item.xml" => "application/xml",
            "xl/model/item.data" => "application/vnd.ms-excel.model",
            "xl/richData/rdrichvalue.xml" => "application/vnd.ms-excel.rdrichvalue+xml",
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

    private static void ApplyPackageFixups(string id, ZipArchive archive)
    {
        if (string.Equals(id, "generated-slicers-001", StringComparison.OrdinalIgnoreCase))
        {
            ApplySlicerTimelineFloatingDrawingFixup(
                archive,
                "Slicer Region",
                "../slicers/slicer1.xml",
                "http://schemas.microsoft.com/office/2007/relationships/slicer");
            return;
        }

        if (string.Equals(id, "generated-timelines-001", StringComparison.OrdinalIgnoreCase))
        {
            ApplySlicerTimelineFloatingDrawingFixup(
                archive,
                "Timeline Date",
                "../timelines/timeline1.xml",
                "http://schemas.microsoft.com/office/2011/relationships/timeline");
            return;
        }

        if (string.Equals(id, "generated-printer-settings-001", StringComparison.OrdinalIgnoreCase))
        {
            ApplyPrinterSettingsReferenceFixup(archive);
            return;
        }

        if (string.Equals(id, "generated-calc-chain-001", StringComparison.OrdinalIgnoreCase))
        {
            ApplyCalcChainReferenceFixup(archive);
            return;
        }

        if (string.Equals(id, "generated-custom-xml-001", StringComparison.OrdinalIgnoreCase))
        {
            ApplyCustomXmlReferenceFixup(archive);
            return;
        }

        if (string.Equals(id, "generated-custom-docprops-001", StringComparison.OrdinalIgnoreCase))
        {
            ApplyCustomDocumentPropertiesReferenceFixup(archive);
            return;
        }

        if (string.Equals(id, "generated-header-footer-legacy-drawing-001", StringComparison.OrdinalIgnoreCase))
        {
            ApplyHeaderFooterLegacyDrawingReferenceFixup(archive);
            return;
        }

        if (string.Equals(id, "generated-workbook-extension-list-001", StringComparison.OrdinalIgnoreCase))
        {
            ApplyWorkbookExtensionListFixup(archive);
            return;
        }

        if (string.Equals(id, "generated-worksheet-legacy-drawing-001", StringComparison.OrdinalIgnoreCase))
        {
            ApplyWorksheetLegacyDrawingFixup(archive);
            return;
        }

        if (string.Equals(id, "generated-workbook-properties-001", StringComparison.OrdinalIgnoreCase))
        {
            ApplyWorkbookPropertiesFixup(archive);
            return;
        }

        if (string.Equals(id, "generated-workbook-calculation-001", StringComparison.OrdinalIgnoreCase))
        {
            ApplyWorkbookCalculationFixup(archive);
            return;
        }

        if (string.Equals(id, "generated-workbook-file-version-001", StringComparison.OrdinalIgnoreCase))
        {
            ApplyWorkbookFileVersionFixup(archive);
            return;
        }

        if (string.Equals(id, "generated-workbook-file-recovery-001", StringComparison.OrdinalIgnoreCase))
        {
            ApplyWorkbookFileRecoveryFixup(archive);
            return;
        }

        if (string.Equals(id, "generated-workbook-file-sharing-001", StringComparison.OrdinalIgnoreCase))
        {
            ApplyWorkbookFileSharingFixup(archive);
            return;
        }

        if (string.Equals(id, "generated-workbook-smart-tags-001", StringComparison.OrdinalIgnoreCase))
        {
            ApplyWorkbookSmartTagsFixup(archive);
            return;
        }

        if (string.Equals(id, "generated-workbook-function-groups-001", StringComparison.OrdinalIgnoreCase))
        {
            ApplyWorkbookFunctionGroupsFixup(archive);
            return;
        }

        if (string.Equals(id, "generated-workbook-views-001", StringComparison.OrdinalIgnoreCase))
        {
            ApplyWorkbookViewsFixup(archive);
            return;
        }

        if (string.Equals(id, "generated-worksheet-ignored-errors-001", StringComparison.OrdinalIgnoreCase))
        {
            ApplyWorksheetIgnoredErrorsFixup(archive);
            return;
        }

        if (string.Equals(id, "generated-worksheet-cell-watches-001", StringComparison.OrdinalIgnoreCase))
        {
            ApplyWorksheetCellWatchesFixup(archive);
            return;
        }

        if (string.Equals(id, "generated-worksheet-single-xml-cells-001", StringComparison.OrdinalIgnoreCase))
        {
            ApplyWorksheetSingleXmlCellsFixup(archive);
            return;
        }

        if (string.Equals(id, "generated-worksheet-sheet-views-001", StringComparison.OrdinalIgnoreCase))
        {
            ApplyWorksheetSheetViewsFixup(archive);
            return;
        }

        if (string.Equals(id, "generated-worksheet-sheet-format-001", StringComparison.OrdinalIgnoreCase))
        {
            ApplyWorksheetSheetFormatFixup(archive);
            return;
        }

        if (string.Equals(id, "generated-worksheet-phonetic-properties-001", StringComparison.OrdinalIgnoreCase))
        {
            ApplyWorksheetPhoneticPropertiesFixup(archive);
            return;
        }

        if (string.Equals(id, "generated-worksheet-sort-state-001", StringComparison.OrdinalIgnoreCase))
        {
            ApplyWorksheetSortStateFixup(archive);
            return;
        }

        if (string.Equals(id, "generated-worksheet-data-consolidation-001", StringComparison.OrdinalIgnoreCase))
        {
            ApplyWorksheetDataConsolidationFixup(archive);
            return;
        }

        if (string.Equals(id, "generated-worksheet-custom-properties-001", StringComparison.OrdinalIgnoreCase))
        {
            ApplyWorksheetCustomPropertiesFixup(archive);
            return;
        }

        if (string.Equals(id, "generated-worksheet-smart-tags-001", StringComparison.OrdinalIgnoreCase))
        {
            ApplyWorksheetSmartTagsFixup(archive);
            return;
        }

        if (string.Equals(id, "generated-worksheet-scenarios-001", StringComparison.OrdinalIgnoreCase))
        {
            ApplyWorksheetScenariosFixup(archive);
            return;
        }

        if (string.Equals(id, "generated-worksheet-custom-sheet-views-001", StringComparison.OrdinalIgnoreCase))
        {
            ApplyWorksheetCustomSheetViewsFixup(archive);
            return;
        }

        if (!string.Equals(id, "generated-external-links-001", StringComparison.OrdinalIgnoreCase))
            return;

        XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace officeRelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        var contentTypesEntry = archive.GetEntry("[Content_Types].xml");
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        var workbookRelsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels");
        if (contentTypesEntry is null || workbookEntry is null || workbookRelsEntry is null)
            return;

        XDocument contentTypes;
        using (var stream = contentTypesEntry.Open())
            contentTypes = XDocument.Load(stream);

        if (contentTypes.Root?.Elements(contentTypeNs + "Override").Any(element =>
                string.Equals(element.Attribute("PartName")?.Value, "/xl/externalLinks/externalLink1.xml", StringComparison.OrdinalIgnoreCase)) != true)
        {
            contentTypes.Root?.Add(new XElement(
                contentTypeNs + "Override",
                new XAttribute("PartName", "/xl/externalLinks/externalLink1.xml"),
                new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.externalLink+xml")));
        }

        contentTypesEntry.Delete();
        var contentTypesReplacement = archive.CreateEntry("[Content_Types].xml");
        using (var output = contentTypesReplacement.Open())
            contentTypes.Save(output);

        XDocument workbookXml;
        using (var stream = workbookEntry.Open())
            workbookXml = XDocument.Load(stream);
        workbookXml.Root?.Element(workbookNs + "externalReferences")?.Remove();
        workbookXml.Root?.Add(new XElement(
            workbookNs + "externalReferences",
            new XElement(workbookNs + "externalReference", new XAttribute(officeRelNs + "id", "rIdFreexcelExternalLink1"))));
        workbookEntry.Delete();
        var workbookReplacement = archive.CreateEntry("xl/workbook.xml");
        using (var output = workbookReplacement.Open())
            workbookXml.Save(output);

        XDocument workbookRelsXml;
        using (var stream = workbookRelsEntry.Open())
            workbookRelsXml = XDocument.Load(stream);
        workbookRelsXml.Root?.Elements(packageRelNs + "Relationship")
            .Where(element => string.Equals(element.Attribute("Id")?.Value, "rIdFreexcelExternalLink1", StringComparison.OrdinalIgnoreCase))
            .Remove();
        workbookRelsXml.Root?.Add(new XElement(
            packageRelNs + "Relationship",
            new XAttribute("Id", "rIdFreexcelExternalLink1"),
            new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/externalLink"),
            new XAttribute("Target", "externalLinks/externalLink1.xml")));
        workbookRelsEntry.Delete();
        var workbookRelsReplacement = archive.CreateEntry("xl/_rels/workbook.xml.rels");
        using var relOutput = workbookRelsReplacement.Open();
        workbookRelsXml.Save(relOutput);
    }

    private static void ApplySlicerTimelineFloatingDrawingFixup(
        ZipArchive archive,
        string objectName,
        string nativePartTarget,
        string nativeRelationshipType)
    {
        XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace officeRelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        XNamespace spreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
        XNamespace drawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";

        var contentTypesEntry = archive.GetEntry("[Content_Types].xml");
        var worksheetEntry = archive.GetEntry("xl/worksheets/sheet1.xml");
        if (contentTypesEntry is null || worksheetEntry is null)
            return;

        XDocument contentTypes;
        using (var stream = contentTypesEntry.Open())
            contentTypes = XDocument.Load(stream);
        if (contentTypes.Root?.Elements(contentTypeNs + "Override").Any(element =>
                string.Equals(element.Attribute("PartName")?.Value, "/xl/drawings/drawing1.xml", StringComparison.OrdinalIgnoreCase)) != true)
        {
            contentTypes.Root?.Add(new XElement(
                contentTypeNs + "Override",
                new XAttribute("PartName", "/xl/drawings/drawing1.xml"),
                new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.drawing+xml")));
        }
        EnsureContentTypeOverride(
            contentTypes,
            nativePartTarget.Contains("/slicers/", StringComparison.OrdinalIgnoreCase)
                ? "/xl/slicers/slicer1.xml"
                : "/xl/timelines/timeline1.xml",
            nativePartTarget.Contains("/slicers/", StringComparison.OrdinalIgnoreCase)
                ? "application/vnd.ms-excel.slicer+xml"
                : "application/vnd.ms-excel.timeline+xml");
        EnsureContentTypeOverride(
            contentTypes,
            nativePartTarget.Contains("/slicers/", StringComparison.OrdinalIgnoreCase)
                ? "/xl/slicerCaches/slicerCache1.xml"
                : "/xl/timelineCaches/timelineCache1.xml",
            nativePartTarget.Contains("/slicers/", StringComparison.OrdinalIgnoreCase)
                ? "application/vnd.ms-excel.slicerCache+xml"
                : "application/vnd.ms-excel.timelineCache+xml");
        ReplacePackageXml(archive, "[Content_Types].xml", contentTypes);

        var drawingRelId = "rIdFreexcelFloatingDrawing1";
        XDocument worksheetXml;
        using (var stream = worksheetEntry.Open())
            worksheetXml = XDocument.Load(stream);
        var root = worksheetXml.Root;
        if (root is not null && root.Element(worksheetNs + "drawing") is null)
            root.Add(new XElement(worksheetNs + "drawing", new XAttribute(officeRelNs + "id", drawingRelId)));
        ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);

        var worksheetRelsPath = "xl/worksheets/_rels/sheet1.xml.rels";
        var worksheetRelsXml = archive.GetEntry(worksheetRelsPath) is { } worksheetRelsEntry
            ? LoadPackageXml(worksheetRelsEntry)
            : new XDocument(new XElement(packageRelNs + "Relationships"));
        EnsureRelationship(
            worksheetRelsXml,
            drawingRelId,
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing",
            "../drawings/drawing1.xml");
        ReplacePackageXml(archive, worksheetRelsPath, worksheetRelsXml);

        ReplacePackageXml(archive, "xl/drawings/drawing1.xml", new XDocument(
            new XElement(
                spreadsheetDrawingNs + "wsDr",
                new XAttribute(XNamespace.Xmlns + "xdr", spreadsheetDrawingNs),
                new XAttribute(XNamespace.Xmlns + "a", drawingNs),
                new XElement(
                    spreadsheetDrawingNs + "twoCellAnchor",
                    new XElement(
                        spreadsheetDrawingNs + "from",
                        new XElement(spreadsheetDrawingNs + "col", "4"),
                        new XElement(spreadsheetDrawingNs + "colOff", "0"),
                        new XElement(spreadsheetDrawingNs + "row", "2"),
                        new XElement(spreadsheetDrawingNs + "rowOff", "0")),
                    new XElement(
                        spreadsheetDrawingNs + "to",
                        new XElement(spreadsheetDrawingNs + "col", "8"),
                        new XElement(spreadsheetDrawingNs + "colOff", "0"),
                        new XElement(spreadsheetDrawingNs + "row", "10"),
                        new XElement(spreadsheetDrawingNs + "rowOff", "0")),
                    new XElement(
                        spreadsheetDrawingNs + "sp",
                        new XElement(
                            spreadsheetDrawingNs + "nvSpPr",
                            new XElement(
                                spreadsheetDrawingNs + "cNvPr",
                                new XAttribute("id", "2"),
                                new XAttribute("name", objectName)),
                            new XElement(spreadsheetDrawingNs + "cNvSpPr")),
                        new XElement(
                            spreadsheetDrawingNs + "spPr",
                            new XElement(drawingNs + "prstGeom",
                                new XAttribute("prst", "rect"),
                                new XElement(drawingNs + "avLst")))),
                    new XElement(spreadsheetDrawingNs + "clientData")))));

        var drawingRelsXml = new XDocument(new XElement(packageRelNs + "Relationships"));
        EnsureRelationship(drawingRelsXml, "rIdFreexcelNativeControl1", nativeRelationshipType, nativePartTarget);
        ReplacePackageXml(archive, "xl/drawings/_rels/drawing1.xml.rels", drawingRelsXml);
    }

    private static void ApplyPrinterSettingsReferenceFixup(ZipArchive archive)
    {
        XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace officeRelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        var contentTypesEntry = archive.GetEntry("[Content_Types].xml");
        var worksheetEntry = archive.GetEntry("xl/worksheets/sheet1.xml");
        if (contentTypesEntry is null || worksheetEntry is null)
            return;

        var contentTypes = LoadPackageXml(contentTypesEntry);
        EnsureContentTypeOverride(
            contentTypes,
            "/xl/printerSettings/printerSettings1.bin",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.printerSettings");
        ReplacePackageXml(archive, "[Content_Types].xml", contentTypes);

        var worksheetXml = LoadPackageXml(worksheetEntry);
        var pageSetup = worksheetXml.Root?.Element(worksheetNs + "pageSetup");
        if (pageSetup is null)
        {
            pageSetup = new XElement(worksheetNs + "pageSetup",
                new XAttribute("paperSize", "1"),
                new XAttribute("orientation", "portrait"));
            worksheetXml.Root?.Add(pageSetup);
        }

        pageSetup.SetAttributeValue(officeRelNs + "id", "rIdPrinterSettings1");
        ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);

        var worksheetRelsPath = "xl/worksheets/_rels/sheet1.xml.rels";
        var worksheetRelsXml = archive.GetEntry(worksheetRelsPath) is { } worksheetRelsEntry
            ? LoadPackageXml(worksheetRelsEntry)
            : new XDocument(new XElement(packageRelNs + "Relationships"));
        EnsureRelationship(
            worksheetRelsXml,
            "rIdPrinterSettings1",
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/printerSettings",
            "../printerSettings/printerSettings1.bin");
        ReplacePackageXml(archive, worksheetRelsPath, worksheetRelsXml);
    }

    private static void ApplyCalcChainReferenceFixup(ZipArchive archive)
    {
        XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        var contentTypesEntry = archive.GetEntry("[Content_Types].xml");
        if (contentTypesEntry is not null)
        {
            var contentTypes = LoadPackageXml(contentTypesEntry);
            EnsureContentTypeOverride(
                contentTypes,
                "/xl/calcChain.xml",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.calcChain+xml");
            ReplacePackageXml(archive, "[Content_Types].xml", contentTypes);
        }

        var workbookRelsPath = "xl/_rels/workbook.xml.rels";
        var workbookRelsXml = archive.GetEntry(workbookRelsPath) is { } workbookRelsEntry
            ? LoadPackageXml(workbookRelsEntry)
            : new XDocument(new XElement(packageRelNs + "Relationships"));
        EnsureRelationship(
            workbookRelsXml,
            "rIdFreexcelCalcChain1",
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/calcChain",
            "calcChain.xml");
        ReplacePackageXml(archive, workbookRelsPath, workbookRelsXml);
    }

    private static void ApplyCustomXmlReferenceFixup(ZipArchive archive)
    {
        XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        var contentTypesEntry = archive.GetEntry("[Content_Types].xml");
        if (contentTypesEntry is not null)
        {
            var contentTypes = LoadPackageXml(contentTypesEntry);
            EnsureContentTypeOverride(contentTypes, "/customXml/item1.xml", "application/xml");
            EnsureContentTypeOverride(
                contentTypes,
                "/customXml/itemProps1.xml",
                "application/vnd.openxmlformats-officedocument.customXmlProperties+xml");
            ReplacePackageXml(archive, "[Content_Types].xml", contentTypes);
        }

        var packageRelsPath = "_rels/.rels";
        var packageRelsXml = archive.GetEntry(packageRelsPath) is { } packageRelsEntry
            ? LoadPackageXml(packageRelsEntry)
            : new XDocument(new XElement(packageRelNs + "Relationships"));
        EnsureRelationship(
            packageRelsXml,
            "rIdFreexcelCustomXml1",
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXml",
            "customXml/item1.xml");
        EnsureRelationship(
            packageRelsXml,
            "rIdFreexcelCustomXmlExternalSchema1",
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXml",
            "https://schemas.freexcel.example/customXml/schema1.xsd",
            "External");
        ReplacePackageXml(archive, packageRelsPath, packageRelsXml);
    }

    private static void ApplyCustomDocumentPropertiesReferenceFixup(ZipArchive archive)
    {
        XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        var contentTypesEntry = archive.GetEntry("[Content_Types].xml");
        if (contentTypesEntry is not null)
        {
            var contentTypes = LoadPackageXml(contentTypesEntry);
            EnsureContentTypeOverride(
                contentTypes,
                "/docProps/custom.xml",
                "application/vnd.openxmlformats-officedocument.custom-properties+xml");
            ReplacePackageXml(archive, "[Content_Types].xml", contentTypes);
        }

        var packageRelsPath = "_rels/.rels";
        var packageRelsXml = archive.GetEntry(packageRelsPath) is { } packageRelsEntry
            ? LoadPackageXml(packageRelsEntry)
            : new XDocument(new XElement(packageRelNs + "Relationships"));
        EnsureRelationship(
            packageRelsXml,
            "rIdFreexcelCustomDocumentProperties1",
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/custom-properties",
            "docProps/custom.xml");
        ReplacePackageXml(archive, packageRelsPath, packageRelsXml);
    }

    private static void ApplyHeaderFooterLegacyDrawingReferenceFixup(ZipArchive archive)
    {
        XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace officeRelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        var contentTypesEntry = archive.GetEntry("[Content_Types].xml");
        if (contentTypesEntry is not null)
        {
            var contentTypes = LoadPackageXml(contentTypesEntry);
            EnsureContentTypeOverride(
                contentTypes,
                "/xl/drawings/vmlDrawing1.vml",
                "application/vnd.openxmlformats-officedocument.vmlDrawing");
            EnsureContentTypeOverride(contentTypes, "/xl/media/headerFooterImage1.png", "image/png");
            ReplacePackageXml(archive, "[Content_Types].xml", contentTypes);
        }

        var worksheetPath = "xl/worksheets/sheet1.xml";
        var worksheetEntry = archive.GetEntry(worksheetPath);
        if (worksheetEntry is not null)
        {
            var worksheetXml = LoadPackageXml(worksheetEntry);
            worksheetXml.Root?.Elements(worksheetNs + "legacyDrawingHF").Remove();
            worksheetXml.Root?.Add(new XElement(
                worksheetNs + "legacyDrawingHF",
                new XAttribute(officeRelNs + "id", "rIdHeaderFooterDrawing1")));
            ReplacePackageXml(archive, worksheetPath, worksheetXml);
        }

        var worksheetRelsPath = "xl/worksheets/_rels/sheet1.xml.rels";
        var worksheetRelsXml = archive.GetEntry(worksheetRelsPath) is { } worksheetRelsEntry
            ? LoadPackageXml(worksheetRelsEntry)
            : new XDocument(new XElement(packageRelNs + "Relationships"));
        EnsureRelationship(
            worksheetRelsXml,
            "rIdHeaderFooterDrawing1",
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/vmlDrawing",
            "../drawings/vmlDrawing1.vml");
        ReplacePackageXml(archive, worksheetRelsPath, worksheetRelsXml);
    }

    private static void ApplyWorkbookExtensionListFixup(ZipArchive archive)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace x15Ns = "http://schemas.microsoft.com/office/spreadsheetml/2010/11/main";

        var workbookPath = "xl/workbook.xml";
        var workbookEntry = archive.GetEntry(workbookPath);
        if (workbookEntry is null)
            return;

        var workbookXml = LoadPackageXml(workbookEntry);
        workbookXml.Root?.Elements(workbookNs + "extLst").Remove();
        workbookXml.Root?.Add(new XElement(
            workbookNs + "extLst",
            new XElement(
                workbookNs + "ext",
                new XAttribute("uri", "{00112233-4455-6677-8899-AABBCCDDEEFF}"),
                new XElement(
                    x15Ns + "futureMetadata",
                    new XAttribute(XNamespace.Xmlns + "x15", x15Ns),
                    new XAttribute("name", "FreexcelUnknownWorkbookExtension")))));
        ReplacePackageXml(archive, workbookPath, workbookXml);
    }

    private static void ApplyWorksheetLegacyDrawingFixup(ZipArchive archive)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace officeRelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        var worksheetPath = "xl/worksheets/sheet1.xml";
        var worksheetEntry = archive.GetEntry(worksheetPath);
        if (worksheetEntry is not null)
        {
            var worksheetXml = LoadPackageXml(worksheetEntry);
            worksheetXml.Root?.Elements(worksheetNs + "legacyDrawing").Remove();
            worksheetXml.Root?.Add(new XElement(
                worksheetNs + "legacyDrawing",
                new XAttribute(officeRelNs + "id", "rIdFreexcelLegacyDrawing")));
            ReplacePackageXml(archive, worksheetPath, worksheetXml);
        }

        var worksheetRelsPath = "xl/worksheets/_rels/sheet1.xml.rels";
        var worksheetRelsXml = archive.GetEntry(worksheetRelsPath) is { } worksheetRelsEntry
            ? LoadPackageXml(worksheetRelsEntry)
            : new XDocument(new XElement(packageRelNs + "Relationships"));
        EnsureRelationship(
            worksheetRelsXml,
            "rIdFreexcelLegacyDrawing",
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/vmlDrawing",
            "../drawings/vmlDrawing1.vml");
        ReplacePackageXml(archive, worksheetRelsPath, worksheetRelsXml);
    }

    private static void ApplyWorkbookFileVersionFixup(ZipArchive archive)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        var workbookPath = "xl/workbook.xml";
        var workbookEntry = archive.GetEntry(workbookPath);
        if (workbookEntry is null)
            return;

        var workbookXml = LoadPackageXml(workbookEntry);
        workbookXml.Root?.Elements(workbookNs + "fileVersion").Remove();
        workbookXml.Root?.AddFirst(new XElement(
            workbookNs + "fileVersion",
            new XAttribute("appName", "xl"),
            new XAttribute("lastEdited", "7"),
            new XAttribute("lowestEdited", "7"),
            new XAttribute("rupBuild", "28129"),
            new XAttribute("customVersionFlag", "keep")));
        ReplacePackageXml(archive, workbookPath, workbookXml);
    }

    private static void ApplyWorkbookPropertiesFixup(ZipArchive archive)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace freexcelNs = "urn:freexcel:test";

        var workbookPath = "xl/workbook.xml";
        var workbookEntry = archive.GetEntry(workbookPath);
        if (workbookEntry is null)
            return;

        var workbookXml = LoadPackageXml(workbookEntry);
        workbookXml.Root?.Elements(workbookNs + "workbookPr").Remove();
        workbookXml.Root?.AddFirst(new XElement(
            workbookNs + "workbookPr",
            new XAttribute("date1904", "1"),
            new XAttribute("defaultThemeVersion", "166925"),
            new XElement(freexcelNs + "workbookPrNativeChild", new XAttribute("id", "first")),
            new XElement(freexcelNs + "workbookPrNativeChild", new XAttribute("id", "second"))));
        ReplacePackageXml(archive, workbookPath, workbookXml);
    }

    private static void ApplyWorkbookCalculationFixup(ZipArchive archive)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        var workbookPath = "xl/workbook.xml";
        var workbookEntry = archive.GetEntry(workbookPath);
        if (workbookEntry is null)
            return;

        var workbookXml = LoadPackageXml(workbookEntry);
        workbookXml.Root?.Elements(workbookNs + "calcPr").Remove();
        workbookXml.Root?.Add(new XElement(
            workbookNs + "calcPr",
            new XAttribute("calcMode", "manual"),
            new XAttribute("iterate", "1"),
            new XAttribute("iterateCount", "50"),
            new XAttribute("calcId", "191029"),
            new XAttribute("refMode", "A1"),
            new XAttribute("fullPrecision", "0"),
            new XAttribute("concurrentCalc", "1")));
        ReplacePackageXml(archive, workbookPath, workbookXml);
    }

    private static void ApplyWorkbookFileRecoveryFixup(ZipArchive archive)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        var workbookPath = "xl/workbook.xml";
        var workbookEntry = archive.GetEntry(workbookPath);
        if (workbookEntry is null)
            return;

        var workbookXml = LoadPackageXml(workbookEntry);
        workbookXml.Root?.Elements(workbookNs + "fileRecoveryPr").Remove();
        workbookXml.Root?.Add(
            new XElement(
                workbookNs + "fileRecoveryPr",
                new XAttribute("autoRecover", "1"),
                new XAttribute("crashSave", "1"),
                new XAttribute("customRecoveryFlag", "keep"),
                new XAttribute("repairLoad", "0")),
            new XElement(
                workbookNs + "fileRecoveryPr",
                new XAttribute("dataExtractLoad", "1"),
                new XAttribute("repairLoad", "1")));
        ReplacePackageXml(archive, workbookPath, workbookXml);
    }

    private static void ApplyWorkbookFileSharingFixup(ZipArchive archive)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        var workbookPath = "xl/workbook.xml";
        var workbookEntry = archive.GetEntry(workbookPath);
        if (workbookEntry is null)
            return;

        var workbookXml = LoadPackageXml(workbookEntry);
        workbookXml.Root?.Elements(workbookNs + "fileSharing").Remove();
        workbookXml.Root?.AddFirst(new XElement(
            workbookNs + "fileSharing",
            new XAttribute("readOnlyRecommended", "1"),
            new XAttribute("userName", "FreexcelTest"),
            new XAttribute("revisionsPassword", "1234")));
        ReplacePackageXml(archive, workbookPath, workbookXml);
    }

    private static void ApplyWorkbookSmartTagsFixup(ZipArchive archive)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        var workbookPath = "xl/workbook.xml";
        var workbookEntry = archive.GetEntry(workbookPath);
        if (workbookEntry is null)
            return;

        var workbookXml = LoadPackageXml(workbookEntry);
        workbookXml.Root?.Elements(workbookNs + "smartTagPr").Remove();
        workbookXml.Root?.Elements(workbookNs + "smartTagTypes").Remove();
        workbookXml.Root?.Add(
            new XElement(
                workbookNs + "smartTagPr",
                new XAttribute("embed", "1"),
                new XAttribute("show", "all"),
                new XAttribute("customSmartTagFlag", "keep")),
            new XElement(
                workbookNs + "smartTagTypes",
                new XAttribute("customSmartTagTypesFlag", "keep"),
                new XElement(
                    workbookNs + "smartTagType",
                    new XAttribute("namespaceUri", "urn:schemas-microsoft-com:office:smarttags"),
                    new XAttribute("name", "place"),
                    new XAttribute("customSmartTagTypeFlag", "keep"))));
        ReplacePackageXml(archive, workbookPath, workbookXml);
    }

    private static void ApplyWorkbookFunctionGroupsFixup(ZipArchive archive)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        var workbookPath = "xl/workbook.xml";
        var workbookEntry = archive.GetEntry(workbookPath);
        if (workbookEntry is null)
            return;

        var workbookXml = LoadPackageXml(workbookEntry);
        workbookXml.Root?.Elements(workbookNs + "functionGroups").Remove();
        workbookXml.Root?.Add(new XElement(
            workbookNs + "functionGroups",
            new XAttribute("builtInGroupCount", "16"),
            new XAttribute("customFunctionGroupFlag", "keep"),
            new XElement(
                workbookNs + "functionGroup",
                new XAttribute("name", "FreexcelNativeFunctions"),
                new XAttribute("customGroupFlag", "keep"))));
        ReplacePackageXml(archive, workbookPath, workbookXml);
    }

    private static void ApplyWorkbookViewsFixup(ZipArchive archive)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        var workbookPath = "xl/workbook.xml";
        var workbookEntry = archive.GetEntry(workbookPath);
        if (workbookEntry is null)
            return;

        var workbookXml = LoadPackageXml(workbookEntry);
        workbookXml.Root?.Elements(workbookNs + "bookViews").Remove();
        workbookXml.Root?.Elements(workbookNs + "customWorkbookViews").Remove();
        workbookXml.Root?.AddFirst(new XElement(
            workbookNs + "bookViews",
            new XElement(
                workbookNs + "workbookView",
                new XAttribute("visibility", "visible"),
                new XAttribute("showSheetTabs", "0"),
                new XAttribute("tabRatio", "700"),
                new XAttribute("firstSheet", "0"),
                new XAttribute("activeTab", "0")),
            new XElement(
                workbookNs + "workbookView",
                new XAttribute("visibility", "hidden"),
                new XAttribute("minimized", "1"),
                new XAttribute("showHorizontalScroll", "0"),
                new XAttribute("showVerticalScroll", "0"),
                new XAttribute("showSheetTabs", "0"),
                new XAttribute("tabRatio", "700"),
                new XAttribute("firstSheet", "0"),
                new XAttribute("activeTab", "0"),
                new XAttribute("customWorkbookViewFlag", "kept"))));
        workbookXml.Root?.Add(new XElement(
            workbookNs + "customWorkbookViews",
            new XElement(
                workbookNs + "customWorkbookView",
                new XAttribute("name", "FreexcelView"),
                new XAttribute("guid", "{22222222-2222-2222-2222-222222222222}"),
                new XAttribute("autoUpdate", "0"),
                new XAttribute("mergeInterval", "0"),
                new XAttribute("personalView", "0"),
                new XAttribute("includePrintSettings", "1"),
                new XAttribute("includeHiddenRowCol", "1"))));
        ReplacePackageXml(archive, workbookPath, workbookXml);
    }

    private static void ApplyWorksheetIgnoredErrorsFixup(ZipArchive archive)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        var worksheetPath = "xl/worksheets/sheet1.xml";
        var worksheetEntry = archive.GetEntry(worksheetPath);
        if (worksheetEntry is null)
            return;

        var worksheetXml = LoadPackageXml(worksheetEntry);
        worksheetXml.Root?.Elements(worksheetNs + "ignoredErrors").Remove();
        worksheetXml.Root?.Add(new XElement(
            worksheetNs + "ignoredErrors",
            new XElement(
                worksheetNs + "ignoredError",
                new XAttribute("sqref", "A1"),
                new XAttribute("numberStoredAsText", "1"),
                new XAttribute("twoDigitTextYear", "1"))));
        ReplacePackageXml(archive, worksheetPath, worksheetXml);
    }

    private static void ApplyWorksheetCellWatchesFixup(ZipArchive archive)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        var worksheetPath = "xl/worksheets/sheet1.xml";
        var worksheetEntry = archive.GetEntry(worksheetPath);
        if (worksheetEntry is null)
            return;

        var worksheetXml = LoadPackageXml(worksheetEntry);
        worksheetXml.Root?.Elements(worksheetNs + "cellWatches").Remove();
        worksheetXml.Root?.Add(new XElement(
            worksheetNs + "cellWatches",
            new XAttribute("nativeContainer", "kept"),
            new XElement(
                worksheetNs + "cellWatch",
                new XAttribute("r", "A1"),
                new XAttribute("nativeWatch", "kept"))));
        ReplacePackageXml(archive, worksheetPath, worksheetXml);
    }

    private static void ApplyWorksheetSingleXmlCellsFixup(ZipArchive archive)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        var worksheetPath = "xl/worksheets/sheet1.xml";
        var worksheetEntry = archive.GetEntry(worksheetPath);
        if (worksheetEntry is null)
            return;

        var worksheetXml = LoadPackageXml(worksheetEntry);
        worksheetXml.Root?.Elements(worksheetNs + "singleXmlCells").Remove();
        worksheetXml.Root?.Add(new XElement(
            worksheetNs + "singleXmlCells",
            new XAttribute("nativeSingleXmlCellsAttr", "kept"),
            new XElement(
                worksheetNs + "singleXmlCell",
                new XAttribute("id", "1"),
                new XAttribute("r", "A1"),
                new XAttribute("xmlCellPrId", "1"),
                new XAttribute("nativeSingleXmlCellAttr", "cell-kept"))));
        ReplacePackageXml(archive, worksheetPath, worksheetXml);
    }

    private static void ApplyWorksheetSheetViewsFixup(ZipArchive archive)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        var worksheetPath = "xl/worksheets/sheet1.xml";
        var worksheetEntry = archive.GetEntry(worksheetPath);
        if (worksheetEntry is null)
            return;

        var worksheetXml = LoadPackageXml(worksheetEntry);
        worksheetXml.Root?.Elements(worksheetNs + "sheetViews").Remove();
        worksheetXml.Root?.AddFirst(new XElement(
            worksheetNs + "sheetViews",
            new XAttribute("nativeSheetViewsAttr", "kept"),
            new XElement(
                worksheetNs + "sheetView",
                new XAttribute("workbookViewId", "0"),
                new XAttribute("showZeros", "0"),
                new XAttribute("rightToLeft", "1"),
                new XElement(
                    worksheetNs + "pivotSelection",
                    new XAttribute("pane", "topRight")))));
        ReplacePackageXml(archive, worksheetPath, worksheetXml);
    }

    private static void ApplyWorksheetSheetFormatFixup(ZipArchive archive)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        var worksheetPath = "xl/worksheets/sheet1.xml";
        var worksheetEntry = archive.GetEntry(worksheetPath);
        if (worksheetEntry is null)
            return;

        var worksheetXml = LoadPackageXml(worksheetEntry);
        worksheetXml.Root?.Elements(worksheetNs + "sheetFormatPr").Remove();
        worksheetXml.Root?.AddFirst(new XElement(
            worksheetNs + "sheetFormatPr",
            new XAttribute("baseColWidth", "12"),
            new XAttribute("zeroHeight", "1"),
            new XAttribute("thickTop", "1"),
            new XAttribute("outlineLevelRow", "3"),
            new XElement(
                worksheetNs + "nativeSheetFormatChild",
                new XAttribute("value", "kept"))));
        ReplacePackageXml(archive, worksheetPath, worksheetXml);
    }

    private static void ApplyWorksheetPhoneticPropertiesFixup(ZipArchive archive)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        var worksheetPath = "xl/worksheets/sheet1.xml";
        var worksheetEntry = archive.GetEntry(worksheetPath);
        if (worksheetEntry is null)
            return;

        var worksheetXml = LoadPackageXml(worksheetEntry);
        worksheetXml.Root?.Elements(worksheetNs + "phoneticPr").Remove();
        worksheetXml.Root?.Add(new XElement(
            worksheetNs + "phoneticPr",
            new XAttribute("fontId", "1"),
            new XAttribute("type", "fullwidthKatakana"),
            new XAttribute("alignment", "center"),
            new XAttribute("nativeOnly", "kept")));
        ReplacePackageXml(archive, worksheetPath, worksheetXml);
    }

    private static void ApplyWorksheetSortStateFixup(ZipArchive archive)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        var worksheetPath = "xl/worksheets/sheet1.xml";
        var worksheetEntry = archive.GetEntry(worksheetPath);
        if (worksheetEntry is null)
            return;

        var worksheetXml = LoadPackageXml(worksheetEntry);
        worksheetXml.Root?.Elements(worksheetNs + "autoFilter").Remove();
        worksheetXml.Root?.Elements(worksheetNs + "sortState").Remove();
        worksheetXml.Root?.Add(
            new XElement(
                worksheetNs + "autoFilter",
                new XAttribute("ref", "A1:B3"),
                new XElement(
                    worksheetNs + "filterColumn",
                    new XAttribute("colId", "0"),
                    new XElement(
                        worksheetNs + "filters",
                        new XElement(worksheetNs + "filter", new XAttribute("val", "A"))))),
            new XElement(
                worksheetNs + "sortState",
                new XAttribute("ref", "A1:A3"),
                new XAttribute("caseSensitive", "1"),
                new XAttribute("sortMethod", "stroke"),
                new XAttribute("customSortStateFlag", "keep"),
                new XElement(
                    worksheetNs + "sortCondition",
                    new XAttribute("ref", "A2:A3"),
                    new XAttribute("descending", "1"),
                    new XAttribute("sortBy", "cellColor"),
                    new XAttribute("customSortConditionFlag", "keep"))));
        ReplacePackageXml(archive, worksheetPath, worksheetXml);
    }

    private static void ApplyWorksheetDataConsolidationFixup(ZipArchive archive)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        var worksheetPath = "xl/worksheets/sheet1.xml";
        var worksheetEntry = archive.GetEntry(worksheetPath);
        if (worksheetEntry is null)
            return;

        var worksheetXml = LoadPackageXml(worksheetEntry);
        worksheetXml.Root?.Elements(worksheetNs + "dataConsolidate").Remove();
        worksheetXml.Root?.Add(new XElement(
            worksheetNs + "dataConsolidate",
            new XAttribute("function", "sum"),
            new XAttribute("leftLabels", "1"),
            new XAttribute("topLabels", "1"),
            new XAttribute("link", "1"),
            new XAttribute("customDataConsolidationFlag", "keep"),
            new XElement(
                worksheetNs + "dataRefs",
                new XAttribute("count", "1"),
                new XElement(
                    worksheetNs + "dataRef",
                    new XAttribute("ref", "A1:B2"),
                    new XAttribute("sheet", "Data"),
                    new XAttribute("customDataRefFlag", "keep")))));
        ReplacePackageXml(archive, worksheetPath, worksheetXml);
    }

    private static void ApplyWorksheetCustomPropertiesFixup(ZipArchive archive)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        var worksheetPath = "xl/worksheets/sheet1.xml";
        var worksheetEntry = archive.GetEntry(worksheetPath);
        if (worksheetEntry is null)
            return;

        var worksheetXml = LoadPackageXml(worksheetEntry);
        worksheetXml.Root?.Elements(worksheetNs + "customProperties").Remove();
        worksheetXml.Root?.Add(new XElement(
            worksheetNs + "customProperties",
            new XElement(
                worksheetNs + "customPr",
                new XAttribute("name", "FreexcelNativeProperty"),
                new XAttribute("id", "1"),
                new XAttribute("unsupportedAttr", "kept"))));
        ReplacePackageXml(archive, worksheetPath, worksheetXml);
    }

    private static void ApplyWorksheetSmartTagsFixup(ZipArchive archive)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        var worksheetPath = "xl/worksheets/sheet1.xml";
        var worksheetEntry = archive.GetEntry(worksheetPath);
        if (worksheetEntry is null)
            return;

        var worksheetXml = LoadPackageXml(worksheetEntry);
        worksheetXml.Root?.Elements(worksheetNs + "smartTags").Remove();
        worksheetXml.Root?.Add(new XElement(
            worksheetNs + "smartTags",
            new XElement(
                worksheetNs + "cellSmartTags",
                new XAttribute("r", "A1"),
                new XElement(
                    worksheetNs + "cellSmartTag",
                    new XAttribute("type", "0"),
                    new XAttribute("deleted", "0"),
                    new XElement(
                        worksheetNs + "cellSmartTagPr",
                        new XAttribute("key", "place"),
                        new XAttribute("val", "Seattle"),
                        new XAttribute("customSmartTagPropertyFlag", "keep"))))));
        ReplacePackageXml(archive, worksheetPath, worksheetXml);
    }

    private static void ApplyWorksheetScenariosFixup(ZipArchive archive)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        var worksheetPath = "xl/worksheets/sheet1.xml";
        var worksheetEntry = archive.GetEntry(worksheetPath);
        if (worksheetEntry is null)
            return;

        var worksheetXml = LoadPackageXml(worksheetEntry);
        worksheetXml.Root?.Elements(worksheetNs + "scenarios").Remove();
        worksheetXml.Root?.Add(new XElement(
            worksheetNs + "scenarios",
            new XAttribute("current", "0"),
            new XAttribute("show", "0"),
            new XElement(
                worksheetNs + "scenario",
                new XAttribute("name", "BestCase"),
                new XAttribute("comment", "Scenario comment"),
                new XAttribute("hidden", "1"),
                new XAttribute("locked", "1"),
                new XAttribute("count", "1"),
                new XAttribute("user", "FreexcelTest"),
                new XElement(
                    worksheetNs + "inputCells",
                    new XAttribute("r", "A1"),
                    new XAttribute("val", "42")))));
        ReplacePackageXml(archive, worksheetPath, worksheetXml);
    }

    private static void ApplyWorksheetCustomSheetViewsFixup(ZipArchive archive)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        var worksheetPath = "xl/worksheets/sheet1.xml";
        var worksheetEntry = archive.GetEntry(worksheetPath);
        if (worksheetEntry is null)
            return;

        var worksheetXml = LoadPackageXml(worksheetEntry);
        worksheetXml.Root?.Elements(worksheetNs + "customSheetViews").Remove();
        worksheetXml.Root?.Add(new XElement(
            worksheetNs + "customSheetViews",
            new XElement(
                worksheetNs + "customSheetView",
                new XAttribute("guid", "{11111111-1111-1111-1111-111111111111}"),
                new XAttribute("scale", "120"),
                new XAttribute("showGridLines", "0"),
                new XAttribute("showRowCol", "0"),
                new XAttribute("state", "visible"),
                new XElement(
                    worksheetNs + "pane",
                    new XAttribute("xSplit", "1"),
                    new XAttribute("ySplit", "1"),
                    new XAttribute("topLeftCell", "B2"),
                    new XAttribute("activePane", "bottomRight")))));
        ReplacePackageXml(archive, worksheetPath, worksheetXml);
    }

    private static void EnsureRelationship(XDocument relationshipsXml, string id, string type, string target, string? targetMode = null)
    {
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        relationshipsXml.Root?.Elements(packageRelNs + "Relationship")
            .Where(element => string.Equals(element.Attribute("Id")?.Value, id, StringComparison.OrdinalIgnoreCase))
            .Remove();
        relationshipsXml.Root?.Add(new XElement(
            packageRelNs + "Relationship",
            new XAttribute("Id", id),
            new XAttribute("Type", type),
            new XAttribute("Target", target),
            string.IsNullOrWhiteSpace(targetMode) ? null : new XAttribute("TargetMode", targetMode)));
    }

    private static void EnsureContentTypeOverride(XDocument contentTypes, string partName, string contentType)
    {
        XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
        if (contentTypes.Root?.Elements(contentTypeNs + "Override").Any(element =>
                string.Equals(element.Attribute("PartName")?.Value, partName, StringComparison.OrdinalIgnoreCase)) == true)
        {
            return;
        }

        contentTypes.Root?.Add(new XElement(
            contentTypeNs + "Override",
            new XAttribute("PartName", partName),
            new XAttribute("ContentType", contentType)));
    }

    private static XDocument LoadPackageXml(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    private static void ReplacePackageXml(ZipArchive archive, string entryName, XDocument document)
    {
        archive.GetEntry(entryName)?.Delete();
        var entry = archive.CreateEntry(entryName);
        using var stream = entry.Open();
        document.Save(stream);
    }

    private static Workbook CreateGridBasic()
    {
        var workbook = NewWorkbook("generated-grid-basic-001");
        var sheet = workbook.AddSheet("Grid");
        Set(sheet, "A1", new TextValue("Text"));
        Set(sheet, "B1", new NumberValue(123.45));
        Set(sheet, "C1", new BoolValue(true));
        Set(sheet, "D1", DateTimeValue.FromDateTime(new DateTime(2026, 5, 17, 9, 30, 0)));
        Set(sheet, "E1", ErrorValue.NA);
        Set(sheet, "A3", new TextValue("Sparse corner"));
        Set(sheet, "XFD10", new NumberValue(16384));
        return workbook;
    }

    private static Workbook CreateFormulas()
    {
        var workbook = NewWorkbook("generated-formulas-001");
        var sheet = workbook.AddSheet("Formulas");
        Set(sheet, "A1", new NumberValue(10));
        Set(sheet, "A2", new NumberValue(20));
        Set(sheet, "A3", new NumberValue(30));
        Formula(sheet, "B1", "SUM(A1:A3)");
        Formula(sheet, "B2", "AVERAGE(A1:A3)");
        Formula(sheet, "B3", "IF(B1>50,\"high\",\"low\")");
        Formula(sheet, "B4", "TEXT(DATE(2026,5,17),\"yyyy-mm-dd\")");
        Formula(sheet, "B5", "A1/A2");
        return workbook;
    }

    private static Workbook CreateCrossSheet()
    {
        var workbook = NewWorkbook("generated-cross-sheet-001");
        var input = workbook.AddSheet("Inputs");
        var summary = workbook.AddSheet("Summary");
        Set(input, "A1", new TextValue("North"));
        Set(input, "B1", new NumberValue(100));
        Set(input, "A2", new TextValue("South"));
        Set(input, "B2", new NumberValue(125));
        workbook.DefineNamedRange("SalesValues", Range(input, "B1", "B2"));
        Formula(summary, "A1", "SUM(Inputs!B1:B2)");
        Formula(summary, "A2", "SUM(SalesValues)");
        Formula(summary, "A3", "Inputs!A1");
        return workbook;
    }

    private static Workbook CreateFormatting()
    {
        var workbook = NewWorkbook("generated-formatting-001");
        var sheet = workbook.AddSheet("Formatting");
        var headerStyle = workbook.RegisterStyle(new CellStyle
        {
            Bold = true,
            FontColor = CellColor.White,
            FillColor = new CellColor(31, 78, 121),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            BorderBottom = new CellBorder(BorderStyle.Thick, new CellColor(90, 90, 90))
        });
        var currencyStyle = workbook.RegisterStyle(new CellStyle
        {
            NumberFormat = "$#,##0.00",
            HorizontalAlignment = HorizontalAlignment.Right
        });

        Set(sheet, "A1", new TextValue("Item"), headerStyle);
        Set(sheet, "B1", new TextValue("Amount"), headerStyle);
        Set(sheet, "A2", new TextValue("Revenue"));
        Set(sheet, "B2", new NumberValue(1234.5), currencyStyle);
        Set(sheet, "A4", new TextValue("Wrapped text sample"));
        sheet.GetCell(4, 1)!.StyleId = workbook.RegisterStyle(new CellStyle { WrapText = true, FontName = "Aptos", FontSize = 12 });
        return workbook;
    }

    private static Workbook CreateStructure()
    {
        var workbook = NewWorkbook("generated-structure-001");
        var sheet = workbook.AddSheet("Structure");
        Set(sheet, "A1", new TextValue("Merged heading"));
        Set(sheet, "A3", new TextValue("Visible"));
        Set(sheet, "C3", new TextValue("Hidden markers"));
        sheet.AddMergedRegion(Range(sheet, "A1", "C1"));
        sheet.FrozenRows = 1;
        sheet.FrozenCols = 1;
        sheet.ColumnWidths[1] = 18;
        sheet.ColumnWidths[3] = 22;
        sheet.RowHeights[1] = 28;
        sheet.HiddenRows.Add(5);
        sheet.HiddenCols.Add(4);
        sheet.RowOutlineLevels[6] = 1;
        sheet.ColOutlineLevels[5] = 1;
        return workbook;
    }

    private static Workbook CreateValidation()
    {
        var workbook = NewWorkbook("generated-validation-001");
        var sheet = workbook.AddSheet("Validation");
        Set(sheet, "A1", new TextValue("Choice"));
        Set(sheet, "B1", new TextValue("Quantity"));
        Set(sheet, "A2", new TextValue("Apple"));
        Set(sheet, "B2", new NumberValue(5));
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = Range(sheet, "A2", "A10"),
            Type = DvType.List,
            Formula1 = "Apple,Banana,Cherry",
            ErrorTitle = "Invalid choice",
            ErrorMessage = "Choose a listed item.",
            PromptTitle = "Pick a fruit",
            PromptMessage = "Select Apple, Banana, or Cherry."
        });
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = Range(sheet, "B2", "B10"),
            Type = DvType.WholeNumber,
            Operator = DvOperator.Between,
            Formula1 = "1",
            Formula2 = "10"
        });
        workbook.DefineNamedRange("ValidChoices", Range(sheet, "A2", "A10"));
        return workbook;
    }

    private static Workbook CreateConditionalFormatting()
    {
        var workbook = NewWorkbook("generated-conditional-formatting-001");
        var sheet = workbook.AddSheet("Conditional Formatting");
        for (uint row = 1; row <= 5; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row * 10));

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = Range(sheet, "A1", "A5"),
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "30",
            FormatIfTrue = new CellStyle { FillColor = new CellColor(198, 239, 206), FontColor = new CellColor(0, 97, 0) }
        });
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = Range(sheet, "A1", "A5"),
            Priority = 2,
            RuleType = CfRuleType.Formula,
            FormulaText = "A1>25",
            FormatIfTrue = new CellStyle { FillColor = new CellColor(255, 235, 156), FontColor = new CellColor(156, 87, 0) }
        });
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = Range(sheet, "B1", "B5"),
            Priority = 3,
            RuleType = CfRuleType.Top10,
            TopBottomRank = 3,
            AboveAverage = true
        });
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = Range(sheet, "C1", "C5"),
            Priority = 4,
            RuleType = CfRuleType.ContainsText,
            TextRuleText = "review",
            FormulaText = "NOT(ISERROR(SEARCH(\"review\",C1)))"
        });
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = Range(sheet, "D1", "D5"),
            Priority = 5,
            RuleType = CfRuleType.DuplicateValues
        });
        return workbook;
    }

    private static Workbook CreateColorScales()
    {
        var workbook = NewWorkbook("generated-color-scales-001");
        var sheet = workbook.AddSheet("Color Scales");
        for (uint row = 1; row <= 5; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row * 10));
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = Range(sheet, "A1", "A5"),
            RuleType = CfRuleType.ColorScale,
            UseThreeColorScale = true,
            MinThresholdType = CfThresholdType.Number,
            MinThresholdValue = "0",
            MidThresholdType = CfThresholdType.Percentile,
            MidThresholdValue = "50",
            MaxThresholdType = CfThresholdType.Number,
            MaxThresholdValue = "100"
        });
        return workbook;
    }

    private static Workbook CreateDataBars()
    {
        var workbook = NewWorkbook("generated-data-bars-001");
        var sheet = workbook.AddSheet("Data Bars");
        for (uint row = 1; row <= 5; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row * 10));
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = Range(sheet, "A1", "A5"),
            RuleType = CfRuleType.DataBar,
            DataBarMinThresholdType = CfThresholdType.Number,
            DataBarMinThresholdValue = "0",
            DataBarMaxThresholdType = CfThresholdType.Number,
            DataBarMaxThresholdValue = "100",
            DataBarShowValue = false,
            DataBarMinLength = 5,
            DataBarMaxLength = 95,
            DataBarGradient = false,
            DataBarBorder = true,
            DataBarAxisPosition = "middle",
            DataBarAxisColor = new RgbColor(90, 90, 90),
            DataBarNegativeFillColor = new RgbColor(220, 80, 80),
            DataBarNegativeBorderColor = new RgbColor(160, 40, 40)
        });
        return workbook;
    }

    private static Workbook CreateIconSets()
    {
        var workbook = NewWorkbook("generated-icon-sets-001");
        var sheet = workbook.AddSheet("Icon Sets");
        for (uint row = 1; row <= 5; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row * 20));

        var rule = new ConditionalFormat
        {
            AppliesTo = Range(sheet, "A1", "A5"),
            RuleType = CfRuleType.IconSet,
            IconSetStyle = "5Arrows",
            IconSetShowValue = false,
            IconSetReverse = true
        };
        rule.IconSetThresholds.AddRange(
        [
            new CfThresholdModel(CfThresholdType.Number, "0"),
            new CfThresholdModel(CfThresholdType.Percent, "25"),
            new CfThresholdModel(CfThresholdType.Percent, "50"),
            new CfThresholdModel(CfThresholdType.Percent, "75")
        ]);
        sheet.ConditionalFormats.Add(rule);
        return workbook;
    }

    private static Workbook CreateImagesAndSparklines()
    {
        var workbook = NewWorkbook("generated-images-sparklines-001");
        var sheet = workbook.AddSheet("Images Sparklines");
        Set(sheet, "A1", new NumberValue(1));
        Set(sheet, "B1", new NumberValue(2));
        Set(sheet, "C1", new NumberValue(3));
        sheet.BackgroundImage = new WorksheetBackgroundImage(MinimalPngBytes(), "image/png", "corpus-background.png");
        sheet.Pictures.Add(new PictureModel
        {
            Name = "Corpus Image 1",
            Anchor = Addr(sheet, "E2"),
            Kind = PictureKind.Image,
            ImageBytes = MinimalPngBytes(),
            ContentType = "image/png",
            Width = 120,
            Height = 80,
            CropLeft = 0.05,
            CropTop = 0.10,
            CropRight = 0.05,
            CropBottom = 0.10,
            Title = "Corpus image title",
            AltText = "Corpus image"
        });
        sheet.Sparklines.Add(new SparklineModel
        {
            DataRange = Range(sheet, "A1", "C1"),
            Location = Addr(sheet, "D1"),
            Kind = SparklineKind.Line
        });
        return workbook;
    }

    private static Workbook CreateTextBoxesAndShapes()
    {
        var workbook = NewWorkbook("generated-text-boxes-shapes-001");
        var sheet = workbook.AddSheet("Text Shapes");
        Set(sheet, "A1", new TextValue("Drawing objects"));
        sheet.TextBoxes.Add(new TextBoxModel
        {
            Name = "Corpus Text Box 1",
            Anchor = Addr(sheet, "B2"),
            Text = "Corpus note",
            Width = 200,
            Height = 90,
            FillThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1, 0.25),
            OutlineThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2, -0.25),
            Title = "Corpus text box title",
            AltText = "Corpus text box"
        });
        sheet.DrawingShapes.Add(new DrawingShapeModel
        {
            Name = "Corpus Ellipse 1",
            Anchor = Addr(sheet, "D5"),
            Kind = DrawingShapeKind.Ellipse,
            Width = 140,
            Height = 90,
            FillColor = new CellColor(221, 235, 247),
            GradientFillEndColor = new CellColor(189, 215, 238),
            OutlineThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent4, -0.5),
            HasShadowEffect = true,
            Title = "Corpus ellipse title",
            AltText = "Corpus ellipse"
        });
        return workbook;
    }

    private static Workbook CreateCommentsAndHyperlinks()
    {
        var workbook = NewWorkbook("generated-comments-hyperlinks-002");
        var sheet = workbook.AddSheet("Links Notes");
        var hyperlinkStyle = RegisterHyperlinkStyle(workbook);
        Set(sheet, "A1", new TextValue("Documentation"));
        Set(sheet, "A2", new TextValue("Release notes"));
        Set(sheet, "B1", new TextValue("Review"));
        Set(sheet, "B2", new TextValue("Follow-up"));
        sheet.Hyperlinks[Addr(sheet, "A1")] = "https://example.com/freexcel/docs";
        sheet.GetCell(Addr(sheet, "A1"))!.StyleId = hyperlinkStyle;
        sheet.HyperlinkMetadata[Addr(sheet, "A1")] = new HyperlinkMetadata(
            HyperlinkTargetKind.ExistingFileOrWebPage,
            "Open the Freexcel documentation");
        sheet.Hyperlinks[Addr(sheet, "A2")] = "mailto:review@example.com";
        sheet.GetCell(Addr(sheet, "A2"))!.StyleId = hyperlinkStyle;
        sheet.HyperlinkMetadata[Addr(sheet, "A2")] = new HyperlinkMetadata(
            HyperlinkTargetKind.EmailAddress,
            "Send a workbook review note");
        sheet.Hyperlinks[Addr(sheet, "B2")] = "Links Notes!A1";
        sheet.GetCell(Addr(sheet, "B2"))!.StyleId = hyperlinkStyle;
        sheet.HyperlinkMetadata[Addr(sheet, "B2")] = new HyperlinkMetadata(
            HyperlinkTargetKind.PlaceInThisDocument,
            "Jump to the documentation link",
            "Links Notes!A1");
        sheet.Comments[Addr(sheet, "B1")] = "Check workbook fidelity notes.";
        sheet.Comments[Addr(sheet, "B2")] = "Confirm links survived round-trip.";
        return workbook;
    }

    private static Workbook CreateMergedFreeze()
    {
        var workbook = NewWorkbook("generated-merged-freeze-002");
        var sheet = workbook.AddSheet("Merged Freeze");
        Set(sheet, "A1", new TextValue("Regional summary"));
        Set(sheet, "A3", new TextValue("North"));
        Set(sheet, "B3", new NumberValue(120));
        Set(sheet, "A4", new TextValue("South"));
        Set(sheet, "B4", new NumberValue(145));
        sheet.AddMergedRegion(Range(sheet, "A1", "D1"));
        sheet.AddMergedRegion(Range(sheet, "C3", "D4"));
        sheet.FrozenRows = 2;
        sheet.FrozenCols = 1;
        sheet.HiddenRows.Add(8);
        sheet.HiddenCols.Add(6);
        sheet.ColumnWidths[1] = 20;
        sheet.RowHeights[1] = 30;
        return workbook;
    }

    private static Workbook CreatePrintTitlesAndBreaks()
    {
        var workbook = NewWorkbook("generated-print-titles-breaks-001");
        var sheet = workbook.AddSheet("Print Setup");
        Set(sheet, "A1", new TextValue("Region"));
        Set(sheet, "B1", new TextValue("Amount"));
        Set(sheet, "A2", new TextValue("North"));
        Set(sheet, "B2", new NumberValue(100));
        Set(sheet, "A25", new TextValue("South"));
        Set(sheet, "B25", new NumberValue(125));
        sheet.PrintArea = Range(sheet, "A1", "D40");
        sheet.PrintTitleRows = new WorksheetRepeatRange(1, 1);
        sheet.PrintTitleColumns = new WorksheetRepeatRange(1, 1);
        sheet.PageOrientation = WorksheetPageOrientation.Landscape;
        sheet.PaperSize = WorksheetPaperSize.Letter;
        sheet.PageMargins = WorksheetPageMargins.Narrow;
        sheet.ScaleToFit = new WorksheetScaleToFit(null, 1, 1);
        sheet.PrintGridlines = true;
        sheet.PrintHeadings = true;
        sheet.PageHeader = new WorksheetHeaderFooter("Freexcel", "Print setup", "Corpus");
        sheet.PageFooter = new WorksheetHeaderFooter("", "Page &P of &N", "");
        sheet.RowPageBreaks.Add(20);
        sheet.ColumnPageBreaks.Add(4);
        return workbook;
    }

    private static Workbook CreateNamedRangesAndFormulas()
    {
        var workbook = NewWorkbook("generated-named-ranges-formulas-002");
        var inputs = workbook.AddSheet("Inputs");
        var summary = workbook.AddSheet("Summary");
        Set(inputs, "A1", new TextValue("North"));
        Set(inputs, "B1", new NumberValue(100));
        Set(inputs, "A2", new TextValue("South"));
        Set(inputs, "B2", new NumberValue(125));
        Set(inputs, "A3", new TextValue("West"));
        Set(inputs, "B3", new NumberValue(90));
        workbook.DefineNamedRange("RevenueValues", Range(inputs, "B1", "B3"));
        workbook.DefineNamedRange("RegionLabels", Range(inputs, "A1", "A3"));
        Formula(summary, "A1", "SUM(RevenueValues)");
        Formula(summary, "A2", "AVERAGE(Inputs!B1:B3)");
        Formula(summary, "A3", "INDEX(RegionLabels,2)");
        return workbook;
    }

    private static Workbook CreateValidationCustom()
    {
        var workbook = NewWorkbook("generated-validation-custom-002");
        var sheet = workbook.AddSheet("Validation Custom");
        Set(sheet, "A1", new TextValue("Allowed"));
        Set(sheet, "A2", new TextValue("Open"));
        Set(sheet, "A3", new TextValue("Closed"));
        Set(sheet, "B1", new TextValue("Status"));
        Set(sheet, "C1", new TextValue("Ratio"));
        workbook.DefineNamedRange("StatusChoices", Range(sheet, "A2", "A3"));
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = Range(sheet, "B2", "B20"),
            Type = DvType.List,
            Formula1 = "StatusChoices"
        });
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = Range(sheet, "C2", "C20"),
            Type = DvType.Decimal,
            Operator = DvOperator.Between,
            Formula1 = "0",
            Formula2 = "1"
        });
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = Range(sheet, "D2", "D20"),
            Type = DvType.Custom,
            Formula1 = "LEN(D2)<=12"
        });
        return workbook;
    }

    private static Workbook CreateStyleOnlyCells()
    {
        var workbook = NewWorkbook("generated-style-only-cells-002");
        var sheet = workbook.AddSheet("Style Only");
        var warningStyle = workbook.RegisterStyle(new CellStyle
        {
            FillColor = new CellColor(255, 242, 204),
            FontColor = new CellColor(156, 87, 0),
            BorderBottom = new CellBorder(BorderStyle.Thin, new CellColor(191, 143, 0))
        });
        var percentStyle = workbook.RegisterStyle(new CellStyle { NumberFormat = "0.00%" });
        sheet.SetStyleOnly(4, 4, warningStyle);
        sheet.SetStyleOnly(5, 4, warningStyle);
        Set(sheet, "A1", new TextValue("Completion"));
        Set(sheet, "B1", new NumberValue(0.875), percentStyle);
        Set(sheet, "A2", new TextValue("Empty styled cells at D4:D5"));
        return workbook;
    }

    private static Workbook CreateChartsCombo()
    {
        var workbook = NewWorkbook("generated-charts-combo-002");
        var sheet = workbook.AddSheet("Chart Mix");
        Set(sheet, "A1", new TextValue("Quarter"));
        Set(sheet, "B1", new TextValue("Revenue"));
        Set(sheet, "C1", new TextValue("Cost"));
        Set(sheet, "A2", new TextValue("Q1"));
        Set(sheet, "A3", new TextValue("Q2"));
        Set(sheet, "A4", new TextValue("Q3"));
        Set(sheet, "A5", new TextValue("Q4"));
        Set(sheet, "B2", new NumberValue(120));
        Set(sheet, "B3", new NumberValue(135));
        Set(sheet, "B4", new NumberValue(150));
        Set(sheet, "B5", new NumberValue(170));
        Set(sheet, "C2", new NumberValue(80));
        Set(sheet, "C3", new NumberValue(92));
        Set(sheet, "C4", new NumberValue(98));
        Set(sheet, "C5", new NumberValue(110));
        sheet.Charts.Add(new ChartModel { Type = ChartType.Line, DataRange = Range(sheet, "A1", "C5"), Title = "Trend", ShowLegend = true });
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Bar,
            DataRange = Range(sheet, "A1", "C5"),
            Title = "Bar View",
            ShowLegend = true,
            BarGapWidth = 75,
            BarOverlap = -20,
            VaryColorsByPoint = true
        });
        sheet.Charts.Add(new ChartModel { Type = ChartType.Area, DataRange = Range(sheet, "A1", "C5"), Title = "Area View", ShowLegend = true });
        return workbook;
    }

    private static Workbook CreatePivotsWithFilters()
    {
        var workbook = NewWorkbook("generated-pivots-filters-002");
        workbook.NumberFormatCatalog[165] = "#,##0.0 \"kg\"";
        var sheet = workbook.AddSheet("Pivot Filters");
        Set(sheet, "A1", new TextValue("Region"));
        Set(sheet, "B1", new TextValue("Category"));
        Set(sheet, "C1", new TextValue("Amount"));
        Set(sheet, "A2", new TextValue("North"));
        Set(sheet, "B2", new TextValue("Hardware"));
        Set(sheet, "C2", new NumberValue(100));
        Set(sheet, "A3", new TextValue("South"));
        Set(sheet, "B3", new TextValue("Software"));
        Set(sheet, "C3", new NumberValue(125));
        Set(sheet, "A4", new TextValue("North"));
        Set(sheet, "B4", new TextValue("Services"));
        Set(sheet, "C4", new NumberValue(80));
        Set(sheet, "A7", new TextValue("Region"));
        Set(sheet, "B7", new TextValue("Sum of Amount"));
        Set(sheet, "A8", new TextValue("North"));
        Set(sheet, "B8", new NumberValue(180));
        Set(sheet, "A9", new TextValue("Grand Total"));
        Set(sheet, "B9", new NumberValue(180));

        var cache = new PivotCacheModel
        {
            CacheId = 2,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = sheet.Name,
            SourceReference = "A1:C4",
            PackagePart = "xl/pivotCache/pivotCacheDefinition2.xml",
            RefreshOnLoad = true,
            PreserveSourceSortFilter = false,
            RecordCount = 3,
            CreatedVersion = 8,
            MinRefreshableVersion = 4,
            RefreshedVersion = 8,
            RefreshedBy = "Freexcel Corpus",
            RefreshedDateIso = "2026-05-24T12:34:56Z"
        };
        cache.Fields.Add(new PivotCacheFieldModel("Region", ContainsString: true, SharedItems: ["North", "South"]));
        cache.Fields.Add(new PivotCacheFieldModel("Category", ContainsString: true, SharedItems: ["Hardware", "Software", "Services"]));
        cache.Fields.Add(new PivotCacheFieldModel("Amount", 165, ContainsNumber: true, MinValue: 80, MaxValue: 125));
        workbook.PivotCaches.Add(cache);

        var style = new PivotTableStyleModel { Name = "FreexcelCorpusFilteredPivotStyle", AppliesToPivotTables = true };
        style.Elements.Add(new PivotTableStyleElementModel("wholeTable", 0));
        style.Elements.Add(new PivotTableStyleElementModel("headerRow", 1));
        workbook.PivotTableStyles.Add(style);

        var pivot = new PivotTableModel
        {
            Name = "PivotTableFiltered",
            CacheId = 2,
            SourceRange = Range(sheet, "A1", "C4"),
            TargetRange = Range(sheet, "A7", "B9"),
            PackagePart = "xl/pivotTables/pivotTable2.xml",
            StyleName = style.Name,
            ShowRowStripes = true,
            RepeatItemLabels = false,
            EnableDrill = false,
            AsteriskTotals = true,
            MultipleFieldFilters = false,
            EnableFieldDialog = false,
            EnableFieldProperties = false,
            EnableDataValueEditing = true,
            ApplyNumberFormats = false,
            ApplyBorderFormats = false,
            ApplyFontFormats = false,
            ApplyPatternFormats = false,
            DataCaption = "Corpus Values",
            GrandTotalCaption = "Corpus Grand Total",
            MissingCaption = "(corpus missing)",
            ErrorCaption = "(corpus error)"
        };
        pivot.PageFields.Add(new PivotFieldModel(1, SelectedItem: "Hardware"));
        pivot.RowFields.Add(new PivotFieldModel(0, SelectedItems: ["North"]));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum", 165, null, PivotShowValuesAs.None, null, null, "#,##0.0 \"kg\""));
        sheet.PivotTables.Add(pivot);
        return workbook;
    }

    private static Workbook CreateStructuredTableTotals()
    {
        var workbook = NewWorkbook("generated-structured-table-totals-002");
        var sheet = workbook.AddSheet("Table Totals");
        Set(sheet, "A1", new TextValue("Item"));
        Set(sheet, "B1", new TextValue("Amount"));
        Set(sheet, "A2", new TextValue("A"));
        Set(sheet, "B2", new NumberValue(10));
        Set(sheet, "A3", new TextValue("B"));
        Set(sheet, "B3", new NumberValue(20));
        Set(sheet, "A4", new TextValue("Total"));
        Set(sheet, "B4", new NumberValue(30));

        var table = new StructuredTableModel
        {
            Id = 2,
            Name = "SalesTotals",
            DisplayName = "SalesTotals",
            Range = Range(sheet, "A1", "B4"),
            HasAutoFilter = true,
            TotalsRowShown = true,
            StyleName = "TableStyleMedium9",
            ShowRowStripes = true,
            ShowFirstColumn = true,
            PackagePart = "xl/tables/table2.xml"
        };
        table.Columns.Add(new StructuredTableColumnModel(1, "Item", TotalsRowLabel: "Total"));
        table.Columns.Add(new StructuredTableColumnModel(2, "Amount", TotalsRowFunction: "sum"));
        table.FilterColumns.Add(new StructuredTableFilterColumnModel(0, ["A", "B"]));
        sheet.StructuredTables.Add(table);
        return workbook;
    }

    private static Workbook CreateImagesAndSparklinesVariant()
    {
        var workbook = NewWorkbook("generated-images-sparklines-002");
        var sheet = workbook.AddSheet("Visual Data");
        Set(sheet, "A1", new NumberValue(5));
        Set(sheet, "B1", new NumberValue(7));
        Set(sheet, "C1", new NumberValue(9));
        Set(sheet, "A2", new NumberValue(3));
        Set(sheet, "B2", new NumberValue(4));
        Set(sheet, "C2", new NumberValue(8));
        sheet.Pictures.Add(new PictureModel
        {
            Name = "Additional Corpus Image 1",
            Anchor = Addr(sheet, "F2"),
            Kind = PictureKind.Image,
            ImageBytes = MinimalPngBytes(),
            ContentType = "image/png",
            Width = 80,
            Height = 80,
            Title = "Additional corpus image title",
            AltText = "Additional corpus image"
        });
        sheet.Sparklines.Add(new SparklineModel { DataRange = Range(sheet, "A1", "C1"), Location = Addr(sheet, "D1"), Kind = SparklineKind.Line });
        sheet.Sparklines.Add(new SparklineModel { DataRange = Range(sheet, "A2", "C2"), Location = Addr(sheet, "D2"), Kind = SparklineKind.Column });
        return workbook;
    }

    private static Workbook CreateObjects()
    {
        var workbook = NewWorkbook("generated-objects-001");
        var sheet = workbook.AddSheet("Objects");
        var hyperlinkStyle = RegisterHyperlinkStyle(workbook);
        Set(sheet, "A1", new TextValue("Documentation"));
        Set(sheet, "B1", new TextValue("Review note"));
        sheet.Hyperlinks[Addr(sheet, "A1")] = "https://example.com/freexcel";
        sheet.GetCell(Addr(sheet, "A1"))!.StyleId = hyperlinkStyle;
        sheet.Comments[Addr(sheet, "B1")] = "Round-trip comment fixture";
        return workbook;
    }

    private static Workbook CreateCharts()
    {
        var workbook = NewWorkbook("generated-charts-001");
        var sheet = workbook.AddSheet("Charts");
        Set(sheet, "A1", new TextValue("Month"));
        Set(sheet, "B1", new TextValue("Sales"));
        Set(sheet, "C1", new TextValue("Margin"));
        Set(sheet, "D1", new TextValue("Open"));
        Set(sheet, "E1", new TextValue("High"));
        Set(sheet, "F1", new TextValue("Low"));
        Set(sheet, "G1", new TextValue("Close"));
        Set(sheet, "I1", new TextValue("Date"));
        Set(sheet, "J1", new TextValue("Volume"));
        Set(sheet, "K1", new TextValue("Open"));
        Set(sheet, "L1", new TextValue("High"));
        Set(sheet, "M1", new TextValue("Low"));
        Set(sheet, "N1", new TextValue("Close"));
        Set(sheet, "A2", new TextValue("Jan"));
        Set(sheet, "A3", new TextValue("Feb"));
        Set(sheet, "A4", new TextValue("Mar"));
        Set(sheet, "I2", new TextValue("2026-01-02"));
        Set(sheet, "I3", new TextValue("2026-01-05"));
        Set(sheet, "I4", new TextValue("2026-01-06"));
        Set(sheet, "B2", new NumberValue(100));
        Set(sheet, "B3", new NumberValue(120));
        Set(sheet, "B4", new NumberValue(140));
        Set(sheet, "C2", new NumberValue(0.2));
        Set(sheet, "C3", new NumberValue(0.25));
        Set(sheet, "C4", new NumberValue(0.3));
        Set(sheet, "D2", new NumberValue(101));
        Set(sheet, "D3", new NumberValue(121));
        Set(sheet, "D4", new NumberValue(139));
        Set(sheet, "E2", new NumberValue(108));
        Set(sheet, "E3", new NumberValue(128));
        Set(sheet, "E4", new NumberValue(145));
        Set(sheet, "F2", new NumberValue(98));
        Set(sheet, "F3", new NumberValue(118));
        Set(sheet, "F4", new NumberValue(135));
        Set(sheet, "G2", new NumberValue(106));
        Set(sheet, "G3", new NumberValue(126));
        Set(sheet, "G4", new NumberValue(142));
        Set(sheet, "J2", new NumberValue(1000));
        Set(sheet, "J3", new NumberValue(1200));
        Set(sheet, "J4", new NumberValue(1400));
        Set(sheet, "K2", new NumberValue(101));
        Set(sheet, "K3", new NumberValue(121));
        Set(sheet, "K4", new NumberValue(139));
        Set(sheet, "L2", new NumberValue(108));
        Set(sheet, "L3", new NumberValue(128));
        Set(sheet, "L4", new NumberValue(145));
        Set(sheet, "M2", new NumberValue(98));
        Set(sheet, "M3", new NumberValue(118));
        Set(sheet, "M4", new NumberValue(135));
        Set(sheet, "N2", new NumberValue(106));
        Set(sheet, "N3", new NumberValue(126));
        Set(sheet, "N4", new NumberValue(142));
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = Range(sheet, "A1", "C4"),
            Title = "Sales by Month",
            XAxisTitle = "Month",
            YAxisTitle = "Sales",
            ChartTitleTextThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1, 0.2),
            ChartTitleFontSize = 18,
            AxisTitleTextThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2, -0.25),
            AxisTitleFontSize = 12.5,
            ChartAreaFillColor = new CellColor(250, 250, 250),
            PlotAreaFillColor = new CellColor(242, 242, 242),
            PlotAreaBorderColor = new CellColor(191, 191, 191),
            PlotAreaBorderThickness = 1.25,
            ChartStyleId = 42,
            RoundedCorners = true,
            BlankDisplayMode = ChartBlankDisplayMode.Zero,
            ShowDataInHiddenRowsAndColumns = true,
            ShowLegend = true,
            ShowDataLabels = true,
            DataLabelPosition = ChartDataLabelPosition.OutsideEnd,
            DataLabelSeparator = ChartDataLabelSeparator.Semicolon,
            DataLabelNumberFormat = ChartDataLabelNumberFormat.Currency,
            ShowDataLabelCallouts = true,
            DataLabelFillColor = new CellColor(255, 255, 225),
            DataLabelBorderColor = new CellColor(128, 128, 128),
            DataLabelTextColor = new CellColor(30, 30, 30),
            DataLabelBorderThickness = 1.5,
            DataLabelFontSize = 13,
            DataLabelAngle = -35,
            ShowLinearTrendline = true,
            TrendlineType = ChartTrendlineType.Polynomial,
            TrendlineOrder = 3,
            ShowTrendlineEquation = true,
            ShowTrendlineRSquared = true,
            TrendlineThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent6),
            TrendlineThickness = 2.25,
            TrendlineDashStyle = ChartLineDashStyle.Dot,
            ShowErrorBars = true,
            ErrorBarKind = ChartErrorBarKind.Percentage,
            ErrorBarDirection = ChartErrorBarDirection.Plus,
            ErrorBarValue = 12.5,
            ErrorBarEndCaps = false,
            ErrorBarThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2),
            ErrorBarThickness = 2,
            ErrorBarDashStyle = ChartLineDashStyle.Dash,
            LegendPosition = ChartLegendPosition.Bottom,
            LegendTextColor = new CellColor(64, 64, 64),
            LegendFillColor = new CellColor(255, 255, 255),
            LegendBorderColor = new CellColor(166, 166, 166),
            LegendBorderThickness = 1,
            LegendFontSize = 10.5,
            ShowXAxisMajorGridlines = true,
            XAxisMajorGridlineColor = new CellColor(217, 217, 217),
            XAxisGridlineThickness = 0.75,
            XAxisMajorTickStyle = ChartAxisTickStyle.Cross,
            XAxisMinorTickStyle = ChartAxisTickStyle.Inside,
            XAxisLineColor = new CellColor(128, 128, 128),
            XAxisLineThickness = 1,
            XAxisLabelTextThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Dark1),
            XAxisLabelFontSize = 10,
            XAxisLabelAngle = -45,
            YAxisMinimum = 0,
            YAxisMaximum = 200,
            YAxisMajorUnit = 50,
            YAxisMinorUnit = 25,
            YAxisNumberFormat = ChartDataLabelNumberFormat.Currency,
            ShowYAxisMajorGridlines = true,
            YAxisMajorGridlineColor = new CellColor(217, 217, 217),
            YAxisGridlineThickness = 0.75,
            YAxisMajorTickStyle = ChartAxisTickStyle.Outside,
            YAxisMinorTickStyle = ChartAxisTickStyle.Inside,
            YAxisLineColor = new CellColor(128, 128, 128),
            YAxisLineThickness = 1,
            YAxisLabelTextThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Light2, 0.1),
            YAxisLabelFontSize = 12,
            YAxisLabelAngle = 90,
            Uses1904DateSystem = true,
            Language = "en-US",
            ShowDataLabelsOverMaximum = true,
            AutoTitleDeleted = true,
            ColorMapOverride = new ChartColorMapOverrideModel
            {
                UseMasterColorMapping = false,
                OverrideMappings = { ["accent1"] = "accent2" }
            },
            ExternalData = new ChartExternalDataModel
            {
                RelationshipId = "rIdExternalData1",
                RelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/package",
                Target = "../externalLinks/externalLink1.xml",
                TargetMode = "External",
                AutoUpdate = true
            },
            PlotAreaLayout = new ChartManualLayoutModel
            {
                LayoutTarget = "inner",
                XMode = "factor",
                YMode = "factor",
                WidthMode = "factor",
                HeightMode = "factor",
                X = 0.1,
                Y = 0.2,
                Width = 0.8,
                Height = 0.6
            },
            LegendLayout = new ChartManualLayoutModel
            {
                LayoutTarget = "inner",
                X = 0.72,
                Y = 0.1,
                Height = 0.7
            },
            DataTable = new ChartDataTableModel
            {
                ShowHorizontalBorder = true,
                ShowVerticalBorder = true,
                ShowOutline = true,
                ShowLegendKeys = true
            },
            SeriesFormats = [new ChartSeriesFormat(0, FillColor: new CellColor(68, 114, 196), Smooth: true, InvertIfNegative: true)]
        });
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.StackedColumn,
            DataRange = Range(sheet, "A1", "C4"),
            Title = "Stacked With Series Lines",
            ShowSeriesLines = true,
            SeriesLineThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent5),
            SeriesLineThickness = 1.5,
            SeriesLineDashStyle = ChartLineDashStyle.Dot
        });
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Radar,
            DataRange = Range(sheet, "A1", "C4"),
            Title = "Radar View",
            ShowLegend = true
        });
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Stock,
            StockSubtype = StockChartSubtype.VolumeOpenHighLowClose,
            DataRange = Range(sheet, "I1", "N4"),
            Title = "Stock View",
            ShowLegend = true,
            ShowHighLowLines = true,
            HighLowLineThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent4),
            HighLowLineThickness = 2,
            HighLowLineDashStyle = ChartLineDashStyle.Dash,
            ShowUpDownBars = true,
            UpDownBarGapWidth = 180,
            UpBarFillColor = new CellColor(112, 173, 71),
            UpBarBorderColor = new CellColor(84, 130, 53),
            UpBarBorderThickness = 1,
            DownBarFillColor = new CellColor(192, 0, 0),
            DownBarBorderThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2),
            DownBarBorderThickness = 2
        });
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Surface,
            DataRange = Range(sheet, "A1", "C4"),
            Title = "Surface View",
            ShowLegend = true
        });
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.ThreeDSurface,
            DataRange = Range(sheet, "A1", "C4"),
            Title = "3D Surface View",
            ShowLegend = true,
            ThreeDView = new Chart3DViewModel
            {
                RotationX = 20,
                HeightPercent = 80,
                RotationY = 30,
                DepthPercent = 150,
                RightAngleAxes = false,
                Perspective = 30
            },
            FloorFormat = new ChartSurfaceFormatModel
            {
                FillColor = new CellColor(217, 234, 211),
                BorderThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent6),
                BorderThickness = 1
            },
            SideWallFormat = new ChartSurfaceFormatModel
            {
                FillThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2),
                BorderColor = new CellColor(192, 0, 0),
                BorderThickness = 2
            },
            BackWallFormat = new ChartSurfaceFormatModel
            {
                FillColor = new CellColor(217, 225, 242),
                BorderColor = new CellColor(68, 114, 196),
                BorderThickness = 3
            }
        });
        return workbook;
    }

    private static Workbook CreateStructuredTables()
    {
        var workbook = NewWorkbook("generated-structured-tables-001");
        var sheet = workbook.AddSheet("Tables");
        Set(sheet, "A1", new TextValue("Category"));
        Set(sheet, "B1", new TextValue("Amount"));
        Set(sheet, "A2", new TextValue("A"));
        Set(sheet, "B2", new NumberValue(10));
        Set(sheet, "A3", new TextValue("B"));
        Set(sheet, "B3", new NumberValue(20));

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = Range(sheet, "A1", "B3"),
            HasAutoFilter = true,
            TotalsRowShown = false,
            StyleName = "TableStyleMedium2",
            ShowRowStripes = true,
            NativeSortStateXml = """<sortState xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" ref="A2:B3"><sortCondition ref="B2:B3" descending="1" /></sortState>""",
            PackagePart = "xl/tables/table1.xml"
        };
        table.Columns.Add(new StructuredTableColumnModel(1, "Category"));
        table.Columns.Add(new StructuredTableColumnModel(2, "Amount"));
        table.FilterColumns.Add(new StructuredTableFilterColumnModel(
            0,
            [],
            IncludeBlank: false,
            NativeFilterXmls:
            [
                """<customFilters xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><customFilter operator="greaterThan" val="15" /></customFilters>"""
            ]));
        sheet.FilterHiddenRows.Add(2);
        sheet.FilterHiddenRows.Add(3);
        sheet.StructuredTables.Add(table);
        return workbook;
    }

    private static Workbook CreatePivots()
    {
        var workbook = NewWorkbook("generated-pivots-001");
        var sheet = workbook.AddSheet("Pivot Data");
        Set(sheet, "A1", new TextValue("Category"));
        Set(sheet, "B1", new TextValue("Amount"));
        Set(sheet, "A2", new TextValue("A"));
        Set(sheet, "B2", new NumberValue(10));
        Set(sheet, "A3", new TextValue("B"));
        Set(sheet, "B3", new NumberValue(20));
        Set(sheet, "A5", new TextValue("Category"));
        Set(sheet, "B5", new TextValue("Sum of Amount"));
        Set(sheet, "A6", new TextValue("A"));
        Set(sheet, "B6", new NumberValue(10));
        Set(sheet, "A7", new TextValue("B"));
        Set(sheet, "B7", new NumberValue(20));
        Set(sheet, "A8", new TextValue("Grand Total"));
        Set(sheet, "B8", new NumberValue(30));

        var cache = new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = sheet.Name,
            SourceReference = "A1:B3",
            PackagePart = "xl/pivotCache/pivotCacheDefinition1.xml",
            RecordCount = 2,
            CreatedVersion = 8,
            MinRefreshableVersion = 4
        };
        cache.Fields.Add(new PivotCacheFieldModel("Category"));
        cache.Fields.Add(new PivotCacheFieldModel("Amount", 4));
        workbook.PivotCaches.Add(cache);
        var style = new PivotTableStyleModel
        {
            Name = "FreexcelCorpusPivotStyle",
            AppliesToPivotTables = true,
            AppliesToTables = false
        };
        style.Elements.Add(new PivotTableStyleElementModel("wholeTable", 0));
        style.Elements.Add(new PivotTableStyleElementModel("firstRowStripe", 1, 1));
        workbook.PivotTableStyles.Add(style);

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B3"),
            TargetRange = Range(sheet, "A5", "B8"),
            PackagePart = "xl/pivotTables/pivotTable1.xml",
            StyleName = "FreexcelCorpusPivotStyle",
            ShowRowStripes = true,
            ShowContextualTooltips = false,
            ShowPropertiesInTooltips = false,
            ShowClassicLayout = true,
            MergeAndCenterLabels = true,
            AltTextTitle = "Corpus pivot",
            AltTextDescription = "Generated PivotTable parity fixture"
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum", 4));
        sheet.PivotTables.Add(pivot);
        return workbook;
    }

    private static Workbook CreateProtectionAndPageSetup()
    {
        var workbook = NewWorkbook("generated-protection-page-setup-001");
        workbook.CalculationMode = WorkbookCalculationMode.Manual;
        workbook.FullCalculationOnLoad = true;
        workbook.ForceFullCalculation = true;
        workbook.IterativeCalculation = true;
        workbook.MaxCalculationIterations = 25;
        workbook.MaxCalculationChange = 0.005;
        workbook.Theme = WorkbookTheme.Office
            .WithName("Freexcel Corpus Theme")
            .WithFonts("Aptos Display", "Aptos")
            .WithEffects("FreexcelEffects")
            .WithColor(WorkbookThemeColorSlot.Accent1, new CellColor(12, 34, 56))
            .WithColor(WorkbookThemeColorSlot.Hyperlink, new CellColor(1, 99, 193));
        var sheet = workbook.AddSheet("Print");
        Set(sheet, "A1", new TextValue("Protected print fixture"));
        Set(sheet, "A2", new NumberValue(42));
        sheet.DefaultColumnWidth = 11;
        sheet.DefaultRowHeight = 22;
        sheet.ColumnWidths[1] = 18;
        sheet.RowHeights[2] = 28;
        sheet.TabColor = new CellColor(0, 176, 80);
        sheet.CodeName = "PrintSheet";
        sheet.FullCalculationOnLoad = true;
        sheet.PhoneticProperties = new WorksheetPhoneticProperties("1", "fullwidthKatakana", "center");
        sheet.CustomProperties.Add(new WorksheetCustomProperty("FreexcelCorpusSheet", 7));
        sheet.IsProtected = true;
        sheet.ProtectionPassword = "fixture";
        sheet.AllowEditRanges.Add(Range(sheet, "A2", "B5"));
        workbook.IsStructureProtected = true;
        workbook.StructureProtectionPassword = "structure";
        sheet.PrintArea = Range(sheet, "A1", "C20");
        sheet.PageOrientation = WorksheetPageOrientation.Landscape;
        sheet.PaperSize = WorksheetPaperSize.Letter;
        sheet.PageMargins = WorksheetPageMargins.Narrow;
        sheet.ScaleToFit = new WorksheetScaleToFit(null, 1, 1);
        sheet.PrintGridlines = true;
        sheet.PrintHeadings = true;
        sheet.PageHeader = new WorksheetHeaderFooter("Freexcel &[Picture]", "Corpus", "2026");
        sheet.PageHeaderPictures = new WorksheetHeaderFooterPictureSet(
            new WorksheetHeaderFooterPicture(MinimalPngBytes(), "image/png", "header-logo.png", 96, 32),
            null,
            null);
        sheet.PageFooter = new WorksheetHeaderFooter("", "Page &P", "");
        sheet.ViewMode = WorksheetViewMode.PageBreakPreview;
        sheet.ViewTopRow = 4;
        sheet.ViewLeftCol = 2;
        sheet.ActiveRow = 6;
        sheet.ActiveCol = 3;
        workbook.WatchedCells.Add(Addr(sheet, "A2"));
        workbook.Scenarios.Add(new WorkbookScenario(
            "Print Forecast",
            [
                new ScenarioCellValue(Addr(sheet, "A2"), new NumberValue(84)),
                new ScenarioCellValue(Addr(sheet, "B2"), new TextValue("Scenario"))
            ]));
        workbook.CustomViews.Add(new WorkbookCustomView(
            "Print Review",
            [
                new WorksheetCustomViewState(
                    sheet.Name,
                    WorksheetViewMode.PageLayout,
                    sheet.FrozenRows,
                    sheet.FrozenCols,
                    sheet.SplitRow,
                    sheet.SplitColumn,
                    sheet.ShowGridlines,
                    sheet.ShowHeadings,
                    sheet.ShowRulers,
                    125,
                    sheet.ShowFormulas)
            ],
            IncludePrintSettings: true,
            IncludeHiddenRowsColumnsAndFilterSettings: true));
        var hidden = workbook.AddSheet("Hidden Meta");
        Set(hidden, "A1", new TextValue("Very hidden metadata fixture"));
        hidden.IsHidden = true;
        hidden.IsVeryHidden = true;
        hidden.CodeName = "HiddenMeta";
        hidden.TabColor = new CellColor(255, 192, 0);
        return workbook;
    }

    private static Workbook NewWorkbook(string name) => new(name);

    private static CellAddress Addr(Sheet sheet, string a1) => CellAddress.Parse(a1, sheet.Id);

    private static GridRange Range(Sheet sheet, string start, string end) =>
        new(Addr(sheet, start), Addr(sheet, end));

    private static void Set(Sheet sheet, string a1, ScalarValue value) =>
        sheet.SetCell(Addr(sheet, a1), value);

    private static void Set(Sheet sheet, string a1, ScalarValue value, StyleId styleId)
    {
        var address = Addr(sheet, a1);
        sheet.SetCell(address, value);
        sheet.GetCell(address)!.StyleId = styleId;
    }

    private static StyleId RegisterHyperlinkStyle(Workbook workbook) =>
        workbook.RegisterStyle(new CellStyle
        {
            Underline = true,
            FontColor = new CellColor(5, 99, 193)
        });

    private static void Formula(Sheet sheet, string a1, string formula) =>
        sheet.SetFormula(Addr(sheet, a1), formula);

    private static byte[] MinimalPngBytes() =>
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41,
        0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
        0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82
    ];

    private static MemoryStream CreatePackage(params (string Name, string Content)[] entries)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (entryName, content) in entries)
            {
                var entry = archive.CreateEntry(entryName);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(content);
            }
        }

        stream.Position = 0;
        return stream;
    }
}
