using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

// Round-100 finding: the accessibility checker's conditional-format contrast checker
// (AccessibilityCheckerService.Contrast.cs) carries its own independent shadow date-formula
// evaluator (FormulaExcelSerialToDate/FormulaDateToExcelSerial/TrySerialToDate/
// TryCreateFormulaDateValue) that must honor the workbook's Uses1904DateSystem flag the same
// way the real Core.Formula evaluator (FreeX.Core.Formula.ExcelDateSystem, used by
// BuiltInFunctions.DateTime.cs and the XLSX cell mapper) does, or it silently mis-reports
// (false negative) low-contrast conditional-format issues for date-bearing formula rules in
// any 1904-date-system workbook.
public sealed class R100_AccessibilityCheckerServiceContrastDate1904Tests
{
    private static readonly DateTime Target = new(2024, 6, 15);

    private static CellStyle CreateLowContrastCellStyle() => new()
    {
        FontColor = new CellColor(120, 120, 120),
        FillColor = new CellColor(130, 130, 130)
    };

    // Mirrors XlsxClosedXmlCellMapper's convention (see its file header, lines 14-22): for a
    // 1904-system workbook, a cell's stored numeric serial is the day-count since 1904-01-01
    // (not the default 1900-epoch OADate).
    private static double SerialFor(DateTime date, bool uses1904DateSystem) =>
        uses1904DateSystem
            ? (date - new DateTime(1904, 1, 1)).TotalDays
            : date.ToOADate();

    private static Workbook CreateWorkbook(bool uses1904DateSystem, out Sheet sheet, out CellAddress target)
    {
        var workbook = new Workbook("Accessibility") { Uses1904DateSystem = uses1904DateSystem };
        sheet = workbook.AddSheet("Sales");
        var source = new CellAddress(sheet.Id, 1, 1); // A1
        target = new CellAddress(sheet.Id, 1, 2); // B1

        sheet.SetCell(source, new DateTimeValue(SerialFor(Target, uses1904DateSystem)));
        sheet.SetCell(target, new TextValue("Value"));

        return workbook;
    }

    private static List<AccessibilityIssue> FindLowContrastCellTextIssues(Workbook workbook) =>
        AccessibilityCheckerService.FindIssues(workbook)
            .Where(issue => issue.Kind == AccessibilityIssueKind.LowContrastCellText)
            .ToList();

    private static void AddFormulaContrastRule(Sheet sheet, CellAddress target, string formulaText)
    {
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(target, target),
            RuleType = CfRuleType.Formula,
            FormulaText = formulaText,
            FormatIfTrue = CreateLowContrastCellStyle()
        });
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatDateEquality_InUses1904Workbook()
    {
        // The cell's stored serial is 1904-epoch-relative (as the XLSX cell mapper writes it
        // for a 1904-system workbook). DATE(2024,6,15) must resolve to that same 1904-relative
        // serial -- not the default 1900-epoch OADate -- for "$A1=DATE(...)" to evaluate TRUE,
        // matching Excel and the real Core.Formula evaluator.
        //
        // Before the fix, TryCreateFormulaDateValue/FormulaExcelSerialToDate hardcoded the
        // 1900 OLE-Automation epoch regardless of workbook.Uses1904DateSystem, so DATE()
        // produced a serial 1462 days off from the stored cell value -- the comparison came
        // out FALSE and no issue was reported here.
        var workbook = CreateWorkbook(uses1904DateSystem: true, out var sheet, out var target);
        AddFormulaContrastRule(sheet, target, "$A1=DATE(2024,6,15)");

        FindLowContrastCellTextIssues(workbook)
            .Select(issue => issue.Location)
            .Should()
            .Equal("B1");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatYearMatch_InUses1904Workbook()
    {
        // Sibling coverage for the YEAR() path (TryResolveFormulaFunctionDate ->
        // TrySerialToDate), which must also resolve the stored 1904-relative serial back to
        // the correct calendar date.
        var workbook = CreateWorkbook(uses1904DateSystem: true, out var sheet, out var target);
        AddFormulaContrastRule(sheet, target, "YEAR($A1)=2024");

        FindLowContrastCellTextIssues(workbook)
            .Select(issue => issue.Location)
            .Should()
            .Equal("B1");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatDateEquality_InDefault1900Workbook()
    {
        // No-regression sibling: the ordinary (default, non-1904) workbook date system must
        // keep working exactly as before -- the fix must not disturb the common case.
        var workbook = CreateWorkbook(uses1904DateSystem: false, out var sheet, out var target);
        AddFormulaContrastRule(sheet, target, "$A1=DATE(2024,6,15)");

        FindLowContrastCellTextIssues(workbook)
            .Select(issue => issue.Location)
            .Should()
            .Equal("B1");
    }

    [Fact]
    public void FindIssues_DoesNotMatchFormulaConditionalFormatDateEquality_WhenSerialsUseMismatchedEpoch()
    {
        // Cross-check: a cell holding a 1900-epoch serial for the same calendar date must NOT
        // match in a 1904-system workbook (the two epochs are genuinely different numbers),
        // confirming the assertions above aren't accidentally passing for an unrelated reason.
        var workbook = new Workbook("Accessibility") { Uses1904DateSystem = true };
        var sheet = workbook.AddSheet("Sales");
        var source = new CellAddress(sheet.Id, 1, 1); // A1
        var target = new CellAddress(sheet.Id, 1, 2); // B1

        sheet.SetCell(source, new DateTimeValue(Target.ToOADate())); // wrong epoch on purpose
        sheet.SetCell(target, new TextValue("Value"));
        AddFormulaContrastRule(sheet, target, "$A1=DATE(2024,6,15)");

        FindLowContrastCellTextIssues(workbook).Should().BeEmpty();
    }
}
