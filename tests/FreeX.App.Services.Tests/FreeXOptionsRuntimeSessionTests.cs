using FluentAssertions;
using FreeX.App.Services;
using FreeX.App.Services.Ribbon;

namespace FreeX.App.Services.Tests;

public sealed class FreeXOptionsRuntimeSessionTests
{
    [Fact]
    public void Constructor_DoesNotNormalizeInjectedLiveSnapshot()
    {
        var live = new AppOptions
        {
            SpellCheckCustomDictionaryWords = [" keep ", "Keep", "also"],
        };

        var runtime = new FreeXOptionsRuntimeSession(live);

        runtime.LiveOptions.Should().BeSameAs(live);
        live.SpellCheckCustomDictionaryWords.Should().Equal(" keep ", "Keep", "also");
    }

    [Fact]
    public void Adopt_PreservesLiveIdentityAndCopiesMutableCollections()
    {
        var live = new AppOptions
        {
            UserName = "Initial",
            SpellCheckCustomDictionaryWords = ["alpha"],
            QuickAccessToolbarCommands = ["Save", "Undo"],
        };
        var adopted = new AppOptions
        {
            UserName = "Reloaded",
            SpellCheckCustomDictionaryWords = ["beta"],
            QuickAccessToolbarCommands = ["Save", "Redo"],
        };
        var runtime = new FreeXOptionsRuntimeSession(live);

        var result = runtime.Adopt(adopted);

        result.Should().BeSameAs(live);
        result.UserName.Should().Be("Reloaded");
        result.SpellCheckCustomDictionaryWords.Should().Equal("beta");
        result.QuickAccessToolbarCommands.Should().Equal("Save", "Redo");
        result.SpellCheckCustomDictionaryWords.Should().NotBeSameAs(adopted.SpellCheckCustomDictionaryWords);
        result.QuickAccessToolbarCommands.Should().NotBeSameAs(adopted.QuickAccessToolbarCommands);
    }

    [Fact]
    public void BeginDialog_ClonesTheOpenSnapshot()
    {
        var live = new AppOptions
        {
            UserName = "Open",
            SpellCheckCustomDictionaryWords = ["alpha"],
        };
        var runtime = new FreeXOptionsRuntimeSession(live);

        var dialog = runtime.BeginDialog(live);
        live.UserName = "Concurrent edit";
        live.SpellCheckCustomDictionaryWords.Add("beta");

        dialog.OpenSnapshot.Should().NotBeSameAs(live);
        dialog.OpenSnapshot.UserName.Should().Be("Open");
        dialog.OpenSnapshot.SpellCheckCustomDictionaryWords.Should().Equal("alpha");
    }

    [Fact]
    public void CommitDialog_MergesUserEditsOntoFreshStoreAndAdoptsSavedSnapshot()
    {
        var openSnapshot = new AppOptions
        {
            UserName = "Open snapshot",
            ShowGridlines = true,
            StatusBarShowMinimum = false,
        };
        var freshSnapshot = new AppOptions
        {
            UserName = "Concurrent edit",
            ShowGridlines = true,
            StatusBarShowMinimum = true,
        };
        AppOptions? saved = null;
        var runtime = new FreeXOptionsRuntimeSession(
            openSnapshot,
            load: () => freshSnapshot,
            save: options =>
            {
                saved = options;
                return true;
            });
        var edited = OptionsDialogPlanner.Project(
            openSnapshot,
            BuildInput(openSnapshot, showGridlines: false));

        var result = runtime.CommitDialog(openSnapshot, edited);

        result.Succeeded.Should().BeTrue();
        saved.Should().NotBeNull();
        result.Options.ShowGridlines.Should().BeFalse();
        result.Options.UserName.Should().Be("Concurrent edit");
        result.Options.StatusBarShowMinimum.Should().BeTrue();
        runtime.LiveOptions.Should().BeSameAs(result.Options);
    }

