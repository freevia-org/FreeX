using System.Globalization;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Round-12 bucket Q5 regression tests:
///   R12-xlsx-data-validation-3 — inline numeric List validation must match Excel's locale-independent
///     value comparison (not CurrentCulture-formatted text).
///   R12-conditional-format-deep-1 — Paste Special of conditional formats must carry over StdDevCount,
///     EqualAverage, IconOverrides, and the theme color-stop sources, matching the authoritative
///     ConditionalFormat.Clone().
/// </summary>
public sealed class FreeXR12Q5Tests
{
    [Fact]
    public void ValidateList_InlineNumericList_MatchesByValueUnderCommaDecimalLocale()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            // de-DE formats 1.5 as "1,5" — the inline list text ("1.5,2.5,3.5") is raw invariant text
            // taken verbatim from Formula1 and never reformatted, so validation must compare by value
            // (invariant round-trip), exactly like Excel, not by CurrentCulture-formatted text.
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

            var sheetId = SheetId.New();
            var rule = new DataValidation
            {
                Type = DvType.List,
                Formula1 = "1.5,2.5,3.5",
                AppliesTo = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 1, 1)),
            };

            DataValidationService.Validate(rule, new NumberValue(1.5)).Should().BeNull();
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [Fact]
    public void PasteConditionalFormatsCommand_PreservesStdDevEqualAverageIconOverridesAndColorSources()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        var source = new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 1)),
            RuleType = CfRuleType.AboveAverage,
            AboveAverage = true,
            EqualAverage = true,
            StdDevCount = 2,
            MinColorSource = new CfColorStopSource(3, 0.25),
            MidColorSource = new CfColorStopSource(4, 0.0),
            MaxColorSource = new CfColorStopSource(5, -0.25),
            DataBarColorSource = new CfColorStopSource(6, 0.5),
        };
        source.IconOverrides.AddRange(
        [
            new CfIconOverride("3TrafficLights1", 0),
            new CfIconOverride("3Arrows", 2),
        ]);
        sheet.ConditionalFormats.Add(source);
        var sourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 1));

        new PasteConditionalFormatsCommand(sheet.Id, sourceRange, new CellAddress(sheet.Id, 10, 3), transpose: false)
            .Apply(new TestCommandContext(workbook));

        var pasted = sheet.ConditionalFormats.Should().HaveCount(2).And.Subject.Last();
        pasted.EqualAverage.Should().BeTrue();
        pasted.StdDevCount.Should().Be(2);
        pasted.MinColorSource.Should().Be(new CfColorStopSource(3, 0.25));
        pasted.MidColorSource.Should().Be(new CfColorStopSource(4, 0.0));
        pasted.MaxColorSource.Should().Be(new CfColorStopSource(5, -0.25));
        pasted.DataBarColorSource.Should().Be(new CfColorStopSource(6, 0.5));
        pasted.IconOverrides.Should().Equal(
            new CfIconOverride("3TrafficLights1", 0),
            new CfIconOverride("3Arrows", 2));
    }
}
