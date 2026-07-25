using System.Globalization;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Regression coverage for R86-app-clipboard-interop-5-3: locale-aware grouped-number paste
/// parsing (PasteCommandFactory.TryParseCultureGroupedNumber) hardcoded a 3-digit group size for
/// every group, so a culture whose NumberFormat.NumberGroupSizes is not a uniform {3} -- e.g.
/// en-IN's Indian numbering {3,2} (3 digits nearest the decimal, then repeating groups of 2
/// further left, e.g. "1,23,456") -- rejected a correctly-grouped pasted number as malformed and
/// fell through to literal text instead of the number real Excel would produce under the same
/// regional settings.
/// </summary>
public sealed class R86_ExternalClipboardIndianGroupingTests
{
    /// <summary>Pins CurrentCulture for the duration of a test so locale-dependent parsing assertions
    /// are deterministic regardless of the host machine's ambient locale.</summary>
    private sealed class CurrentCultureScope : IDisposable
    {
        private readonly CultureInfo _previous = CultureInfo.CurrentCulture;

        public CurrentCultureScope(string cultureName)
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
        }

        public void Dispose() => CultureInfo.CurrentCulture = _previous;
    }

    // ── R86-app-clipboard-interop-5-3: non-3-digit (Indian) locale grouping ─────────────────

    [Theory]
    [InlineData("1,23,456", 123456)]
    [InlineData("12,34,56,789", 123456789)]
    [InlineData("1,234", 1234)] // leftmost partial group can still be up to the repeat size (3 here)
    [InlineData("1,23,456.5", 123456.5)]
    public void ExternalTextPaste_EnInCulture_CoercesIndianGroupedNumbers(string text, double expected)
    {
        using var _ = new CurrentCultureScope("en-IN");
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var address = new CellAddress(sheet.Id, 1, 1);

        var command = PasteCommandFactory.CreateExternalTextPasteCommand(sheet.Id, address, [[text]]);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(address).Should().Be(new NumberValue(expected));
    }

    // Sibling no-regression: a malformed en-IN grouping (e.g. the innermost group not exactly 3
    // digits) is still rejected as a number and stays literal text.
    [Theory]
    [InlineData("1,2,456")]
    [InlineData("1,23,45")]
    public void ExternalTextPaste_EnInCulture_RejectsMalformedGroupingAsText(string text)
    {
        using var _ = new CurrentCultureScope("en-IN");
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var address = new CellAddress(sheet.Id, 1, 1);

        var command = PasteCommandFactory.CreateExternalTextPasteCommand(sheet.Id, address, [[text]]);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(address).Should().Be(new TextValue(text));
    }

    // Sibling no-regression: en-US uniform 3-digit grouping keeps working exactly as before,
    // unaffected by reading NumberGroupSizes instead of hardcoding 3.
    [Theory]
    [InlineData("1,234", 1234)]
    [InlineData("1,234,567.5", 1234567.5)]
    public void ExternalTextPaste_EnUsCulture_StillCoercesUniformGroupedNumbers(string text, double expected)
    {
        using var _ = new CurrentCultureScope("en-US");
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var address = new CellAddress(sheet.Id, 1, 1);

        var command = PasteCommandFactory.CreateExternalTextPasteCommand(sheet.Id, address, [[text]]);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(address).Should().Be(new NumberValue(expected));
    }
}
