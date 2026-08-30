using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;
using System.Diagnostics;

namespace FreeX.Core.Calc.Tests;

public partial class DataValidationTests
{
    [Fact]
    public void GetInvalidEntryAction_ReturnsBlockForStopAlert()
    {
        var dv = new DataValidation { AlertStyle = DvAlertStyle.Stop, ShowErrorMessage = true };

        DataValidationService.GetInvalidEntryAction(dv)
            .Should().Be(DataValidationInvalidEntryAction.Block);
    }

    [Fact]
    public void GetInvalidEntryAction_ReturnsAllowForHiddenErrorAlert()
    {
        var dv = new DataValidation { AlertStyle = DvAlertStyle.Stop, ShowErrorMessage = false };

        DataValidationService.GetInvalidEntryAction(dv)
            .Should().Be(DataValidationInvalidEntryAction.Allow);
    }

    [Fact]
    public void Validate_WholeNumber_Between_AcceptsInRange()
    {
        var dv = new DataValidation
        {
            Type = DvType.WholeNumber,
            Operator = DvOperator.Between,
            Formula1 = "1",
            Formula2 = "10",
            AllowBlank = true,
        };

        var result = DataValidationService.Validate(dv, new NumberValue(5));

        result.Should().BeNull("5 is between 1 and 10");
    }

    [Fact]
    public void Validate_WholeNumber_Between_RejectsOutOfRange()
    {
        var dv = new DataValidation
        {
            Type = DvType.WholeNumber,
            Operator = DvOperator.Between,
            Formula1 = "1",
            Formula2 = "10",
            AllowBlank = true,
        };

        var result = DataValidationService.Validate(dv, new NumberValue(15));

        result.Should().NotBeNull("15 is outside the range 1-10");
    }

    [Fact]
    public void Validate_WholeNumber_RejectsDecimalValueInsideRange()
    {
        var dv = new DataValidation
        {
            Type = DvType.WholeNumber,
            Operator = DvOperator.Between,
            Formula1 = "1",
            Formula2 = "10",
            AllowBlank = true,
        };

        var result = DataValidationService.Validate(dv, new NumberValue(5.5));

        result.Should().NotBeNull("Excel whole-number validation rejects decimal values even when they are in range");
    }

    [Fact]
    public void Validate_WholeNumber_GreaterThan_AcceptsLargerValue()
    {
        var dv = new DataValidation
        {
            Type = DvType.WholeNumber,
            Operator = DvOperator.GreaterThan,
            Formula1 = "0",
            AllowBlank = true,
        };

        var result = DataValidationService.Validate(dv, new NumberValue(1));

        result.Should().BeNull("1 > 0");
    }

    [Fact]
    public void Validate_WholeNumber_GreaterThan_RejectsSmallerValue()
    {
        var dv = new DataValidation
        {
            Type = DvType.WholeNumber,
            Operator = DvOperator.GreaterThan,
            Formula1 = "0",
            AllowBlank = true,
        };

        var result = DataValidationService.Validate(dv, new NumberValue(-1));

        result.Should().NotBeNull("-1 is not > 0");
    }

    // ─── TextLength validation ────────────────────────────────────────────────

    [Fact]
    public void Validate_TextLength_LessThan_AcceptsShortText()
    {
        var dv = new DataValidation
        {
            Type = DvType.TextLength,
            Operator = DvOperator.LessThan,
            Formula1 = "10",
            AllowBlank = true,
        };

        var result = DataValidationService.Validate(dv, new TextValue("Hi"));

        result.Should().BeNull("'Hi' has length 2 which is < 10");
    }

    [Fact]
    public void Validate_TextLength_LessThan_RejectsLongText()
    {
        var dv = new DataValidation
        {
            Type = DvType.TextLength,
            Operator = DvOperator.LessThan,
            Formula1 = "5",
            AllowBlank = true,
        };

        var result = DataValidationService.Validate(dv, new TextValue("Hello World"));

        result.Should().NotBeNull("'Hello World' has length 11 which is not < 5");
    }

    // ─── Any validation ───────────────────────────────────────────────────────

    [Fact]
    public void Validate_Any_AlwaysAccepts()
    {
        var dv = new DataValidation { Type = DvType.Any };

        DataValidationService.Validate(dv, new NumberValue(99)).Should().BeNull();
        DataValidationService.Validate(dv, new TextValue("x")).Should().BeNull();
        DataValidationService.Validate(dv, BlankValue.Instance).Should().BeNull();
    }

