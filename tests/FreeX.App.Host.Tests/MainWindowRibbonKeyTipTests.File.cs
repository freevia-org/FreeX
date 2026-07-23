using System.Windows.Input;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class MainWindowRibbonKeyTipTests
{
    [Fact]
    public void FileKeyTip_RoutesThroughBackstageCommandsOnly()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.EnterKeyTipScope("TopLevel");
            harness.HandleKeyTip(Key.F);

            harness.StartScreenIsVisible.Should().BeTrue();
            harness.KeyTipScope.Should().Be("Commands");
            harness.OverlayBadgeTexts.Should().Contain(["N", "O", "R"]);
            harness.OverlayBadgeTexts.Should().NotContain("FG", "covered Home ribbon controls should not participate while Backstage is open");
            harness.VisibleCommandKeyTips("N").Should().ContainSingle().Which.Should().Be("New");
        });
    }

    [Fact]
    public void FileKeyTip_DoesNotExposeDuplicateRecentFileRowKeyTips()
    {
        RunSta(() =>
        {
            using var tempFiles = TempRecentFiles.Create(4);
            using var harness = MainWindowHarness.Create();
            harness.SetRecentFiles(tempFiles.Paths.Take(2), tempFiles.Paths.Skip(2));

            harness.EnterKeyTipScope("TopLevel");
            harness.HandleKeyTip(Key.F);

            harness.OverlayBadgeTexts
                .GroupBy(text => text, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .Should()
                .BeEmpty("Backstage keytips must be unique within the visible File scope");
        });
    }
}
