using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.ConditionalFormatting;

/// <summary>
/// R33-commands-conditionalformat-manage-1: <c>CloneConditionalFormatForSheet</c> used to be a
/// hand-maintained, un-mirrored twin of <see cref="ConditionalFormat.Clone"/> that silently
/// dropped icon overrides, color-scale/dataBar theme provenance, and <c>EqualAverage</c>/
/// <c>StdDevCount</c>. It now delegates to <see cref="ConditionalFormat.Clone"/> so every semantic
/// field is preserved. This is a fan-out of a rule to ANOTHER sheet, so the copy correctly still
/// gets a FRESH Id and has its sheet-specific x14 extLst id stripped (Clone(newId)) — two sheets
/// must not share the same x14 CF id.
/// </summary>
public sealed class GroupedSheetRangePlannerConditionalFormatCloneTests
{
    [Fact]
    public void CloneConditionalFormatForSheet_PreservesIconOverridesThemedColorScaleAndId()
    {
        var sourceSheet = SheetId.New();
        var targetSheet = SheetId.New();
        var ruleId = Guid.NewGuid();
        var source = new ConditionalFormat
        {
            Id = ruleId,
            AppliesTo = new GridRange(new CellAddress(sourceSheet, 1, 1), new CellAddress(sourceSheet, 4, 2)),
            RuleType = CfRuleType.IconSet,
            IconSetStyle = "3TrafficLights1",
            UseThreeColorScale = true,
            MinColorSource = new CfColorStopSource(ThemeIndex: 4, Tint: 0.2),
            MidColorSource = new CfColorStopSource(ThemeIndex: 5),
            MaxColorSource = new CfColorStopSource(ThemeIndex: 6, Tint: -0.1),
            DataBarColorSource = new CfColorStopSource(ThemeIndex: 2),
            DataBarBorder = true,
            DataBarBorderColor = new RgbColor(10, 20, 30),
            DataBarDirection = "rightToLeft",
            EqualAverage = true,
            StdDevCount = 2,
            NativeChildXmls =
            [
                """<extLst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><ext><x14:id xmlns:x14="http://schemas.microsoft.com/office/spreadsheetml/2009/9/main">{11111111-2222-3333-4444-555555555555}</x14:id></ext></extLst>""",
                """<future xmlns="urn:future" value="kept" />"""
            ]
        };
        source.IconOverrides.Add(new CfIconOverride("3Symbols", 1));
        source.IconOverrides.Add(new CfIconOverride("NoIcons", 0));

        var clone = GroupedSheetRangePlanner.CloneConditionalFormatForSheet(source, targetSheet);

        // The copy gets a FRESH id (fan-out to another sheet must not share the source's x14 CF id).
        clone.Id.Should().NotBe(ruleId);
        clone.NativeChildXmls.Should().NotContain(xml => xml.Contains(
            "11111111-2222-3333-4444-555555555555",
            StringComparison.Ordinal));
        clone.NativeChildXmls.Should().Contain(xml => xml.Contains("urn:future", StringComparison.Ordinal));

        // Icon overrides (previously dropped entirely).
        clone.IconOverrides.Should().HaveCount(2);
        clone.IconOverrides.Should().ContainInOrder(source.IconOverrides);
        clone.IconOverrides.Should().NotBeSameAs(source.IconOverrides);

        // Color-scale / dataBar theme provenance (previously dropped, degrading a themed color
        // scale to plain sRGB on every Manage-Rules OK/Apply).
        clone.MinColorSource.Should().Be(source.MinColorSource);
        clone.MidColorSource.Should().Be(source.MidColorSource);
        clone.MaxColorSource.Should().Be(source.MaxColorSource);
        clone.DataBarColorSource.Should().Be(source.DataBarColorSource);
        clone.DataBarBorderColor.Should().Be(source.DataBarBorderColor);
        clone.DataBarDirection.Should().Be(source.DataBarDirection);
        clone.EqualAverage.Should().Be(source.EqualAverage);
        clone.StdDevCount.Should().Be(source.StdDevCount);

        // Range remapping still happens.
        clone.AppliesTo.Start.Sheet.Should().Be(targetSheet);
        clone.AppliesTo.End.Sheet.Should().Be(targetSheet);
    }

