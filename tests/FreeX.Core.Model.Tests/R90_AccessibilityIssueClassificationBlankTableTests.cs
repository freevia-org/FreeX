using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R90-app-accessibility-checker-5-1: <see cref="AccessibilityIssueKind.BlankRowOrColumnInTable"/>
/// had no case in <see cref="AccessibilityIssueClassification"/>'s Describe switch and silently fell
/// into the generic "Other accessibility issues" default arm (Tip severity, boilerplate guidance).
/// These tests drive the actual product entry point the WPF/Avalonia Accessibility Checker dialogs
/// use -- <see cref="AccessibilityInspectionResult.Build"/> over <see cref="AccessibilityCheckerService.FindIssues"/>
/// -- rather than calling <see cref="AccessibilityIssueClassification.Describe"/> directly.
/// </summary>
public sealed class R90_AccessibilityIssueClassificationBlankTableTests
{
    private static Workbook BuildWorkbookWithBlankTableRow()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Sales");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Quarter"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Sales"));

        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("Q1"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(100));

        // Row 3 (the last data-body row) is entirely blank across all 3 table columns.

        sheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 3)),
            HeaderRowCount = 1,
            HasAutoFilter = true,
        });

        return workbook;
    }

    [Fact]
    public void Build_GivesBlankTableRowIssueItsOwnDedicatedGroup_NotTheGenericOtherBucket()
    {
        var workbook = BuildWorkbookWithBlankTableRow();
        var issues = AccessibilityCheckerService.FindIssues(workbook);
        issues.Should().Contain(i => i.Kind == AccessibilityIssueKind.BlankRowOrColumnInTable);

        var sections = AccessibilityInspectionResult.Build(issues);
        var group = sections
            .SelectMany(section => section.Groups)
            .Should().ContainSingle(g => g.Items.Any(item => item.Issue.Kind == AccessibilityIssueKind.BlankRowOrColumnInTable))
            .Subject;

        group.Descriptor.Label.Should().NotBe("Other accessibility issues");
        group.Descriptor.Label.Should().Be("Blank rows or columns in a table");

        var containingSection = sections.Should().ContainSingle(s => s.Groups.Contains(group)).Subject;
        containingSection.Severity.Should().Be(AccessibilitySeverity.Warning);
    }

    [Fact]
    public void Describe_ReturnsDedicatedDescriptorForBlankRowOrColumnInTable()
    {
        // No-regression sibling: pins the classification's own contract directly (in addition to the
        // FindIssues/Build product-path test above), so a future refactor of Describe's switch cannot
        // silently regress the case back into the default arm without a test noticing here too.
        var descriptor = AccessibilityIssueClassification.Describe(AccessibilityIssueKind.BlankRowOrColumnInTable);

        descriptor.Severity.Should().Be(AccessibilitySeverity.Warning);
        descriptor.Label.Should().Be("Blank rows or columns in a table");
        descriptor.LabelKey.Should().Be("AccessibilityChecker_GroupBlankTableRowOrColumn");
    }

    [Fact]
    public void Describe_StillReturnsGenericOtherBucketForAnUnmappedFutureKind()
    {
        // No-regression sibling: the default `_ =>` arm must still exist and still classify as the
        // generic Tip bucket for any AccessibilityIssueKind that genuinely has no dedicated case (this
        // fix must not have removed/altered the default fallback itself).
        var descriptor = AccessibilityIssueClassification.Describe((AccessibilityIssueKind)(-1));

        descriptor.Severity.Should().Be(AccessibilitySeverity.Tip);
        descriptor.Label.Should().Be("Other accessibility issues");
    }
}
