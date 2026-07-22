using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Coverage for R71-meta-2: a .fxl file saved by any pre-r70 build persisted a data bar's default
/// "Automatic" min/max endpoint as the plain <see cref="CfThresholdType.Min"/>/<see cref="CfThresholdType.Max"/>
/// value (AutoMin/AutoMax did not exist yet). After r70 introduced the distinct AutoMin/AutoMax
/// variants and made the zero-baseline clamp apply ONLY to them, reloading such a legacy file must
/// still migrate the legacy Min/Max data-bar endpoint to AutoMin/AutoMax -- matching the equivalent
/// migration the XLSX read path already performs in XlsxFileAdapter.ConditionalFormats.cs -- or the
/// data bar silently renders with a zero-length bar instead of Excel's clamped-to-zero bar.
/// </summary>
public sealed class R71_NativeJsonLegacyDataBarAutomaticMigrationTests
{
    private static ConditionalFormat AddDataBar(
        Sheet sheet, CfThresholdType minType, CfThresholdType maxType)
    {
        var rule = new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 1)),
            Priority = 1,
            RuleType = CfRuleType.DataBar,
            DataBarColor = new RgbColor(99, 142, 198),
            DataBarMinThresholdType = minType,
            DataBarMaxThresholdType = maxType,
        };
        sheet.ConditionalFormats.Add(rule);
        return rule;
    }

    [Fact]
    public void Load_LegacyDataBarMinMaxThresholdType_MigratesToAutoMinAutoMax()
    {
        // A pre-r70 .fxl serialized its default "Automatic" data bar with the plain Min/Max ordinal
        // (the only values that existed at the time). Round-tripping that exact wire representation
        // through the native JSON adapter must now yield AutoMin/AutoMax so the zero-baseline clamp
        // still applies, not a silent switch to an explicit (zero-length) endpoint.
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        AddDataBar(sheet, CfThresholdType.Min, CfThresholdType.Max);
        using var stream = new MemoryStream();
        new NativeJsonAdapter().Save(workbook, stream);
        stream.Position = 0;

        var reloaded = new NativeJsonAdapter().Load(stream);

        var rule = reloaded.GetSheetAt(0).ConditionalFormats.Should().ContainSingle().Subject;
        rule.DataBarMinThresholdType.Should().Be(CfThresholdType.AutoMin,
            "a legacy Min data-bar endpoint must migrate to AutoMin to preserve the zero-baseline clamp");
        rule.DataBarMaxThresholdType.Should().Be(CfThresholdType.AutoMax,
            "a legacy Max data-bar endpoint must migrate to AutoMax to preserve the zero-baseline clamp");
    }

    [Fact]
    public void Load_NewAutoMinAutoMaxDataBar_RoundTripsUnchanged()
    {
        // Sibling no-regression case: a data bar saved by an r70+ build already serializes AutoMin/
        // AutoMax by their own distinct ordinal, so the legacy-migration remap must be a no-op here.
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        AddDataBar(sheet, CfThresholdType.AutoMin, CfThresholdType.AutoMax);
        using var stream = new MemoryStream();
        new NativeJsonAdapter().Save(workbook, stream);
        stream.Position = 0;

        var reloaded = new NativeJsonAdapter().Load(stream);

        var rule = reloaded.GetSheetAt(0).ConditionalFormats.Should().ContainSingle().Subject;
        rule.DataBarMinThresholdType.Should().Be(CfThresholdType.AutoMin);
        rule.DataBarMaxThresholdType.Should().Be(CfThresholdType.AutoMax);
    }

    [Fact]
    public void Load_NonMinMaxExplicitDataBarThresholdType_IsNeverMigrated()
    {
        // A data bar using a genuinely explicit, non-Min/Max threshold type (Number/Percentile, etc.)
        // is unambiguous and must never be touched by the legacy Min->AutoMin / Max->AutoMax remap.
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        AddDataBar(sheet, CfThresholdType.Number, CfThresholdType.Percentile);
        sheet.ConditionalFormats.Single().DataBarMinThresholdValue = "-10";
        sheet.ConditionalFormats.Single().DataBarMaxThresholdValue = "90";
        using var stream = new MemoryStream();
        new NativeJsonAdapter().Save(workbook, stream);
        stream.Position = 0;

        var reloaded = new NativeJsonAdapter().Load(stream);

        var rule = reloaded.GetSheetAt(0).ConditionalFormats.Should().ContainSingle().Subject;
        rule.DataBarMinThresholdType.Should().Be(CfThresholdType.Number,
            "a non-Min/Max explicit threshold type must never be migrated");
        rule.DataBarMaxThresholdType.Should().Be(CfThresholdType.Percentile,
            "a non-Min/Max explicit threshold type must never be migrated");
    }

    [Fact]
    public void Load_ColorScaleMinThresholdType_IsNotRemappedToAutoMin()
    {
        // The legacy-migration remap must be scoped to the data-bar min/max fields only. A color
        // scale's MinThresholdType has no Automatic concept and must always stay the literal Min.
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 1)),
            Priority = 1,
            RuleType = CfRuleType.ColorScale,
            MinColor = new RgbColor(99, 190, 123),
            MaxColor = new RgbColor(248, 105, 107),
            MinThresholdType = CfThresholdType.Min,
            MaxThresholdType = CfThresholdType.Max,
        });
        using var stream = new MemoryStream();
        new NativeJsonAdapter().Save(workbook, stream);
        stream.Position = 0;

        var reloaded = new NativeJsonAdapter().Load(stream);

        var rule = reloaded.GetSheetAt(0).ConditionalFormats.Should().ContainSingle().Subject;
        rule.MinThresholdType.Should().Be(CfThresholdType.Min,
            "color-scale thresholds have no Automatic concept and must not be remapped");
        rule.MaxThresholdType.Should().Be(CfThresholdType.Max,
            "color-scale thresholds have no Automatic concept and must not be remapped");
    }
}
