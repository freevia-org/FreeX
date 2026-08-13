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
            harness.UndoQatHistoryIsEnabled.Should().BeFalse();
            harness.RedoQatHistoryIsEnabled.Should().BeFalse();
            harness.SelectActiveCell();

            harness.EnterKeyTipScope("TopLevel");
            harness.HandleKeyTip(Key.H);
            harness.HandleKeyTip(Key.D1);

            harness.ActiveCellBold.Should().BeTrue();
            harness.UndoQatIsEnabled.Should().BeTrue();
            harness.RedoQatIsEnabled.Should().BeFalse();
            harness.UndoQatHistoryIsEnabled.Should().BeTrue();
            harness.RedoQatHistoryIsEnabled.Should().BeFalse();

            harness.HandleDirectTopLevelKeyTip(Key.D2).Should().BeTrue();
            harness.KeyTipScope.Should().Be("None");
            harness.ActiveCellBold.Should().BeFalse();
            harness.UndoQatIsEnabled.Should().BeFalse();
            harness.RedoQatIsEnabled.Should().BeTrue();
            harness.UndoQatHistoryIsEnabled.Should().BeFalse();
            harness.RedoQatHistoryIsEnabled.Should().BeTrue();

            harness.HandleDirectTopLevelKeyTip(Key.D3).Should().BeTrue();
            harness.KeyTipScope.Should().Be("None");
            harness.ActiveCellBold.Should().BeTrue();
            harness.UndoQatIsEnabled.Should().BeTrue();
            harness.RedoQatIsEnabled.Should().BeFalse();
            harness.UndoQatHistoryIsEnabled.Should().BeTrue();
            harness.RedoQatHistoryIsEnabled.Should().BeFalse();
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
    public void TitleBarQuickAccessToolbar_PreservesConfiguredOrderKeyTipsAndChromeHitTesting()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.ConfigureQuickAccessToolbar(
            [
                QuickAccessToolbarCommandIds.Open,
                QuickAccessToolbarCommandIds.Save,
                QuickAccessToolbarCommandIds.Undo,
                QuickAccessToolbarCommandIds.Redo
            ],
            belowRibbon: false);

            harness.TitleBarQatIsVisible.Should().BeTrue();
            harness.BelowRibbonQatIsVisible.Should().BeFalse();
            harness.QuickAccessToolbarAutomationIds.Should().Equal(
                "OpenQatBtn",
                "SaveQatBtn",
                "UndoQatBtn",
                "UndoQatHistoryBtn",
                "RedoQatBtn",
                "RedoQatHistoryBtn");
            harness.QuickAccessToolbarKeyTips.Should().Equal("1", "2", "3", "", "4", "");
            harness.QuickAccessToolbarChromeHitTestVisibility
                .Should()
                .OnlyContain(isHitTestVisible => isHitTestVisible);
        });
    }

    [Fact]
    public void CustomQuickAccessToolbar_RebuildsBelowRibbonAndRoutesCustomKeyTips()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create();

            var commandIds = new[]
            {
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
            };
            harness.ConfigureQuickAccessToolbar(commandIds, belowRibbon: true);

            harness.TitleBarQatIsVisible.Should().BeFalse();
            harness.BelowRibbonQatIsVisible.Should().BeTrue();
            harness.ButtonIsInBelowRibbonQat("UndoQatHistoryBtn").Should().BeTrue();
            harness.ButtonIsInBelowRibbonQat("RedoQatHistoryBtn").Should().BeTrue();
            harness.ButtonIsInBelowRibbonQat("NameManagerQatBtn").Should().BeTrue();
            harness.QuickAccessToolbarAutomationIds.Should().Equal(
                "SaveQatBtn",
                "UndoQatBtn",
                "UndoQatHistoryBtn",
                "RedoQatBtn",
                "RedoQatHistoryBtn",
                "BoldQatBtn",
                "ItalicQatBtn",
                "UnderlineQatBtn",
                "PrintQatBtn",
                "OpenQatBtn",
                "InsertFunctionQatBtn",
                "NameManagerQatBtn");
            harness.QuickAccessToolbarKeyTips.Should().Equal("1", "2", "", "3", "", "4", "5", "6", "7", "8", "9", "01");
            harness.QuickAccessToolbarChromeHitTestVisibility
                .Should()
                .OnlyContain(isHitTestVisible => !isHitTestVisible);

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
        var keyTips = Enumerable.Range(1, QuickAccessToolbarCatalog.Commands.Count)
            .Select(QuickAccessToolbarCatalog.FormatKeyTip)
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