    // ─── GetApplicable ────────────────────────────────────────────────────────

    [Fact]
    public void Validate_Decimal_Between_AcceptsInRange()
    {
        var dv = new DataValidation
        {
            Type = DvType.Decimal,
            Operator = DvOperator.Between,
            Formula1 = "0.5",
            Formula2 = "9.5",
            AllowBlank = true,
        };

        DataValidationService.Validate(dv, new NumberValue(5.0)).Should().BeNull("5.0 is between 0.5 and 9.5");
        DataValidationService.Validate(dv, new NumberValue(10.0)).Should().NotBeNull("10.0 is outside the range");
    }

    // ─── minimal test helpers ─────────────────────────────────────────────────

    [Fact]
    public void Validate_Date_Between_ParsesIsoDateBounds()
    {
        var dv = new DataValidation
        {
            Type = DvType.Date,
            Operator = DvOperator.Between,
            Formula1 = "2026-05-01",
            Formula2 = "2026-05-31",
            AllowBlank = true,
        };

        DataValidationService.Validate(dv, DateTimeValue.FromDateTime(new DateTime(2026, 5, 15)))
            .Should().BeNull("May 15 is within the May date validation window");
        DataValidationService.Validate(dv, DateTimeValue.FromDateTime(new DateTime(2026, 6, 1)))
            .Should().NotBeNull("June 1 is outside the May date validation window");
    }

    [Fact]
    public void Validate_Time_Between_ParsesClockTimeBounds()
    {
        var dv = new DataValidation
        {
            Type = DvType.Time,
            Operator = DvOperator.Between,
            Formula1 = "09:00",
            Formula2 = "17:30",
            AllowBlank = true,
        };

        DataValidationService.Validate(dv, new NumberValue(new TimeSpan(10, 30, 0).TotalDays))
            .Should().BeNull("10:30 is within the workday validation window");
        DataValidationService.Validate(dv, new NumberValue(new TimeSpan(18, 0, 0).TotalDays))
            .Should().NotBeNull("18:00 is outside the workday validation window");
    }

    [Fact]
    public void Validate_Date_GreaterThan_PreservesOperatorAndCustomError()
    {
        var dv = new DataValidation
        {
            Type = DvType.Date,
            Operator = DvOperator.GreaterThan,
            Formula1 = "2026-05-01",
            ErrorMessage = "Choose a later date.",
        };

        DataValidationService.Validate(dv, DateTimeValue.FromDateTime(new DateTime(2026, 5, 2)))
            .Should().BeNull("May 2 is later than the resolved May 1 bound");
        DataValidationService.Validate(dv, DateTimeValue.FromDateTime(new DateTime(2026, 5, 1)))
            .Should().Be("Choose a later date.", "an equal date fails the strict comparison");
    }

    [Fact]
    public void Validate_Time_NotBetween_NormalizesFractionalDayAndPreservesCustomError()
    {
        var dv = new DataValidation
        {
            Type = DvType.Time,
            Operator = DvOperator.NotBetween,
            Formula1 = "09:00",
            Formula2 = "17:30",
            ErrorMessage = "Choose a time outside working hours.",
        };

        DataValidationService.Validate(dv, new NumberValue(2 + new TimeSpan(10, 30, 0).TotalDays))
            .Should().Be("Choose a time outside working hours.", "the whole-day portion is ignored for time rules");
        DataValidationService.Validate(dv, new NumberValue(2 + new TimeSpan(18, 0, 0).TotalDays))
            .Should().BeNull("18:00 is outside the excluded window after fractional-day normalization");
    }

    [Theory]
    [InlineData(DvType.Date, "not-a-date")]
    [InlineData(DvType.Time, "not-a-time")]
    public void Validate_DateOrTime_WithMalformedBound_IsTreatedAsValid(DvType type, string malformedBound)
    {
        var dv = new DataValidation
        {
            Type = type,
            Operator = DvOperator.Equal,
            Formula1 = malformedBound,
        };

        DataValidationService.Validate(dv, new NumberValue(0.5))
            .Should().BeNull("an unresolved validation bound cannot be enforced");
    }