    [Fact]
    public void CloneConditionalFormatForSheet_PlainCellValueRuleWithoutThemeSources_StillClonesCorrectly()
    {
        // Sibling already-working case: a plain rule with no theme provenance / icon overrides
        // must keep behaving exactly as before (remapped range, independent FormatIfTrue clone,
        // scalar fields copied).
        var sourceSheet = SheetId.New();
        var targetSheet = SheetId.New();
        var source = new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sourceSheet, 1, 1), new CellAddress(sourceSheet, 4, 2)),
            Priority = 3,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "10",
            FormatIfTrue = new CellStyle { Bold = true, FillColor = new CellColor(1, 2, 3) },
            StopIfTrue = true
        };

        var clone = GroupedSheetRangePlanner.CloneConditionalFormatForSheet(source, targetSheet);

        clone.Should().NotBeSameAs(source);
        clone.Id.Should().NotBe(source.Id);
        clone.AppliesTo.Start.Sheet.Should().Be(targetSheet);
        clone.AppliesTo.End.Sheet.Should().Be(targetSheet);
        clone.Priority.Should().Be(3);
        clone.RuleType.Should().Be(CfRuleType.CellValue);
        clone.Operator.Should().Be(CfOperator.GreaterThan);
        clone.Value1.Should().Be("10");
        clone.StopIfTrue.Should().BeTrue();
        clone.FormatIfTrue.Should().NotBeSameAs(source.FormatIfTrue);
        clone.FormatIfTrue.Should().Be(source.FormatIfTrue);
        clone.MinColorSource.Should().BeNull();
        clone.IconOverrides.Should().BeEmpty();
    }

    [Fact]
    public void CloneConditionalFormatForSheet_PreserveIdentity_RemapsAllRangesWithoutStrippingX14Identity()
    {
        var sourceSheet = SheetId.New();
        var targetSheet = SheetId.New();
        var ruleId = Guid.NewGuid();
        var nativeChildren = new[]
        {
            """<extLst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><ext><x14:id xmlns:x14="http://schemas.microsoft.com/office/spreadsheetml/2009/9/main">{AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE}</x14:id></ext></extLst>"""
        };
        var source = new ConditionalFormat
        {
            Id = ruleId,
            AppliesTo = new GridRange(
                new CellAddress(sourceSheet, 2, 3),
                new CellAddress(sourceSheet, 5, 4)),
            AdditionalRanges =
            [
                new GridRange(
                    new CellAddress(sourceSheet, 8, 1),
                    new CellAddress(sourceSheet, 10, 2))
            ],
            RuleType = CfRuleType.IconSet,
            NativeChildXmls = nativeChildren
        };
        source.IconOverrides.Add(new CfIconOverride("3Arrows", 1));

        var clone = GroupedSheetRangePlanner.CloneConditionalFormatForSheet(
            source,
            targetSheet,
            preserveIdentity: true);

        clone.Id.Should().Be(ruleId);
        clone.AppliesTo.Start.Sheet.Should().Be(targetSheet);
        clone.AppliesTo.End.Sheet.Should().Be(targetSheet);
        clone.AdditionalRanges.Should().ContainSingle();
        clone.AdditionalRanges![0].Start.Sheet.Should().Be(targetSheet);
        clone.AdditionalRanges[0].End.Sheet.Should().Be(targetSheet);
        clone.AdditionalRanges.Should().NotBeSameAs(source.AdditionalRanges);
        clone.IconOverrides.Should().Equal(source.IconOverrides);
        clone.IconOverrides.Should().NotBeSameAs(source.IconOverrides);
        clone.NativeChildXmls.Should().BeSameAs(nativeChildren);
    }

    [Fact]
    public void ConditionalFormatCommandPlanner_DelegatesSheetCloneAndIdentityPolicyToGroupedPlanner()
    {
        var presentationRoot = RepositoryFileLocator.FindDirectory("src", "FreeX.App.Presentation");
        var groupedSource = File.ReadAllText(Path.Combine(presentationRoot, "GroupedSheetRangePlanner.cs"));
        var commandSource = File.ReadAllText(Path.Combine(
            presentationRoot,
            "ConditionalFormatting",
            "ConditionalFormatCommandPlanner.cs"));

        groupedSource.Should().Contain("bool preserveIdentity");
        groupedSource.Should().Contain("preserveIdentity ? null : Guid.NewGuid()");
        commandSource.Should().Contain("GroupedSheetRangePlanner.CloneConditionalFormatForSheet(");
        commandSource.Should().Contain("preserveIdentity: preserveIdentity");
        commandSource.Should().NotContain("private static ConditionalFormat CloneForSheet");
    }
}
