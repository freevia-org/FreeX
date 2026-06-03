using System.Windows.Input;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class MainWindowRibbonKeyTipTests
{
    [Fact]
    public void ReviewNoteAndCommentNavigationKeyTips_RouteSplitReviewLanes()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create();
            harness.AddNote(2, 2, "Plain note");
            harness.AddThreadedComment(4, 4, "Threaded note");
            harness.SelectRange(1, 1, 1, 1);

            harness.HandleDirectTopLevelKeyTip(Key.R).Should().BeTrue();
            harness.HandleKeyTip(Key.N);

            harness.SelectedCellAddress.Should().Be((2u, 2u));
            harness.KeyTipScope.Should().Be("None");

            harness.HandleDirectTopLevelKeyTip(Key.R).Should().BeTrue();
            harness.HandleKeyTip(Key.N);

            harness.SelectedCellAddress.Should().Be((2u, 2u), "Next Note should cycle simple notes without crossing into threaded comments");
            harness.KeyTipScope.Should().Be("None");

            harness.HandleDirectTopLevelKeyTip(Key.R).Should().BeTrue();
            harness.HandleKeyTip(Key.J);
            harness.KeyTipScope.Should().Be("Commands", "J is the shared Review prefix before Next Comment resolves");
            harness.HandleKeyTip(Key.C);

            harness.SelectedCellAddress.Should().Be((4u, 4u));
            harness.KeyTipScope.Should().Be("None");

            harness.HandleDirectTopLevelKeyTip(Key.R).Should().BeTrue();
            harness.HandleKeyTip(Key.P);
            harness.KeyTipScope.Should().Be("Commands", "P is a shared Review prefix before Previous Note resolves");
            harness.HandleKeyTip(Key.N);

            harness.SelectedCellAddress.Should().Be((2u, 2u));
            harness.KeyTipScope.Should().Be("None");
        });
    }

    [Fact]
    public void ReviewAllowEditRangesKeyTip_IsDisabledWhenSheetIsProtected()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create(workbook =>
            {
                workbook.Sheets[0].IsProtected = true;
            });

            harness.RefreshSheetProtectionUi();

            harness.NamedButtonIsEnabled("AllowEditRangesButton").Should().BeFalse();
            harness.HandleDirectTopLevelKeyTip(Key.R).Should().BeTrue();
            harness.HandleKeyTip(Key.A);

            harness.KeyTipScope.Should().Be("None", "disabled Review commands should not stay routable through keytips");
            harness.StartScreenIsVisible.Should().BeFalse("Alt,R,A,R must not open the Allow Edit Ranges workflow on a protected sheet");
        });
    }
}