    [Fact]
    public void Validate_Time_GreaterThan_ResolvesAndNormalizesContextualBound()
    {
        var (workbook, sheet) = MakeWorkbook();
        var boundAddress = new CellAddress(sheet.Id, 1, 1);
        var targetAddress = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(
            boundAddress,
            Cell.FromValue(DateTimeValue.FromDateTime(new DateTime(2026, 5, 1, 9, 0, 0))));

        var dv = new DataValidation
        {
            Type = DvType.Time,
            Operator = DvOperator.GreaterThan,
            Formula1 = "=A1",
        };

        DataValidationService.Validate(
                dv,
                new NumberValue(new TimeSpan(10, 0, 0).TotalDays),
                sheet,
                targetAddress,
                workbook)
            .Should().BeNull("10:00 is later than the 09:00 time resolved from A1");
        DataValidationService.Validate(
                dv,
                new NumberValue(new TimeSpan(8, 0, 0).TotalDays),
                sheet,
                targetAddress,
                workbook)
            .Should().NotBeNull("08:00 is earlier than the 09:00 time resolved from A1");
    }

    [BenchmarkFact]
    public void Benchmark_ValidateResolvedDateAndTimeBounds_ReportsTimingAndAllocation()
    {
        const int iterations = 100_000;
        var dateRule = new DataValidation
        {
            Type = DvType.Date,
            Operator = DvOperator.Between,
            Formula1 = "2026-01-01",
            Formula2 = "2026-12-31",
        };
        var timeRule = new DataValidation
        {
            Type = DvType.Time,
            Operator = DvOperator.Between,
            Formula1 = "09:00",
            Formula2 = "17:30",
        };
        var dateValue = DateTimeValue.FromDateTime(new DateTime(2026, 8, 30));
        var timeValue = new NumberValue(new TimeSpan(12, 0, 0).TotalDays);

        DataValidationService.Validate(dateRule, dateValue).Should().BeNull();
        DataValidationService.Validate(timeRule, timeValue).Should().BeNull();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var validCount = 0;
        var stopwatch = Stopwatch.StartNew();
        for (var iteration = 0; iteration < iterations; iteration++)
        {
            if (DataValidationService.Validate(dateRule, dateValue) is null)
                validCount++;
            if (DataValidationService.Validate(timeRule, timeValue) is null)
                validCount++;
        }
        stopwatch.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        validCount.Should().Be(iterations * 2);
        Console.WriteLine(
            "PERF DATAVALIDATION_RESOLVED_BOUNDS " +
            $"validations={iterations * 2} total_ms={stopwatch.Elapsed.TotalMilliseconds:F2} " +
            $"mean_ns={stopwatch.Elapsed.TotalNanoseconds / (iterations * 2):F2} " +
            $"allocated_bytes={allocatedBytes:N0}");
    }

    // ─── WholeNumber bounds resolved from cell references (F11) ───────────────
    //
    // Excel allows Formula1/Formula2 to be cell references (or arbitrary formulas)
    // rather than literal numbers. The rule must be evaluated against the current
    // value of the referenced cells, not silently treated as "always valid".

