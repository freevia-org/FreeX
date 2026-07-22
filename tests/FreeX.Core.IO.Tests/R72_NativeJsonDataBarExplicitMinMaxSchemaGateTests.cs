using System.Text;
using System.Text.Json;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Coverage for R72-meta-1: <c>ToConditionalFormat</c> used to call the legacy data-bar Min/Max ->
/// AutoMin/AutoMax migration (introduced by R71-meta-2 for genuinely pre-r70 files) on EVERY load,
/// with no schema-version gate. That meant a user who explicitly picked "Lowest Value"
/// (<see cref="CfThresholdType.Min"/>) in the Data Bar dialog on an r70+ build had that explicit
/// choice silently and irrevocably reverted to "Automatic" (<see cref="CfThresholdType.AutoMin"/>) on
/// every single reload of their own file.
///
/// The fix bumps the native JSON schema version to 2 and threads the LOADED file's schema version
/// into the conditional-format loader: the legacy migration now applies ONLY when the loaded file's
/// schema version is &lt; 2 (see R71_NativeJsonLegacyDataBarAutomaticMigrationTests for that case). A
/// v2+ file's Min/Max is trusted as an explicit, unambiguous endpoint and must round-trip unchanged.
/// </summary>
public sealed class R72_NativeJsonDataBarExplicitMinMaxSchemaGateTests
{
    private static string DataBarJson(int? schemaVersion, int minThresholdType, int maxThresholdType)
    {
        var schemaVersionLine = schemaVersion is { } version ? $"\"SchemaVersion\": {version}," : "";
        return $$"""
            {
              {{schemaVersionLine}}
              "Name": "Book1",
              "Sheets": [
                {
                  "Name": "Sheet1",
                  "ConditionalFormats": [
                    {
                      "AppliesTo": "A1:A5",
                      "RuleType": 2,
                      "Operator": 0,
                      "DataBarMinThresholdType": {{minThresholdType}},
                      "DataBarMaxThresholdType": {{maxThresholdType}}
                    }
                  ]
                }
              ]
            }
            """;
    }

    private static Workbook LoadJson(string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return new NativeJsonAdapter().Load(stream);
    }

    [Fact]
    public void Load_V2SchemaExplicitMinMax_StaysExplicit_NotClobberedToAutomatic()
    {
        // (int)CfThresholdType.Min == 0, (int)CfThresholdType.Max == 1.
        var json = DataBarJson(schemaVersion: 2, minThresholdType: 0, maxThresholdType: 1);

        var workbook = LoadJson(json);

        var rule = workbook.GetSheetAt(0).ConditionalFormats.Should().ContainSingle().Subject;
        rule.DataBarMinThresholdType.Should().Be(CfThresholdType.Min,
            "a v2+ file's explicit Lowest Value endpoint must never be silently reverted to Automatic");
        rule.DataBarMaxThresholdType.Should().Be(CfThresholdType.Max,
            "a v2+ file's explicit Highest Value endpoint must never be silently reverted to Automatic");
    }

    [Fact]
    public void Load_V2SchemaAutoMinAutoMax_StaysAutomatic()
    {
        // (int)CfThresholdType.AutoMin == 6, (int)CfThresholdType.AutoMax == 7.
        var json = DataBarJson(schemaVersion: 2, minThresholdType: 6, maxThresholdType: 7);

        var workbook = LoadJson(json);

        var rule = workbook.GetSheetAt(0).ConditionalFormats.Should().ContainSingle().Subject;
        rule.DataBarMinThresholdType.Should().Be(CfThresholdType.AutoMin);
        rule.DataBarMaxThresholdType.Should().Be(CfThresholdType.AutoMax);
    }

    [Fact]
    public void RoundTrip_SaveExplicitMinMaxDataBar_NowStampedV2_ReloadsStillExplicit()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 1)),
            Priority = 1,
            RuleType = CfRuleType.DataBar,
            DataBarColor = new RgbColor(99, 142, 198),
            DataBarMinThresholdType = CfThresholdType.Min,
            DataBarMaxThresholdType = CfThresholdType.Max,
        });
        using var stream = new MemoryStream();

        new NativeJsonAdapter().Save(workbook, stream);

        // The freshly-saved file must be stamped with the current (>= 2) schema version -- otherwise
        // the gate below could never distinguish this save from a genuine pre-r70 legacy file.
        using (var savedDocument = JsonDocument.Parse(stream.ToArray()))
            savedDocument.RootElement.GetProperty("SchemaVersion").GetInt32().Should().BeGreaterThanOrEqualTo(2);

        stream.Position = 0;
        var reloaded = new NativeJsonAdapter().Load(stream);

        var rule = reloaded.GetSheetAt(0).ConditionalFormats.Should().ContainSingle().Subject;
        rule.DataBarMinThresholdType.Should().Be(CfThresholdType.Min,
            "saving and reloading an explicit Lowest Value data bar on a current build must never clobber it to Automatic");
        rule.DataBarMaxThresholdType.Should().Be(CfThresholdType.Max,
            "saving and reloading an explicit Highest Value data bar on a current build must never clobber it to Automatic");
    }

    [Fact]
    public void Load_V2SchemaColorScaleMinThreshold_IsNeverRemapped()
    {
        // Sibling no-regression: the schema-version gate must not somehow start touching color-scale
        // thresholds -- they have no Automatic concept and always use Min/Max literally, regardless
        // of the loaded file's schema version.
        const string json = """
            {
              "SchemaVersion": 2,
              "Name": "Book1",
              "Sheets": [
                {
                  "Name": "Sheet1",
                  "ConditionalFormats": [
                    {
                      "AppliesTo": "A1:A5",
                      "RuleType": 1,
                      "Operator": 0,
                      "MinThresholdType": 0,
                      "MaxThresholdType": 1
                    }
                  ]
                }
              ]
            }
            """;

        var workbook = LoadJson(json);

        var rule = workbook.GetSheetAt(0).ConditionalFormats.Should().ContainSingle().Subject;
        rule.MinThresholdType.Should().Be(CfThresholdType.Min,
            "color-scale thresholds have no Automatic concept and must not be remapped regardless of schema version");
        rule.MaxThresholdType.Should().Be(CfThresholdType.Max,
            "color-scale thresholds have no Automatic concept and must not be remapped regardless of schema version");
    }
}
