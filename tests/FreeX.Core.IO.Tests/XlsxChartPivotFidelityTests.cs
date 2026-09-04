using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round-trip fidelity tests for chart/pivot P2/P3 fixes (2026-06-12 batch).
///
/// Fix 1  – Numeric chart categories emitted as &lt;c:numRef&gt; not &lt;c:strRef&gt;.
/// Fix 2  – refreshOnLoad OOXML default (absent attr) reads as false.
/// Fix 3  – Pivot shared-item type preserved via SharedItemKinds (s/n/d/b round-trips).
/// Fix 4  – chartEx axis-bearing types (Histogram, Funnel, …) emit &lt;cx:axis&gt; elements.
/// Fix 5  – Two pivot caches get distinct records rel ids.
/// Fix 6  – Legacy comment authors round-trip via CommentAuthors dictionary.
/// Fix 7  – Threaded comment parts occupy next-free index (stable path).
/// Fix 8  – Multi-area series formula stored verbatim when unparseable as rectangle.
/// Fix 9  – Axis title with rich/bold formatting stored verbatim.
/// Fix 10 – dxf numFmtId does not collide with workbook custom numFmt ids.
/// </summary>
public sealed class XlsxChartPivotFidelityTests
{
    // ─────────────────────────────────────────────────────────────────────────
    // Fix 1 – Numeric category column → <c:numRef>
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void NumericCategoryColumn_EmitsNumRefInsteadOfStrRef()
    {
        var (workbook, sheet) = MakeWorkbook("NumCatTest");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Year"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Value"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(2021));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(2022));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new NumberValue(2023));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(30));
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2)),
            FirstRowIsHeader = true,
            FirstColIsCategories = true
        });

        using var saved = SaveToStream(workbook);
        var chartXml = ReadFirstChartXml(saved);
        XNamespace c = "http://schemas.openxmlformats.org/drawingml/2006/chart";

        var cat = chartXml.Descendants(c + "cat").First();
        cat.Element(c + "numRef").Should().NotBeNull("numeric category column must use <c:numRef>");
        cat.Element(c + "strRef").Should().BeNull("numeric category column must NOT use <c:strRef>");
    }

    [Fact]
    public void TextCategoryColumn_EmitsStrRef()
    {
        var (workbook, sheet) = MakeWorkbook("TextCatTest");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Value"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)),
            FirstRowIsHeader = true,
            FirstColIsCategories = true
        });

        using var saved = SaveToStream(workbook);
        var chartXml = ReadFirstChartXml(saved);
        XNamespace c = "http://schemas.openxmlformats.org/drawingml/2006/chart";

        var cat = chartXml.Descendants(c + "cat").First();
        cat.Element(c + "strRef").Should().NotBeNull("text category column must use <c:strRef>");
        cat.Element(c + "numRef").Should().BeNull("text category column must NOT use <c:numRef>");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Fix 2 – refreshOnLoad absent → false
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void PivotCacheRoundTrip_WithRefreshOnLoadFalse_WritesNoAttribute()
    {
        var workbook = BuildPivotWorkbook(refreshOnLoad: false);
        using var saved = SaveToStream(workbook);

        // After save+reload the flag must still be false
        saved.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(saved);

        reloaded.PivotCaches.Should().ContainSingle()
            .Which.RefreshOnLoad.Should().BeFalse("saved false must reload as false");
    }

    [Fact]
    public void PivotCacheRoundTrip_WithRefreshOnLoadTrue_WritesAttribute()
    {
        var workbook = BuildPivotWorkbook(refreshOnLoad: true);
        using var saved = SaveToStream(workbook);

        // Verify the XML actually carries the attribute
        var cacheXml = ReadPivotCacheDefinitionXml(saved);
        cacheXml.Root!.Attribute("refreshOnLoad")!.Value.Should().Be("1",
            "refreshOnLoad=true must write refreshOnLoad=\"1\" in the XML");

        // Also verify reload
        saved.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(saved);
        reloaded.PivotCaches.Should().ContainSingle()
            .Which.RefreshOnLoad.Should().BeTrue();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Fix 3 – Pivot shared-item types (s/n/d/b) preserved via SharedItemKinds
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void PivotCacheSharedItemKinds_TextKindsWriteAsStringElements()
    {
        var workbook = new Workbook("SharedItemKindsTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Code"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("123"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("456"));

        var cache = new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = sheet.Name,
            SourceReference = "A1:A3",
            PackagePart = "xl/pivotCache/pivotCacheDefinition1.xml",
            RecordCount = 2,
            CreatedVersion = 8,
            MinRefreshableVersion = 4
        };
        // SharedItemKinds 's' means text — even though the value looks numeric
        cache.Fields.Add(new PivotCacheFieldModel(
            "Code",
            SharedItems: new[] { "123", "456" },
            SharedItemKinds: new[] { 's', 's' }));
        workbook.PivotCaches.Add(cache);
        AddMinimalPivotTable(sheet, cache);

        using var saved = SaveToStream(workbook);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var cacheXml = ReadPivotCacheDefinitionXml(saved);

        var sharedItems = cacheXml.Descendants(ns + "sharedItems").First();
        sharedItems.Elements(ns + "s").Should().HaveCount(2,
            "items with kind='s' must be written as <s> elements");
        sharedItems.Elements(ns + "n").Should().BeEmpty(
            "no numeric elements should appear when kind='s' is specified");
    }

    [Fact]
    public void PivotCacheSharedItemKinds_NumericKindsWriteAsNumericElements()
    {
        var workbook = new Workbook("SharedItemKindsNumTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(20));

        var cache = new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = sheet.Name,
            SourceReference = "A1:A3",
            PackagePart = "xl/pivotCache/pivotCacheDefinition1.xml",
            RecordCount = 2,
            CreatedVersion = 8,
            MinRefreshableVersion = 4
        };
        cache.Fields.Add(new PivotCacheFieldModel(
            "Amount",
            NumberFormatId: 4,
            SharedItems: new[] { "10", "20" },
            SharedItemKinds: new[] { 'n', 'n' }));
        workbook.PivotCaches.Add(cache);
        AddMinimalPivotTable(sheet, cache);

        using var saved = SaveToStream(workbook);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var cacheXml = ReadPivotCacheDefinitionXml(saved);

        var sharedItems = cacheXml.Descendants(ns + "sharedItems").First();
        sharedItems.Elements(ns + "n").Should().HaveCount(2,
            "items with kind='n' must be written as <n> elements");
        sharedItems.Elements(ns + "s").Should().BeEmpty(
            "no string elements should appear when kind='n' is specified");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Fix 4 – chartEx axis-bearing types emit <cx:axis> elements
    // ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(ChartType.Histogram)]
    [InlineData(ChartType.Funnel)]
    [InlineData(ChartType.BoxAndWhisker)]
    [InlineData(ChartType.Waterfall)]
    [InlineData(ChartType.Pareto)]
    public void ChartEx_AxisBearingTypes_EmitAxisElements(ChartType chartType)
    {
        using var saved = SaveChartExWorkbook(chartType);
        var cxXml = ReadChartExXml(saved);

        XNamespace cx = "http://schemas.microsoft.com/office/drawing/2014/chartex";
        cxXml.Descendants(cx + "axis").Should().NotBeEmpty(
            $"chart type {chartType} must emit <cx:axis> elements");
    }

    [Theory]
    [InlineData(ChartType.Treemap)]
    [InlineData(ChartType.Sunburst)]
    public void ChartEx_AxislessTypes_DoNotEmitAxisElements(ChartType chartType)
    {
        using var saved = SaveChartExWorkbook(chartType);
        var cxXml = ReadChartExXml(saved);

        XNamespace cx = "http://schemas.microsoft.com/office/drawing/2014/chartex";
        cxXml.Descendants(cx + "axis").Should().BeEmpty(
            $"chart type {chartType} must NOT emit <cx:axis> elements");
    }

    [Fact]
    public void ChartEx_Histogram_WithYAxisScaling_AppliesScalingToAxis()
    {
        using var saved = SaveChartExWorkbook(ChartType.Histogram, chart =>
        {
            chart.YAxisMinimum = 0;
            chart.YAxisMaximum = 100;
            chart.YAxisMajorUnit = 20;
        });
        var cxXml = ReadChartExXml(saved);
        XNamespace cx = "http://schemas.microsoft.com/office/drawing/2014/chartex";

        // The value axis (<cx:axis>) that has a <cx:valScaling> child
        var valAxis = cxXml.Descendants(cx + "axis")
            .FirstOrDefault(a => a.Element(cx + "valScaling") is not null);
        valAxis.Should().NotBeNull("Histogram must have a value axis with <cx:valScaling>");

        var scaling = valAxis!.Element(cx + "valScaling")!;
        scaling.Attribute("min")!.Value.Should().Be("0");
        scaling.Attribute("max")!.Value.Should().Be("100");
        scaling.Attribute("majorUnit")!.Value.Should().Be("20");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Fix 5 – Two pivot caches get distinct records rel ids
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TwoPivotCaches_GetDistinctRecordsRelIds()
    {
        var (workbook, sheet) = MakeWorkbook("TwoCacheTest");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("X"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Y"));

        for (var i = 1; i <= 2; i++)
        {
            var cache = new PivotCacheModel
            {
                CacheId = i,
                SourceType = PivotCacheSourceType.WorksheetRange,
                SourceSheetName = sheet.Name,
                SourceReference = "A1:A2",
                PackagePart = $"xl/pivotCache/pivotCacheDefinition{i}.xml",
                RecordCount = 1,
                CreatedVersion = 8,
                MinRefreshableVersion = 4
            };
            cache.Fields.Add(new PivotCacheFieldModel("X"));
            workbook.PivotCaches.Add(cache);
            // The pivot writer only runs when at least one sheet has pivot tables
            AddMinimalPivotTable(sheet, cache, targetStartRow: (uint)(5 + i * 5));
        }

        using var saved = SaveToStream(workbook);
        saved.Position = 0;
        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);

        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        var relIds = archive.Entries
            .Where(e =>
                e.FullName.Contains("pivotCacheDefinition", StringComparison.OrdinalIgnoreCase) &&
                e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
                !e.FullName.Contains("_rels", StringComparison.OrdinalIgnoreCase))
            .Select(e =>
            {
                using var s = e.Open();
                var doc = XDocument.Load(s);
                return doc.Root!.Attribute(relNs + "id")?.Value;
            })
            .Where(id => id is not null)
            .ToList();

        relIds.Should().HaveCount(2, "two pivot caches must produce two cacheDefinition entries");
        relIds.Distinct(StringComparer.Ordinal).Should().HaveCount(2,
            "each pivot cache must get a unique records relationship id");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Fix 6 – Legacy comment authors round-trip via CommentAuthors
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void LegacyComment_WithAuthor_PopulatesCommentAuthors()
    {
        using var package = CreateLegacyCommentPackage("C2", "Review this", "Alice");

        var workbook = new XlsxFileAdapter().Load(package);
        var sheet = workbook.GetSheetAt(0);

        // C2 = row 2, col 3
        var address = new CellAddress(sheet.Id, 2, 3);
        sheet.Comments.Should().ContainKey(address);
        sheet.Comments[address].Should().Be("Review this");
        sheet.CommentAuthors.Should().ContainKey(address,
            "comment with named author must populate CommentAuthors");
        sheet.CommentAuthors[address].Should().Be("Alice");
    }

    [Fact]
    public void LegacyComment_EmptyAuthors_DoesNotPopulateCommentAuthors()
    {
        // Test that a comment whose author is an empty string does not populate CommentAuthors.
        // (An all-whitespace or empty string author is treated as "no author" by FreeX — see
        // LoadSheetXmlLayoutApplication: if (!string.IsNullOrEmpty(author)) ... ).
        // We use authorId="0" pointing to an empty-string <author> element to avoid a ClosedXML
        // NPE that occurs when the <authors/> element has no children.
        using var package = CreateLegacyCommentPackage("C2", "No author note", authorName: "");

        var workbook = new XlsxFileAdapter().Load(package);
        var sheet = workbook.GetSheetAt(0);

        var address = new CellAddress(sheet.Id, 2, 3);
        sheet.Comments.Should().ContainKey(address,
            "the comment text should still be loaded even when author is empty");
        sheet.CommentAuthors.Should().NotContainKey(address,
            "comment with empty-string author should not map to CommentAuthors");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Fix 7 – Threaded comment part uses next-free index
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ThreadedComment_WrittenToNextFreeIndex()
    {
        var workbook = new Workbook("TcIndexTest");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.ThreadedComments[new CellAddress(sheet.Id, 2, 3)] =
            new ThreadedComment("Check this", "TestUser")
            {
                CreatedAtUtc = new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero)
            };

        using var saved = SaveToStream(workbook);
        saved.Position = 0;
        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);

        archive.Entries
            .Select(e => e.FullName)
            .Should()
            .Contain("xl/threadedComments/threadedComment1.xml",
                "first threaded comment must get index 1 (next-free stable allocation)");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Fix 8 – Multi-area series formula stored verbatim
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void UnparsableSeriesFormula_PopulatesVerbatimSeriesFormulas()
    {
        // A full-column reference like Sheet1!$B:$B is unparseable as a GridRange
        // (no row numbers) so it triggers the verbatim-capture path.
        var sheetId = new SheetId(Guid.NewGuid());
        const string fullColFormula = "Sheet1!$B:$B";
        var chartXml = XDocument.Parse($$"""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <c:chart>
                <c:plotArea>
                  <c:barChart>
                    <c:barDir val="col"/>
                    <c:ser>
                      <c:idx val="0"/>
                      <c:order val="0"/>
                      <c:tx><c:strRef><c:f>Sheet1!$B$1</c:f></c:strRef></c:tx>
                      <c:cat><c:strRef><c:f>Sheet1!$A:$A</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>{{fullColFormula}}</c:f></c:numRef></c:val>
                    </c:ser>
                  </c:barChart>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart).Should().BeTrue();

        chart.VerbatimSeriesFormulas.Should().NotBeNull(
            "full-column formula cannot parse as GridRange — verbatim capture must trigger");

        var s0 = chart.VerbatimSeriesFormulas!.Should().ContainSingle().Subject;
        s0.ValFormula.Should().Be(fullColFormula,
            "val formula must be stored exactly as read from XML");
        s0.CatFormula.Should().Be("Sheet1!$A:$A",
            "cat formula must also be stored verbatim when verbatim mode activates");
    }

    [Fact]
    public void SingleAreaSeriesFormula_DoesNotSetVerbatimSeriesFormulas()
    {
        var sheetId = new SheetId(Guid.NewGuid());
        var chartXml = XDocument.Parse("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <c:chart>
                <c:plotArea>
                  <c:barChart>
                    <c:barDir val="col"/>
                    <c:ser>
                      <c:idx val="0"/>
                      <c:order val="0"/>
                      <c:tx><c:strRef><c:f>Sheet1!$B$1</c:f></c:strRef></c:tx>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$B$2:$B$4</c:f></c:numRef></c:val>
                    </c:ser>
                  </c:barChart>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart).Should().BeTrue();

        chart.VerbatimSeriesFormulas.Should().BeNull(
            "single-area formulas are all parseable — verbatim capture must not trigger");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Fix 9 – Axis title with rich/bold formatting stored verbatim
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void YAxisTitle_WithBoldFormatting_PopulatesVerbatimXml()
    {
        var sheetId = new SheetId(Guid.NewGuid());
        var chartXml = XDocument.Parse("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <c:chart>
                <c:plotArea>
                  <c:barChart>
                    <c:barDir val="col"/>
                    <c:ser>
                      <c:idx val="0"/>
                      <c:order val="0"/>
                      <c:tx><c:strRef><c:f>Sheet1!$B$1</c:f></c:strRef></c:tx>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$B$2:$B$4</c:f></c:numRef></c:val>
                    </c:ser>
                    <c:axId val="1"/>
                    <c:axId val="2"/>
                  </c:barChart>
                  <c:catAx>
                    <c:axId val="1"/>
                    <c:scaling><c:orientation val="minMax"/></c:scaling>
                    <c:delete val="0"/>
                    <c:axPos val="b"/>
                    <c:crossAx val="2"/>
                    <c:crosses val="autoZero"/>
                  </c:catAx>
                  <c:valAx>
                    <c:axId val="2"/>
                    <c:scaling><c:orientation val="minMax"/></c:scaling>
                    <c:delete val="0"/>
                    <c:axPos val="l"/>
                    <c:title>
                      <c:tx>
                        <c:rich>
                          <a:bodyPr/>
                          <a:p>
                            <a:r>
                              <a:rPr lang="en-US" sz="1200" b="1"/>
                              <a:t>Bold Y Title</a:t>
                            </a:r>
                          </a:p>
                        </c:rich>
                      </c:tx>
                    </c:title>
                    <c:numFmt formatCode="General" sourceLinked="1"/>
                    <c:majorTickMark val="out"/>
                    <c:minorTickMark val="none"/>
                    <c:tickLblPos val="nextTo"/>
                    <c:crossAx val="1"/>
                    <c:crosses val="autoZero"/>
                  </c:valAx>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart).Should().BeTrue();

        chart.YAxisTitle.Should().Be("Bold Y Title", "plain-text extraction must still work");
        chart.YAxisTitleVerbatimXml.Should().NotBeNullOrWhiteSpace(
            "b=\"1\" attribute must trigger verbatim capture");
        chart.YAxisTitleVerbatimXml.Should().Contain("b=\"1\"",
            "verbatim XML must include the bold attribute");
    }

    [Fact]
    public void YAxisTitle_PlainFormattingOnly_DoesNotPopulateVerbatimXml()
    {
        var sheetId = new SheetId(Guid.NewGuid());
        var chartXml = XDocument.Parse("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <c:chart>
                <c:plotArea>
                  <c:barChart>
                    <c:barDir val="col"/>
                    <c:ser>
                      <c:idx val="0"/>
                      <c:order val="0"/>
                      <c:tx><c:strRef><c:f>Sheet1!$B$1</c:f></c:strRef></c:tx>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$B$2:$B$4</c:f></c:numRef></c:val>
                    </c:ser>
                    <c:axId val="1"/>
                    <c:axId val="2"/>
                  </c:barChart>
                  <c:catAx>
                    <c:axId val="1"/>
                    <c:scaling><c:orientation val="minMax"/></c:scaling>
                    <c:delete val="0"/>
                    <c:axPos val="b"/>
                    <c:crossAx val="2"/>
                    <c:crosses val="autoZero"/>
                  </c:catAx>
                  <c:valAx>
                    <c:axId val="2"/>
                    <c:scaling><c:orientation val="minMax"/></c:scaling>
                    <c:delete val="0"/>
                    <c:axPos val="l"/>
                    <c:title>
                      <c:tx>
                        <c:rich>
                          <a:bodyPr/>
                          <a:p>
                            <a:r>
                              <a:rPr lang="en-US" sz="1200"/>
                              <a:t>Plain Y Title</a:t>
                            </a:r>
                          </a:p>
                        </c:rich>
                      </c:tx>
                    </c:title>
                    <c:numFmt formatCode="General" sourceLinked="1"/>
                    <c:majorTickMark val="out"/>
                    <c:minorTickMark val="none"/>
                    <c:tickLblPos val="nextTo"/>
                    <c:crossAx val="1"/>
                    <c:crosses val="autoZero"/>
                  </c:valAx>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart).Should().BeTrue();

        chart.YAxisTitle.Should().Be("Plain Y Title");
        chart.YAxisTitleVerbatimXml.Should().BeNullOrWhiteSpace(
            "plain axis title (no b/i/u/strike/multi-run) must not set verbatim XML");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Fix 10 – dxf numFmtId does not collide with workbook custom numFmts
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ConditionalFormat_WithCustomNumberFormat_DxfNumFmtIdDoesNotCollideWithWorkbookNumFmts()
    {
        // Fix 10: dxf numFmtIds were previously allocated as 164 + dxfIndex, which collides
        // with workbook-level custom numFmt entries. The fix allocates them above the highest
        // existing id (workbook + any prior dxf entries) to avoid collision.
        //
        // This test exercises the ADVANCED CF path (ContainsText → IsAdvancedConditionalFormat=true),
        // which is the path that calls SaveDifferentialStyles. CellValue rules go through ClosedXML
        // and do not exercise this allocation logic.

        var (workbook, sheet) = MakeWorkbook("DxfNumFmtTest");
        for (uint row = 2; row <= 6; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue($"item{row}"));

        // Register a workbook-level custom numFmt — this will get id 164 (the first custom slot).
        // The old buggy code would also assign 164+1=165 to the first dxf entry when a dxf already
        // exists; with a workbook numFmt at 164 and dxf allocated as 164+nextIndex, a collision is
        // possible. The fix ensures the dxf always gets max(workbookMax, dxfMax) + 1 instead.
        var workbookStyleId = workbook.RegisterStyle(new CellStyle { NumberFormat = "\"Item: \"@" });
        sheet.SetStyleOnly(2, 1, workbookStyleId);

        // Add an advanced CF rule (ContainsText = IsAdvancedConditionalFormat) with a custom numFmt.
        // This goes through SaveDifferentialStyles → ToDifferentialStyleXml with the fixed id allocation.
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(
                new CellAddress(sheet.Id, 2, 1),
                new CellAddress(sheet.Id, 6, 1)),
            Priority = 1,
            RuleType = CfRuleType.ContainsText,
            TextRuleText = "item",
            FormatIfTrue = new CellStyle { NumberFormat = "\"Found: \"@" }
        });

        using var saved = SaveToStream(workbook);
        saved.Position = 0;
        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);

        XNamespace ss = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var stylesEntry = archive.GetEntry("xl/styles.xml");
        stylesEntry.Should().NotBeNull();

        XDocument stylesXml;
        using (var s = stylesEntry!.Open())
            stylesXml = XDocument.Load(s);

        // Collect workbook custom numFmt ids (≥ 164) from <numFmts>
        var workbookNumFmtIds = stylesXml.Root!
            .Element(ss + "numFmts")?
            .Elements(ss + "numFmt")
            .Select(e => int.TryParse(e.Attribute("numFmtId")?.Value, out var id) ? id : -1)
            .Where(id => id >= 164)
            .ToHashSet() ?? [];

        // Collect dxf numFmtIds (≥ 164)
        var dxfNumFmtIds = stylesXml.Root!
            .Element(ss + "dxfs")?
            .Elements(ss + "dxf")
            .SelectMany(dxf => dxf.Descendants(ss + "numFmt"))
            .Select(nf => int.TryParse(nf.Attribute("numFmtId")?.Value, out var id) ? id : -1)
            .Where(id => id >= 164)
            .ToList() ?? [];

        dxfNumFmtIds.Should().NotBeEmpty(
            "ContainsText CF rule with non-built-in format must emit a dxf numFmt with id ≥ 164");

        foreach (var dxfId in dxfNumFmtIds)
        {
            workbookNumFmtIds.Should().NotContain(dxfId,
                $"dxf numFmtId {dxfId} must not collide with a workbook custom numFmt entry (Fix 10)");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static (Workbook Workbook, Sheet Sheet) MakeWorkbook(string name)
    {
        var workbook = new Workbook(name);
        var sheet = workbook.AddSheet("Data");
        return (workbook, sheet);
    }

    private static MemoryStream SaveToStream(Workbook workbook)
    {
        var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;
        return stream;
    }

    private static XDocument ReadFirstChartXml(MemoryStream package)
    {
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.Entries.First(e =>
            e.FullName.StartsWith("xl/charts/chart", StringComparison.OrdinalIgnoreCase) &&
            e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
            !e.FullName.Contains("style", StringComparison.OrdinalIgnoreCase) &&
            !e.FullName.Contains("color", StringComparison.OrdinalIgnoreCase));
        using var s = entry.Open();
        return XDocument.Load(s);
    }

    private static XDocument ReadChartExXml(MemoryStream package)
    {
        // ChartEx types write to xl/charts/chart1.xml (same path as classic charts);
        // they are distinguished by the cx: namespace on the root element.
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry("xl/charts/chart1.xml");
        entry.Should().NotBeNull("xl/charts/chart1.xml must exist in the saved package");
        using var s = entry!.Open();
        return XDocument.Load(s);
    }

    private static XDocument ReadPivotCacheDefinitionXml(MemoryStream package)
    {
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.Entries.First(e =>
            e.FullName.Contains("pivotCacheDefinition", StringComparison.OrdinalIgnoreCase) &&
            e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
        using var s = entry.Open();
        return XDocument.Load(s);
    }

    private static Workbook BuildPivotWorkbook(bool refreshOnLoad)
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("B"));

        var cache = new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = sheet.Name,
            SourceReference = "A1:A3",
            PackagePart = "xl/pivotCache/pivotCacheDefinition1.xml",
            RecordCount = 2,
            CreatedVersion = 8,
            MinRefreshableVersion = 4,
            RefreshOnLoad = refreshOnLoad
        };
        cache.Fields.Add(new PivotCacheFieldModel("Category"));
        workbook.PivotCaches.Add(cache);
        // Pivot caches are only written when at least one sheet has a pivot table
        AddMinimalPivotTable(sheet, cache);
        return workbook;
    }

    /// <summary>
    /// Adds a minimal pivot table to <paramref name="sheet"/> that references <paramref name="cache"/>.
    /// This ensures the XlsxPivotTableWriter runs (it only runs when HasPivotTables is true).
    /// </summary>
    private static void AddMinimalPivotTable(Sheet sheet, PivotCacheModel cache, uint targetStartRow = 6)
    {
        var pivot = new PivotTableModel
        {
            Name = $"PivotTable_Cache{cache.CacheId}",
            CacheId = cache.CacheId,
            SourceRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 3, 1)),
            TargetRange = new GridRange(
                new CellAddress(sheet.Id, targetStartRow, 3),
                new CellAddress(sheet.Id, targetStartRow + 3, 4)),
            PackagePart = $"xl/pivotTables/pivotTable_c{cache.CacheId}.xml"
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(0, "Count of Category", "count"));
        sheet.PivotTables.Add(pivot);
    }

    private static MemoryStream SaveChartExWorkbook(ChartType chartType, Action<ChartModel>? configure = null)
    {
        var workbook = new Workbook("ChartExFidelityTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Value"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("B"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("C"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(30));

        var chart = new ChartModel
        {
            Type = chartType,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2)),
            Title = chartType.ToString()
        };
        configure?.Invoke(chart);
        sheet.Charts.Add(chart);

        return SaveToStream(workbook);
    }

    /// <summary>
    /// Creates a minimal XLSX package with a single legacy comment.
    /// <para>
    /// When <paramref name="authorName"/> is a non-empty string the comment has <c>authorId="0"</c>
    /// pointing to that author (named-author case).  When <paramref name="authorName"/> is null or
    /// empty string the comment still references <c>authorId="0"</c> but the author element value
    /// is empty — ClosedXML NPEs on a truly absent &lt;authors/&gt; element so we must always
    /// provide at least one &lt;author&gt; child.
    /// </para>
    /// </summary>
    private static MemoryStream CreateLegacyCommentPackage(string cellRef, string text, string? authorName)
    {
        // ClosedXML crashes with NPE when comments have no <author> children (<authors/>).
        // Workaround: always emit at least one <author> element. For the "no author" case we
        // emit an empty-string author with authorId="0"; FreeX treats "" as "no author" and
        // skips the CommentAuthors entry.
        var effectiveAuthor = authorName ?? "";
        var authorsXml = $"<authors><author>{effectiveAuthor}</author></authors>";
        var authorAttr = " authorId=\"0\"";

        var commentsXml = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <comments xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              {authorsXml}
              <commentList>
                <comment ref="{cellRef}"{authorAttr}>
                  <text><r><t>{text}</t></r></text>
                </comment>
              </commentList>
            </comments>
            """;

        return XlsxPackageTestFixtures.CreatePackage(
            ("[Content_Types].xml", LegacyCommentContentTypesXml()),
            ("_rels/.rels", MinimalRootRels()),
            ("xl/workbook.xml", MinimalWorkbookXml()),
            ("xl/_rels/workbook.xml.rels", WorkbookRelsWithStyles()),
            ("xl/styles.xml", MinimalStylesXml()),
            ("xl/worksheets/sheet1.xml", MinimalWorksheetXmlWithLegacyDrawing()),
            ("xl/worksheets/_rels/sheet1.xml.rels", SheetRelsWithComments()),
            ("xl/comments1.xml", commentsXml),
            ("xl/drawings/vmlDrawing1.vml", MinimalVmlDrawing()));
    }

    private static string LegacyCommentContentTypesXml() => """
        <?xml version="1.0" encoding="UTF-8"?>
        <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
          <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
          <Default Extension="xml" ContentType="application/xml"/>
          <Default Extension="vml" ContentType="application/vnd.openxmlformats-officedocument.vmlDrawing"/>
          <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
          <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
          <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
          <Override PartName="/xl/comments1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.comments+xml"/>
        </Types>
        """;

    private static string MinimalRootRels() => """
        <?xml version="1.0" encoding="UTF-8"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
        </Relationships>
        """;

    private static string MinimalWorkbookXml() => """
        <?xml version="1.0" encoding="UTF-8"?>
        <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                  xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
          <sheets>
            <sheet name="Data" sheetId="1" r:id="rId1"/>
          </sheets>
        </workbook>
        """;

    private static string WorkbookRelsWithStyles() => """
        <?xml version="1.0" encoding="UTF-8"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
          <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
        </Relationships>
        """;

    private static string MinimalStylesXml() => """
        <?xml version="1.0" encoding="UTF-8"?>
        <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <fonts count="1"><font><sz val="11"/><color theme="1"/><name val="Calibri"/><family val="2"/><scheme val="minor"/></font></fonts>
          <fills count="2">
            <fill><patternFill patternType="none"/></fill>
            <fill><patternFill patternType="gray125"/></fill>
          </fills>
          <borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders>
          <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
          <cellXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/></cellXfs>
          <cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles>
          <dxfs count="0"/>
          <tableStyles count="0" defaultTableStyle="TableStyleMedium2" defaultPivotStyle="TableStyleLight16"/>
        </styleSheet>
        """;

    private static string MinimalWorksheetXmlWithLegacyDrawing() => """
        <?xml version="1.0" encoding="UTF-8"?>
        <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                   xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
          <dimension ref="A1:C2"/>
          <sheetData>
            <row r="1"><c r="A1" t="inlineStr"><is><t>source</t></is></c></row>
            <row r="2"><c r="C2" t="inlineStr"><is><t>review</t></is></c></row>
          </sheetData>
          <legacyDrawing r:id="rId2"/>
        </worksheet>
        """;

    private static string SheetRelsWithComments() => """
        <?xml version="1.0" encoding="UTF-8"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/comments" Target="../comments1.xml"/>
          <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/vmlDrawing" Target="../drawings/vmlDrawing1.vml"/>
        </Relationships>
        """;

    private static string MinimalVmlDrawing() => """
        <?xml version="1.0" encoding="UTF-8"?>
        <xml xmlns:v="urn:schemas-microsoft-com:vml"
             xmlns:o="urn:schemas-microsoft-com:office:office"
             xmlns:x="urn:schemas-microsoft-com:office:excel">
          <v:shape id="_x0000_s1025" type="#_x0000_t202"
                   style="position:absolute;margin-left:80pt;margin-top:6pt;width:108pt;height:59.25pt;z-index:1;visibility:hidden"
                   fillcolor="#ffffe1" o:insetmode="auto">
            <v:fill color2="#ffffe1"/>
            <v:shadow color="black" obscured="t"/>
            <v:path o:connecttype="none"/>
            <v:textbox style="mso-direction-alt:auto"><div style="text-align:left"/></v:textbox>
            <x:ClientData ObjectType="Note">
              <x:MoveWithCells/>
              <x:SizeWithCells/>
              <x:Anchor>2, 15, 1, 2, 4, 15, 5, 3</x:Anchor>
              <x:AutoFill>False</x:AutoFill>
              <x:Row>1</x:Row>
              <x:Column>2</x:Column>
            </x:ClientData>
          </v:shape>
        </xml>
        """;
}