    [Fact]
    public void Validate_WholeNumber_Between_WithCellReferenceBounds_RejectsBelowRange()
    {
        var (workbook, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(1)));  // A1 = 1
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new NumberValue(10))); // A2 = 10
        var target = new CellAddress(sheet.Id, 3, 1); // A3 — the cell being validated

        var dv = new DataValidation
        {
            Type = DvType.WholeNumber,
            Operator = DvOperator.Between,
            Formula1 = "=A1",
            Formula2 = "=A2",
            AllowBlank = true,
        };

        DataValidationService.Validate(dv, new NumberValue(0), sheet, target, workbook)
            .Should().NotBeNull("0 is below the referenced lower bound A1=1");
    }

    [Fact]
    public void Validate_WholeNumber_Between_WithCellReferenceBounds_RejectsAboveRange()
    {
        var (workbook, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(1)));  // A1 = 1
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new NumberValue(10))); // A2 = 10
        var target = new CellAddress(sheet.Id, 3, 1); // A3

        var dv = new DataValidation
        {
            Type = DvType.WholeNumber,
            Operator = DvOperator.Between,
            Formula1 = "=A1",
            Formula2 = "=A2",
            AllowBlank = true,
        };

        DataValidationService.Validate(dv, new NumberValue(11), sheet, target, workbook)
            .Should().NotBeNull("11 is above the referenced upper bound A2=10");
    }

    [Fact]
    public void Validate_WholeNumber_Between_WithCellReferenceBounds_AcceptsInRange()
    {
        var (workbook, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(1)));  // A1 = 1
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new NumberValue(10))); // A2 = 10
        var target = new CellAddress(sheet.Id, 3, 1); // A3

        var dv = new DataValidation
        {
            Type = DvType.WholeNumber,
            Operator = DvOperator.Between,
            Formula1 = "=A1",
            Formula2 = "=A2",
            AllowBlank = true,
        };

        DataValidationService.Validate(dv, new NumberValue(5), sheet, target, workbook)
            .Should().BeNull("5 is between the referenced bounds A1=1 and A2=10");
    }

    [Fact]
    public void Validate_WholeNumber_Between_CellReferenceBounds_UnregisteredRule_EvaluatesInPlace()
    {
        // R131 regression: the bound formula is shifted from the rule's AppliesTo.Start anchor so a
        // multi-cell rule re-anchors per validated cell. But AppliesTo is a non-nullable GridRange,
        // so a rule constructed standalone (never registered against a range -- as every ad-hoc
        // validation does) reports Start as the DEFAULT CellAddress: row 0, col 0. That is not a
        // cell, yet it is not null, so it was accepted as the anchor and shifted "=A1" off the grid,
        // collapsing the bounds to "0 and 0" and rejecting every value. Pin that an out-of-grid
        // anchor means "no anchor": evaluate the bound where it stands.
        var (workbook, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(1)));  // A1 = 1
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new NumberValue(10))); // A2 = 10
        var target = new CellAddress(sheet.Id, 3, 1); // A3

        var dv = new DataValidation
        {
            Type = DvType.WholeNumber,
            Operator = DvOperator.Between,
            Formula1 = "=A1",
            Formula2 = "=A2",
            AllowBlank = true,
        };

        // AppliesTo deliberately left unset -- this is the defaulted-anchor case.
        dv.AppliesTo.Start.Row.Should().Be(0, "this test is only meaningful while the default anchor is out of grid");

        DataValidationService.Validate(dv, new NumberValue(5), sheet, target, workbook)
            .Should().BeNull("5 is between the referenced bounds A1=1 and A2=10, which must still resolve");
        DataValidationService.Validate(dv, new NumberValue(0), sheet, target, workbook)
            .Should().NotBeNull("0 is below the referenced lower bound A1=1");
        DataValidationService.Validate(dv, new NumberValue(11), sheet, target, workbook)
            .Should().NotBeNull("11 is above the referenced upper bound A2=10");
    }

    [Fact]
    public void Validate_WholeNumber_Between_WithLiteralBounds_StillWorks_WhenSheetContextSupplied()
    {
        // Guards against regressing the existing literal-bound path when the new
        // sheet-context overload is used (as all real callers now do).
        var (workbook, sheet) = MakeWorkbook();
        var target = new CellAddress(sheet.Id, 3, 1);

        var dv = new DataValidation
        {
            Type = DvType.WholeNumber,
            Operator = DvOperator.Between,
            Formula1 = "1",
            Formula2 = "10",
            AllowBlank = true,
        };

        DataValidationService.Validate(dv, new NumberValue(5), sheet, target, workbook)
            .Should().BeNull("5 is between the literal bounds 1 and 10");
        DataValidationService.Validate(dv, new NumberValue(0), sheet, target, workbook)
            .Should().NotBeNull("0 is below the literal lower bound 1");
        DataValidationService.Validate(dv, new NumberValue(11), sheet, target, workbook)
            .Should().NotBeNull("11 is above the literal upper bound 10");
    }

    [Fact]
    public void Validate_WholeNumber_Between_WithCellReferenceBounds_NoContextOverload_TreatsAsValid()
    {
        // Without sheet context there is no way to resolve the reference; the parser
        // must not throw, and the rule should be treated as unenforceable (valid),
        // matching the existing "can't evaluate — treat as valid" contract.
        var dv = new DataValidation
        {
            Type = DvType.WholeNumber,
            Operator = DvOperator.Between,
            Formula1 = "=A1",
            Formula2 = "=A2",
            AllowBlank = true,
        };

        DataValidationService.Validate(dv, new NumberValue(999))
            .Should().BeNull("without sheet context the reference bound cannot be resolved");
    }
}
