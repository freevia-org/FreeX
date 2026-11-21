using FluentAssertions;
using FreeX.App.Services;

namespace FreeX.App.Services.Tests;

/// <summary>Excel's Advanced > "Automatically Flash Fill" option plumbing (default on).</summary>
public sealed class AutomaticallyFlashFillOptionTests
{
    private static OptionsDialogPlanner.OptionsDialogInput Input(AppOptions options) =>
        new(
            options.DefaultFontName,
            options.DefaultFontSize,
            options.DefaultSheetCount,
            options.UserName,
            options.AutoCalculate,
            options.UseR1C1ReferenceStyle,
            options.ErrorCheckingEnabled,
            options.ProofingIgnoreUppercase,
            options.ProofingIgnoreNumbers,
            options.ShowFormulaBar,
            options.ShowGridlines,
            options.ShowHeadings,
            options.DefaultFormat,
            options.ShowScreenTips,
            options.MoveSelectionAfterEnter,
            options.AfterEnterDirection);

    private static OptionsDialogPlanner.OptionsDialogSupplementalInput Supplemental(
        AppOptions options,
        bool? automaticallyFlashFill) =>
        new(
            options.EnableFillHandleAndCellDragAndDrop,
            options.EnableAutoCompleteForCellValues,
            options.QuickAccessToolbarBelowRibbon,
            options.QuickAccessToolbarCommands,
            options.SpellCheckCustomDictionaryWords)
        {
            AutomaticallyFlashFill = automaticallyFlashFill,
        };

    [Fact]
    public void DefaultsToOn() => new AppOptions().AutomaticallyFlashFill.Should().BeTrue();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Project_AppliesTheDialogValue(bool value)
    {
        var existing = new AppOptions { AutomaticallyFlashFill = !value };

        var projected = OptionsDialogPlanner.Project(existing, Input(existing), Supplemental(existing, value));

        projected.AutomaticallyFlashFill.Should().Be(value);
    }

    [Fact]
    public void Project_KeepsTheExistingValueWhenTheDialogDoesNotSupplyOne()
    {
        var existing = new AppOptions { AutomaticallyFlashFill = false };

        OptionsDialogPlanner.Project(existing, Input(existing), Supplemental(existing, null))
            .AutomaticallyFlashFill.Should().BeFalse();
        OptionsDialogPlanner.Project(existing, Input(existing))
            .AutomaticallyFlashFill.Should().BeFalse();
    }

    [Fact]
    public void MergeOntoFreshLoad_OnlyAppliesAChangeTheUserMadeInTheDialog()
    {
        var snapshot = new AppOptions { AutomaticallyFlashFill = true };
        var edited = snapshot.Clone();
        edited.AutomaticallyFlashFill = false;

        OptionsDialogPlanner.MergeOntoFreshLoad(new AppOptions { AutomaticallyFlashFill = true }, snapshot, edited)
            .AutomaticallyFlashFill.Should().BeFalse();

        // Untouched in this dialog session: the freshest on-disk value wins.
        var untouched = snapshot.Clone();
        OptionsDialogPlanner.MergeOntoFreshLoad(new AppOptions { AutomaticallyFlashFill = false }, snapshot, untouched)
            .AutomaticallyFlashFill.Should().BeFalse();
    }
}
