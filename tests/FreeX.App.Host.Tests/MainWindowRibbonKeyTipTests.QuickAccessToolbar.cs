using System.Reflection;
using System.Windows.Input;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class MainWindowRibbonKeyTipTests
{
    [Fact]
    public void DirectAltQatKeyTips_InvokeUndoRedoQuickAccessToolbarCommands()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.UndoQatIsEnabled.Should().BeFalse();
            harness.RedoQatIsEnabled.Should().BeFalse();
            harness.SelectActiveCell();

            harness.EnterKeyTipScope("TopLevel");
            harness.HandleKeyTip(Key.H);
            harness.HandleKeyTip(Key.D1);

            harness.ActiveCellBold.Should().BeTrue();
            harness.UndoQatIsEnabled.Should().BeTrue();
            harness.RedoQatIsEnabled.Should().BeFalse();

            harness.HandleDirectTopLevelKeyTip(Key.D2).Should().BeTrue();
            harness.KeyTipScope.Should().Be("None");
            harness.ActiveCellBold.Should().BeFalse();
            harness.UndoQatIsEnabled.Should().BeFalse();
            harness.RedoQatIsEnabled.Should().BeTrue();

            harness.HandleDirectTopLevelKeyTip(Key.D3).Should().BeTrue();
            harness.KeyTipScope.Should().Be("None");
            harness.ActiveCellBold.Should().BeTrue();
            harness.UndoQatIsEnabled.Should().BeTrue();
            harness.RedoQatIsEnabled.Should().BeFalse();
        });
    }

    [Fact]
    public void DirectAltQatKeyTips_NormalizeAttachedKeyTipMetadata()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SelectActiveCell();
            harness.EnterKeyTipScope("TopLevel");
            harness.HandleKeyTip(Key.H);
            harness.HandleKeyTip(Key.D1);
            harness.ActiveCellBold.Should().BeTrue();

            var originalKeyTip = harness.SetButtonKeyTip("UndoQatBtn", " 2 ");

            try
            {
                harness.HandleDirectTopLevelKeyTip(Key.D2).Should().BeTrue();

                harness.ActiveCellBold.Should().BeFalse();
                harness.KeyTipScope.Should().Be("None");
            }
            finally
            {
                harness.SetButtonKeyTip("UndoQatBtn", originalKeyTip ?? "");
            }
        });
    }

    [Fact]
    public void CustomQuickAccessToolbar_RebuildsBelowRibbonAndRoutesCustomKeyTips()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.ConfigureQuickAccessToolbar(
            [
                QuickAccessToolbarCommandIds.Save,
                QuickAccessToolbarCommandIds.Undo,
                QuickAccessToolbarCommandIds.Redo,
                QuickAccessToolbarCommandIds.Bold,
                QuickAccessToolbarCommandIds.Italic,
                QuickAccessToolbarCommandIds.Underline,
                QuickAccessToolbarCommandIds.Print,
                QuickAccessToolbarCommandIds.Open,
                QuickAccessToolbarCommandIds.InsertFunction,
                QuickAccessToolbarCommandIds.NameManager
            ],
            belowRibbon: true);

            harness.TitleBarQatIsVisible.Should().BeFalse();
            harness.BelowRibbonQatIsVisible.Should().BeTrue();
            harness.ButtonIsInBelowRibbonQat("NameManagerQatBtn").Should().BeTrue();

            harness.EnterKeyTipScope("TopLevel");
            harness.OverlayBadgeTexts.Should().Contain(["1", "4", "01"]);

            harness.SelectActiveCell();
            harness.HandleKeyTip(Key.D4);

            harness.ActiveCellBold.Should().BeTrue();
            harness.KeyTipScope.Should().Be("None");
        });
    }

    [Fact]
    public void QuickAccessToolbarCatalogKeyTips_AreUniqueAndPrefixSafe()
    {
        var formatter = typeof(MainWindow).GetMethod(
            "FormatQuickAccessToolbarKeyTip",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(nameof(MainWindow), "FormatQuickAccessToolbarKeyTip");

        var keyTips = Enumerable.Range(1, QuickAccessToolbarCatalog.Commands.Count)
            .Select(index => (string)formatter.Invoke(null, [index])!)
            .ToList();

        keyTips.Should().OnlyHaveUniqueItems();
        keyTips
            .SelectMany(first => keyTips
                .Where(second => !string.Equals(first, second, StringComparison.OrdinalIgnoreCase) &&
                    second.StartsWith(first, StringComparison.OrdinalIgnoreCase))
                .Select(second => $"{first}->{second}"))
            .Should()
            .BeEmpty("top-level QAT keytips must not hold shorter commands hostage as prefixes");
    }
}
