using System;
using System.Globalization;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Regression coverage for review finding R57-services-clipboard-formats-5-4: external-paste date
/// detection (LooksLikePasteDateCandidate, via ParseClipboardValue) previously required an explicit
/// year (>= 3 '/'/'-'-separated digit groups) before treating a candidate as a date, so a common
/// 2-part "M/D" clipboard paste with no year (e.g. "3/4", "12/25") was left as literal text instead
/// of becoming a date defaulting to the current year -- Excel's well-known typed/pasted-entry
/// behavior for this exact input shape.
/// </summary>
public sealed class PasteCommandFactoryExternalDateCandidateTests
{
    /// <summary>
    /// Pins CurrentCulture to en-US for the duration of a test so the '/' date-separator and
    /// month/day parsing are deterministic regardless of the host machine's ambient locale.
    /// </summary>
    private sealed class CurrentCultureScope : IDisposable
    {
        private readonly CultureInfo _previous = CultureInfo.CurrentCulture;

        public CurrentCultureScope(string cultureName)
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
        }

        public void Dispose() => CultureInfo.CurrentCulture = _previous;
    }

    [Theory]
    [InlineData("3/4", 3, 4)]
    [InlineData("12/25", 12, 25)]
    public void ExternalTextPaste_TwoPartSlashDateWithNoYearBecomesDateInCurrentYear(string text, int month, int day)
    {
        using var _ = new CurrentCultureScope("en-US");
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var address = new CellAddress(sheet.Id, 1, 1);

        var command = PasteCommandFactory.CreateExternalTextPasteCommand(sheet.Id, address, [[text]]);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        var expected = DateTimeValue.FromDateTime(new DateTime(DateTime.Now.Year, month, day));
        sheet.GetValue(address).Should().Be(expected);
    }

    [Fact]
    public void ExternalTextPaste_ThreePartSlashDateWithExplicitYearStillParsesAsDate()
    {
        // Sibling no-regression test: the pre-existing >= 3 digit-group (explicit year) date form
        // must keep parsing exactly as before now that the digit-group-count gate has been relaxed
        // to just "has a date separator" (digitGroups >= 2 is already guaranteed by the earlier
        // guard, so a 3-group date is unaffected by the relaxation).
        using var _ = new CurrentCultureScope("en-US");
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var address = new CellAddress(sheet.Id, 1, 1);

        var command = PasteCommandFactory.CreateExternalTextPasteCommand(sheet.Id, address, [["6/15/2026"]]);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(address).Should().Be(DateTimeValue.FromDateTime(new DateTime(2026, 6, 15)));
    }

    [Fact]
    public void ExternalTextPaste_CommaSeparatedTwoGroupTextWithoutDateSeparatorStaysText()
    {
        // Sibling no-regression test: a 2-digit-group candidate whose separator is NOT a recognized
        // date separator (comma, in en-US where '/' is the date separator) must still be rejected as
        // a date candidate and fall through to malformed-thousands-grouping text, exactly as before.
        using var _ = new CurrentCultureScope("en-US");
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var address = new CellAddress(sheet.Id, 1, 1);

        var command = PasteCommandFactory.CreateExternalTextPasteCommand(sheet.Id, address, [["12,34"]]);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(address).Should().Be(new TextValue("12,34"));
    }
}
