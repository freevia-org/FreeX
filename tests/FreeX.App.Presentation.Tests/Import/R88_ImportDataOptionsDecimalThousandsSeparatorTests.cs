using FreeX.App.Presentation.Import;
using FreeX.App.Presentation.TextToColumns;
using FreeX.Core.Model;

using FluentAssertions;

namespace FreeX.App.Presentation.Tests.Import;

/// <summary>
/// R88-io-text-import-wizard-5-4: the Get Data / From Text import had no way to override the
/// decimal/thousands separators independent of the OS locale (the Text-to-Columns Advanced dialog's
/// own capability). <see cref="ImportDataOptions.DecimalSeparator"/> and
/// <see cref="ImportDataOptions.ThousandsSeparator"/> plus <see cref="ImportDataPlanner.BuildAdvancedOptions"/>
/// add that override at the options-model layer and resolve it to the same
/// <see cref="TextToColumnsAdvancedOptions"/> the sibling Text-to-Columns numeric coercion already
/// understands, so a value parser downstream of the import can honor a European-format numeric literal
/// (dot-thousands, comma-decimal) on any OS locale.
/// </summary>
public sealed class R88_ImportDataOptionsDecimalThousandsSeparatorTests
{
    /// <summary>
    /// Primary regression test: before this fix, <see cref="ImportDataOptions"/> had no separator-
    /// override properties at all (a compile-time gap), so there was no way to build an
    /// <see cref="TextToColumnsAdvancedOptions"/> that remaps a European-format numeric literal
    /// ("1.234,56" -- dot thousands, comma decimal) to the correct number regardless of OS locale. This
    /// asserts the resolved options round-trip through the shared numeric-coercion helper correctly.
    /// </summary>
    [Fact]
    public void BuildAdvancedOptions_EuropeanSeparatorOverride_ParsesGroupedDecimalCorrectly()
    {
        var options = new ImportDataOptions
        {
            DecimalSeparator = ",",
            ThousandsSeparator = "."
        };

        var advanced = ImportDataPlanner.BuildAdvancedOptions(options);

        advanced.Should().NotBeNull();
        advanced!.DecimalSeparator.Should().Be(",");
        advanced.ThousandsSeparator.Should().Be(".");

        var value = TextToColumnsValueConverter.ConvertValue("1.234,56", TextToColumnsColumnFormat.General, advanced);
        value.Should().BeOfType<NumberValue>();
        ((NumberValue)value).Value.Should().Be(1234.56);
    }

    /// <summary>
    /// No-regression sibling: when neither separator is overridden (the default, pre-existing shape of
    /// <see cref="ImportDataOptions"/>), <see cref="ImportDataPlanner.BuildAdvancedOptions"/> must return
    /// null so a caller that forwards it changes nothing about the existing current-culture-then-
    /// invariant-culture numeric coercion.
    /// </summary>
    [Fact]
    public void BuildAdvancedOptions_NoOverrideRequested_ReturnsNull()
    {
        var options = new ImportDataOptions();

        ImportDataPlanner.BuildAdvancedOptions(options).Should().BeNull();
    }

    /// <summary>
    /// A single overridden separator still resolves, falling back to the sibling feature's own default
    /// for the one left unset (mirroring <see cref="TextToColumnsAdvancedOptions"/>'s own defaults).
    /// </summary>
    [Fact]
    public void BuildAdvancedOptions_OnlyThousandsSeparatorOverridden_FallsBackToDefaultDecimalSeparator()
    {
        var options = new ImportDataOptions
        {
            ThousandsSeparator = " "
        };

        var advanced = ImportDataPlanner.BuildAdvancedOptions(options);

        advanced.Should().NotBeNull();
        advanced!.ThousandsSeparator.Should().Be(" ");
        advanced.DecimalSeparator.Should().Be(new TextToColumnsAdvancedOptions().DecimalSeparator);
    }
}