    [Fact]
    public void CommitDialog_WhenSaveFails_DoesNotReplaceLiveSnapshot()
    {
        var live = new AppOptions { UserName = "Live" };
        var runtime = new FreeXOptionsRuntimeSession(
            live,
            load: () => new AppOptions { UserName = "Fresh" },
            save: _ => false);
        var edited = OptionsDialogPlanner.Project(
            live,
            BuildInput(live, showGridlines: false));

        var result = runtime.CommitDialog(live, edited);

        result.IsPersisted.Should().BeFalse();
        runtime.LiveOptions.Should().BeSameAs(live);
        result.Options.ShowGridlines.Should().BeFalse();
    }

    [Fact]
    public void MutateFresh_UsesLatestStoreSnapshotAndKeepsRuntimeChoiceOnSaveFailure()
    {
        var fresh = new AppOptions
        {
            UserName = "Concurrent edit",
            QuickAccessToolbarCommands = ["Save", "Undo"],
        };
        var runtime = new FreeXOptionsRuntimeSession(
            new AppOptions(),
            load: () => fresh,
            save: _ => false);

        var result = runtime.MutateFresh(options =>
            options.QuickAccessToolbarCommands = ["Save", "Redo"]);

        result.IsPersisted.Should().BeFalse();
        result.Options.UserName.Should().Be("Concurrent edit");
        runtime.LiveOptions.Should().BeSameAs(result.Options);
        runtime.LiveOptions.QuickAccessToolbarCommands.Should().Equal("Save", "Redo");
    }

    [Fact]
    public void DialogSession_OwnsSupplementalEditorsAndPersistsTheirState()
    {
        var open = new AppOptions
        {
            SpellCheckCustomDictionaryWords = ["alpha"],
            QuickAccessToolbarCommands = ["Save", "Undo"],
        };
        AppOptions? saved = null;
        var runtime = new FreeXOptionsRuntimeSession(
            open,
            load: () => new AppOptions
            {
                SpellCheckCustomDictionaryWords = ["alpha"],
                QuickAccessToolbarCommands = ["Save", "Undo"],
            },
            save: options =>
            {
                saved = options;
                return true;
            });
        var dialog = runtime.BeginDialog(open);
        dialog.CustomDictionary.SetPendingWord("beta");
        dialog.CustomDictionary.AddPendingWord().Words.Should().Contain("beta");
        dialog.QuickAccessToolbar.Apply(
            "Redo",
            QuickAccessToolbarCustomizationAction.Add);

        var result = dialog.Commit(
            BuildInput(open, showGridlines: open.ShowGridlines),
            enableFillHandleAndCellDragAndDrop: false,
            enableAutoCompleteForCellValues: false,
            quickAccessToolbarBelowRibbon: true,
            formulaBarExpanded: true);

        result.Succeeded.Should().BeTrue();
        saved.Should().NotBeNull();
        result.Options.SpellCheckCustomDictionaryWords.Should().Equal("alpha", "beta");
        result.Options.QuickAccessToolbarCommands.Should().Equal("Save", "Undo", "Redo");
        result.Options.QuickAccessToolbarBelowRibbon.Should().BeTrue();
        result.Options.EnableFillHandleAndCellDragAndDrop.Should().BeFalse();
        result.Options.EnableAutoCompleteForCellValues.Should().BeFalse();
        result.Options.FormulaBarExpanded.Should().BeTrue();
    }

    private static OptionsDialogPlanner.OptionsDialogInput BuildInput(
        AppOptions options,
        bool showGridlines) =>
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
            showGridlines,
            options.ShowHeadings,
            options.DefaultFormat,
            options.ShowScreenTips,
            options.MoveSelectionAfterEnter,
            options.AfterEnterDirection,
            options.ObjectsDisplay,
            options.CollapseRibbonAutomatically,
            options.AppLanguage,
            options.CrashAnalyticsEnabled);
}
