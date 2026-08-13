
using FluentAssertions;

using FreeX.App.Presentation.TextToColumns;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Regression tests for review finding H34: the Avalonia Text-to-Columns column-format dropdown must
/// offer every date order (MDY/DMY/YMD/MYD/DYM/YDM), matching the WPF host's date-format combo and
/// Excel's Text Import Wizard, instead of hardcoding <see cref="TextToColumnsColumnFormat.DateMDY"/> as
/// the only reachable "Date" choice. <c>MainWindow.TextToColumnsFormatChoices</c> is a private static
/// property, so it is read via reflection (the established pattern for this test project — see
/// <c>AvaloniaRibbonRendererTests</c>), keeping the assertions tied to the exact list the dialog builds
/// its ComboBox from.
/// </summary>
public sealed class TextToColumnsDateFormatChoiceTests
{
    [Fact]
    public void FormatChoices_OfferAllSixDateOrders()
    {
        var choices = GetFormatChoices();

        var dateFormats = choices.Select(c => c.Format).Where(TextToColumnsDialogPlanner.IsDateColumnFormat).ToList();

        dateFormats.Should().BeEquivalentTo(
        [
            TextToColumnsColumnFormat.DateMDY,
            TextToColumnsColumnFormat.DateDMY,
            TextToColumnsColumnFormat.DateYMD,
            TextToColumnsColumnFormat.DateMYD,
            TextToColumnsColumnFormat.DateDYM,
            TextToColumnsColumnFormat.DateYDM,
        ], "every date order Excel's Text Import Wizard supports must be reachable, not just MDY");
    }

    [Fact]
    public void FormatChoices_StillOfferGeneralTextAndSkip()
    {
        var choices = GetFormatChoices();

        choices.Select(c => c.Format).Should().Contain(
        [
            TextToColumnsColumnFormat.General,
            TextToColumnsColumnFormat.Text,
            TextToColumnsColumnFormat.Skip,
        ]);
    }

    [Fact]
    public void FormatChoices_EachDateOrderHasADistinctLabel()
    {
        var choices = GetFormatChoices();

        var dateLabels = choices
            .Where(c => TextToColumnsDialogPlanner.IsDateColumnFormat(c.Format))
            .Select(c => c.Label)
            .ToList();

        dateLabels.Should().OnlyHaveUniqueItems("a user must be able to tell the six date orders apart in the dropdown");
    }

    [Fact]
    public void FormatChoices_DmyOrderIsSelectable_AndMapsToTheDmyEnumValue()
    {
        var choices = GetFormatChoices();

        var dmyChoice = choices.Should().ContainSingle(c => c.Format == TextToColumnsColumnFormat.DateDMY).Subject;

        dmyChoice.Label.Should().Contain("DMY");
    }

    [Fact]
    public void SelectingDmyChoice_ParsesEuropeanDateCorrectly_UnlikeTheOldMdyOnlyBehavior()
    {
        var choices = GetFormatChoices();
        var dmyFormat = choices.Single(c => c.Format == TextToColumnsColumnFormat.DateDMY).Format;

        // "03/04/2024" is 3 April 2024 under a day-first (DMY) convention. Before the fix, only
        // DateMDY was reachable from the dialog, which would have silently produced March 4 instead.
        var value = TextToColumnsValueConverter.ConvertValue("03/04/2024", dmyFormat);

        value.Should().BeOfType<DateTimeValue>();
        ((DateTimeValue)value).ToDateTime().Should().Be(new DateTime(2024, 4, 3));
    }

    [Fact]
    public void SelectingMdyChoice_StillParsesAmericanDateCorrectly()
    {
        var choices = GetFormatChoices();
        var mdyFormat = choices.Single(c => c.Format == TextToColumnsColumnFormat.DateMDY).Format;

        var value = TextToColumnsValueConverter.ConvertValue("03/04/2024", mdyFormat);

        value.Should().BeOfType<DateTimeValue>();
        ((DateTimeValue)value).ToDateTime().Should().Be(new DateTime(2024, 3, 4));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static IReadOnlyList<(TextToColumnsColumnFormat Format, string Label)> GetFormatChoices()
    {
        return MainWindow.TextToColumnsFormatChoicesForTest;
    }
}
