using System.Globalization;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxPivotFilterKindCodecTests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    public static IEnumerable<object[]> ValueDecodeCases()
    {
        yield return ["count", false, PivotValueFilterKind.Top];
        yield return ["count", true, PivotValueFilterKind.Bottom];
        yield return ["percent", false, PivotValueFilterKind.Top];
        yield return ["percent", true, PivotValueFilterKind.Bottom];
        yield return ["sum", false, PivotValueFilterKind.Top];
        yield return ["sum", true, PivotValueFilterKind.Bottom];
        yield return ["topcount", false, PivotValueFilterKind.Top];
        yield return ["topcount", true, PivotValueFilterKind.Bottom];
        yield return ["top", false, PivotValueFilterKind.Top];
        yield return ["top", true, PivotValueFilterKind.Bottom];
        yield return ["bottomcount", false, PivotValueFilterKind.Bottom];
        yield return ["bottom", false, PivotValueFilterKind.Bottom];
        yield return ["valueequal", false, PivotValueFilterKind.Equals];
        yield return ["valueequals", false, PivotValueFilterKind.Equals];
        yield return ["valuenotequal", false, PivotValueFilterKind.DoesNotEqual];
        yield return ["valuedoesnotequal", false, PivotValueFilterKind.DoesNotEqual];
        yield return ["valuegreaterthan", false, PivotValueFilterKind.GreaterThan];
        yield return ["valuegreaterthanorequal", false, PivotValueFilterKind.GreaterThanOrEqual];
        yield return ["valuelessthan", false, PivotValueFilterKind.LessThan];
        yield return ["valuelessthanorequal", false, PivotValueFilterKind.LessThanOrEqual];
        yield return ["valuebetween", false, PivotValueFilterKind.Between];
        yield return ["valuenotbetween", false, PivotValueFilterKind.NotBetween];
    }

    public static IEnumerable<object[]> ValueEncodeCases()
    {
        yield return [PivotValueFilterKind.Top, "count"];
        yield return [PivotValueFilterKind.Bottom, "count"];
        yield return [PivotValueFilterKind.GreaterThan, "valueGreaterThan"];
        yield return [PivotValueFilterKind.GreaterThanOrEqual, "valueGreaterThanOrEqual"];
        yield return [PivotValueFilterKind.LessThan, "valueLessThan"];
        yield return [PivotValueFilterKind.LessThanOrEqual, "valueLessThanOrEqual"];
        yield return [PivotValueFilterKind.Equals, "valueEqual"];
        yield return [PivotValueFilterKind.DoesNotEqual, "valueNotEqual"];
        yield return [PivotValueFilterKind.Between, "valueBetween"];
        yield return [PivotValueFilterKind.NotBetween, "valueNotBetween"];
        yield return [PivotValueFilterKind.AboveAverage, null!];
        yield return [PivotValueFilterKind.BelowAverage, null!];
    }

    public static IEnumerable<object[]> LabelDecodeCases()
    {
        foreach (var entry in LabelCanonicalTokens)
            yield return [entry.Value, entry.Key];

        yield return ["captionEquals", PivotLabelFilterKind.Equals];
        yield return ["captionDoesNotEqual", PivotLabelFilterKind.DoesNotEqual];
        yield return ["captionDoesNotContain", PivotLabelFilterKind.DoesNotContain];
    }

    public static IEnumerable<object[]> LabelEncodeCases()
    {
        foreach (var entry in LabelCanonicalTokens)
            yield return [entry.Key, entry.Value];
    }

    public static IEnumerable<object[]> LabelEmptyValueCases()
    {
        foreach (var kind in Enum.GetValues<PivotLabelFilterKind>())
        {
            var expected = kind is PivotLabelFilterKind.Yesterday or
                PivotLabelFilterKind.Today or
                PivotLabelFilterKind.Tomorrow or
                PivotLabelFilterKind.LastWeek or
                PivotLabelFilterKind.ThisWeek or
                PivotLabelFilterKind.NextWeek or
                PivotLabelFilterKind.LastMonth or
                PivotLabelFilterKind.ThisMonth or
                PivotLabelFilterKind.NextMonth or
                PivotLabelFilterKind.LastQuarter or
                PivotLabelFilterKind.ThisQuarter or
                PivotLabelFilterKind.NextQuarter or
                PivotLabelFilterKind.LastYear or
                PivotLabelFilterKind.ThisYear or
                PivotLabelFilterKind.NextYear or
                PivotLabelFilterKind.YearToDate;
            yield return [kind, expected];
        }
    }

    [Theory]
    [MemberData(nameof(ValueDecodeCases))]
    public void DecodeValue_AcceptsEveryNativeAndLegacyAlias(
        string token,
        bool topFilterIsBottom,
        PivotValueFilterKind expected)
    {
        XlsxPivotFilterKindCodec.DecodeValue(token, topFilterIsBottom).Should().Be(expected);
        XlsxPivotFilterKindCodec.DecodeValue($"  {token.ToUpperInvariant()}  ", topFilterIsBottom).Should().Be(expected);
    }

    [Theory]
    [MemberData(nameof(ValueEncodeCases))]
    public void EncodeValue_UsesCanonicalNativeTokens(PivotValueFilterKind kind, string? expected)
    {
        XlsxPivotFilterKindCodec.EncodeValue(kind).Should().Be(expected);
    }

    [Theory]
    [MemberData(nameof(LabelDecodeCases))]
    public void DecodeLabel_AcceptsEveryNativeAndLegacyAlias(string token, PivotLabelFilterKind expected)
    {
        XlsxPivotFilterKindCodec.DecodeLabel(token).Should().Be(expected);
        XlsxPivotFilterKindCodec.DecodeLabel($"  {token.ToUpperInvariant()}  ").Should().Be(expected);
    }

    [Theory]
    [MemberData(nameof(LabelEncodeCases))]
    public void EncodeLabel_UsesCanonicalNativeTokens(PivotLabelFilterKind kind, string expected)
    {
        XlsxPivotFilterKindCodec.EncodeLabel(kind).Should().Be(expected);
    }

    [Theory]
    [MemberData(nameof(LabelEmptyValueCases))]
    public void AllowsEmptyLabelValue_IsLimitedToRelativeDateKinds(PivotLabelFilterKind kind, bool expected)
    {
        XlsxPivotFilterKindCodec.AllowsEmptyLabelValue(kind).Should().Be(expected);
    }

    [Fact]
    public void ValueTopDirection_UsesNestedTop10AndItsSchemaDefault()
    {
        var absentTop10 = new XElement(WorkbookNs + "filter", new XAttribute("type", "count"));
        var absentTop = NativeTopFilter("count", top: null);
        var explicitTop = NativeTopFilter("percent", top: "1");
        var explicitBottom = NativeTopFilter("sum", top: "0");

        XlsxPivotFilterKindCodec.DecodeValue(absentTop10, WorkbookNs).Should().Be(PivotValueFilterKind.Top);
        XlsxPivotFilterKindCodec.DecodeValue(absentTop, WorkbookNs).Should().Be(PivotValueFilterKind.Top);
        XlsxPivotFilterKindCodec.DecodeValue(explicitTop, WorkbookNs).Should().Be(PivotValueFilterKind.Top);
        XlsxPivotFilterKindCodec.DecodeValue(explicitBottom, WorkbookNs).Should().Be(PivotValueFilterKind.Bottom);
    }

    [Fact]
    public void UnsupportedTokensAndKinds_PreserveEstablishedFallbacks()
    {
        foreach (var token in new string?[] { null, "", "  ", "unknown", "aboveAverage", "captionEqual" })
            XlsxPivotFilterKindCodec.DecodeValue(token).Should().BeNull();

        foreach (var token in new string?[] { null, "", "  ", "unknown", "valueEqual", "captionNotBetween" })
            XlsxPivotFilterKindCodec.DecodeLabel(token).Should().BeNull();

        XlsxPivotFilterKindCodec.EncodeValue((PivotValueFilterKind)int.MaxValue).Should().BeNull();
        XlsxPivotFilterKindCodec.EncodeLabel((PivotLabelFilterKind)int.MaxValue).Should().Be("captionEqual");
    }

    [Fact]
    public void CanonicalTokens_RoundTripEverySupportedKind()
    {
        foreach (var kind in Enum.GetValues<PivotValueFilterKind>())
        {
            var token = XlsxPivotFilterKindCodec.EncodeValue(kind);
            if (token is null)
                continue;

            XlsxPivotFilterKindCodec.DecodeValue(token, topFilterIsBottom: kind == PivotValueFilterKind.Bottom)
                .Should().Be(kind);
        }

        foreach (var kind in Enum.GetValues<PivotLabelFilterKind>())
        {
            var token = XlsxPivotFilterKindCodec.EncodeLabel(kind);
            XlsxPivotFilterKindCodec.DecodeLabel(token).Should().Be(kind);
        }
    }

    public static IEnumerable<object[]> PreservedNativeTopFilterCases()
    {
        yield return ["count", false, PivotValueFilterKind.Bottom, 7, false];
        yield return ["percent", true, PivotValueFilterKind.Top, 25, true];
        yield return ["sum", false, PivotValueFilterKind.Bottom, 125, false];
    }

    [Theory]
    [MemberData(nameof(PreservedNativeTopFilterCases))]
    public void PreservedSave_RetainsUntouchedNativeTopFilterRepresentation(
        string nativeType,
        bool top,
        PivotValueFilterKind expectedKind,
        int value,
        bool percent)
    {
        var workbook = CreatePivotWorkbook(expectedKind, value);
        using var source = XlsxPackageTestHelper.SaveWorkbook(workbook);
        XlsxPackageTestHelper.PatchPackageXml(source, "xl/pivotTables/pivotTable1.xml", document =>
        {
            var filter = document.Root!
                .Element(WorkbookNs + "filters")!
                .Element(WorkbookNs + "filter")!;
            filter.SetAttributeValue("type", nativeType);

            var top10 = filter.Element(WorkbookNs + "autoFilter")!
                .Element(WorkbookNs + "filterColumn")!
                .Element(WorkbookNs + "top10")!;
            top10.SetAttributeValue("top", top ? "1" : "0");
            top10.SetAttributeValue("val", value.ToString(CultureInfo.InvariantCulture));
            top10.SetAttributeValue("percent", percent ? "1" : null);
        });
        var expectedFilter = ReadNativeFilter(source);
        expectedFilter.Attribute("count").Should().BeNull();
        expectedFilter.Attribute("val").Should().BeNull();
        expectedFilter.Descendants(WorkbookNs + "top10").Single()
            .Attribute("val")!.Value.Should().Be(value.ToString(CultureInfo.InvariantCulture));

        var adapter = new XlsxFileAdapter();
        source.Position = 0;
        var loaded = adapter.Load(source);
        var loadedFilter = loaded.GetSheetAt(0).PivotTables.Single().ValueFilters.Single();
        loadedFilter.Kind.Should().Be(expectedKind);
        loadedFilter.Count.Should().Be(value);

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);

        var savedFilter = ReadNativeFilter(saved);
        XNode.DeepEquals(savedFilter, expectedFilter).Should().BeTrue(
            "an untouched source-native pivot filter must not be canonicalized on preserved save");
        savedFilter.Attribute("type")!.Value.Should().Be(nativeType);
        var savedTop10 = savedFilter.Descendants(WorkbookNs + "top10").Single();
        savedTop10.Attribute("top")!.Value.Should().Be(top ? "1" : "0");
        savedTop10.Attribute("val")!.Value.Should().Be(value.ToString(CultureInfo.InvariantCulture));
        savedTop10.Attribute("percent")?.Value.Should().Be(percent ? "1" : null);

        saved.Position = 0;
        var reloaded = adapter.Load(saved);
        var reloadedFilter = reloaded.GetSheetAt(0).PivotTables.Single().ValueFilters.Single();
        reloadedFilter.Kind.Should().Be(expectedKind);
        reloadedFilter.Count.Should().Be(value);
    }

    [Fact]
    public void ReaderPreservedSaveAndWriter_UseTheSharedCodec()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var reader = File.ReadAllText(Path.Combine(root, "src", "FreeX.Core.IO", "XlsxPivotTableReader.FiltersAndSorts.cs"));
        var preservedSave = File.ReadAllText(Path.Combine(root, "src", "FreeX.Core.IO", "XlsxFileAdapter.SavePostProcessing.cs"));
        var writer = File.ReadAllText(Path.Combine(root, "src", "FreeX.Core.IO", "XlsxPivotTableWriter.cs"));
        var writerConverters = File.ReadAllText(Path.Combine(root, "src", "FreeX.Core.IO", "XlsxPivotTableWriter.Converters.cs"));

        reader.Should().Contain("XlsxPivotFilterKindCodec.DecodeValue(filter, workbookNs)");
        reader.Should().Contain("XlsxPivotFilterKindCodec.DecodeLabel(filter.Attribute(\"type\")?.Value)");
        preservedSave.Should().Contain("XlsxPivotFilterKindCodec.DecodeValue(filter, workbookNs)");
        preservedSave.Should().Contain("XlsxPivotFilterKindCodec.DecodeLabel(filter.Attribute(\"type\")?.Value)");
        writer.Should().Contain("XlsxPivotFilterKindCodec.EncodeValue(filter.Kind)");
        writer.Should().Contain("XlsxPivotFilterKindCodec.EncodeLabel(filter.Kind)");

        reader.Should().NotContain("ReadNativePivotValueFilterKind");
        preservedSave.Should().NotContain("DecodeNativePivotValueFilterKind");
        writerConverters.Should().NotContain("ToNativePivotValueFilterKindText");
        writerConverters.Should().NotContain("ToNativePivotLabelFilterKindText");
    }

    private static XElement NativeTopFilter(string token, string? top) =>
        new(
            WorkbookNs + "filter",
            new XAttribute("type", token),
            new XElement(
                WorkbookNs + "autoFilter",
                new XElement(
                    WorkbookNs + "filterColumn",
                    new XElement(
                        WorkbookNs + "top10",
                        top is null ? null : new XAttribute("top", top)))));

    private static XElement ReadNativeFilter(MemoryStream package) =>
        new(XlsxPackageTestHelper.ReadPackageXml(package, "xl/pivotTables/pivotTable1.xml")
            .Root!
            .Element(WorkbookNs + "filters")!
            .Element(WorkbookNs + "filter")!);

    private static Workbook CreatePivotWorkbook(PivotValueFilterKind kind, int count)
    {
        var workbook = new Workbook("PivotFilterPreservation");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));

        var cache = new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = sheet.Name,
            SourceReference = "A1:B3",
        };
        cache.Fields.Add(new PivotCacheFieldModel("Region"));
        cache.Fields.Add(new PivotCacheFieldModel("Amount"));
        workbook.PivotCaches.Add(cache);

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 3, 2)),
            TargetRange = new GridRange(
                new CellAddress(sheet.Id, 5, 1),
                new CellAddress(sheet.Id, 8, 2)),
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        pivot.ValueFilters.Add(new PivotValueFilterModel(0, kind, count, SourceFieldIndex: 0));
        sheet.PivotTables.Add(pivot);

        return workbook;
    }

    private static readonly IReadOnlyDictionary<PivotLabelFilterKind, string> LabelCanonicalTokens =
        new Dictionary<PivotLabelFilterKind, string>
        {
            [PivotLabelFilterKind.Equals] = "captionEqual",
            [PivotLabelFilterKind.DoesNotEqual] = "captionNotEqual",
            [PivotLabelFilterKind.BeginsWith] = "captionBeginsWith",
            [PivotLabelFilterKind.EndsWith] = "captionEndsWith",
            [PivotLabelFilterKind.Contains] = "captionContains",
            [PivotLabelFilterKind.DoesNotContain] = "captionNotContains",
            [PivotLabelFilterKind.GreaterThan] = "captionGreaterThan",
            [PivotLabelFilterKind.GreaterThanOrEqual] = "captionGreaterThanOrEqual",
            [PivotLabelFilterKind.LessThan] = "captionLessThan",
            [PivotLabelFilterKind.LessThanOrEqual] = "captionLessThanOrEqual",
            [PivotLabelFilterKind.Between] = "captionBetween",
            [PivotLabelFilterKind.DateEqual] = "dateEqual",
            [PivotLabelFilterKind.DateNotEqual] = "dateNotEqual",
            [PivotLabelFilterKind.DateOlderThan] = "dateOlderThan",
            [PivotLabelFilterKind.DateOlderThanOrEqual] = "dateOlderThanOrEqual",
            [PivotLabelFilterKind.DateNewerThan] = "dateNewerThan",
            [PivotLabelFilterKind.DateNewerThanOrEqual] = "dateNewerThanOrEqual",
            [PivotLabelFilterKind.DateBetween] = "dateBetween",
            [PivotLabelFilterKind.DateNotBetween] = "dateNotBetween",
            [PivotLabelFilterKind.Yesterday] = "yesterday",
            [PivotLabelFilterKind.Today] = "today",
            [PivotLabelFilterKind.Tomorrow] = "tomorrow",
            [PivotLabelFilterKind.LastWeek] = "lastWeek",
            [PivotLabelFilterKind.ThisWeek] = "thisWeek",
            [PivotLabelFilterKind.NextWeek] = "nextWeek",
            [PivotLabelFilterKind.LastMonth] = "lastMonth",
            [PivotLabelFilterKind.ThisMonth] = "thisMonth",
            [PivotLabelFilterKind.NextMonth] = "nextMonth",
            [PivotLabelFilterKind.LastQuarter] = "lastQuarter",
            [PivotLabelFilterKind.ThisQuarter] = "thisQuarter",
            [PivotLabelFilterKind.NextQuarter] = "nextQuarter",
            [PivotLabelFilterKind.LastYear] = "lastYear",
            [PivotLabelFilterKind.ThisYear] = "thisYear",
            [PivotLabelFilterKind.NextYear] = "nextYear",
            [PivotLabelFilterKind.YearToDate] = "yearToDate",
        };
}
