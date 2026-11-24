using System.Globalization;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

public sealed class TabsDialogLocaleProjectionTests
{
    [Theory]
    [InlineData("en-US", "108.25", "42.5")]
    [InlineData("bg-BG", "108,25", "42,5")]
    [InlineData("de-DE", "108,25", "42,5")]
    public void SelectedStop_ProjectsAndAcceptsTheDialogCulturesValues(
        string cultureName, string selectedPosition, string defaultDistance)
    {
        // Each dialog session owns its culture. Exercise that boundary independently of the
        // native dialog class's culture captured at first use; its UI test retains focus coverage.
        var session = new TabsDialogSession(
            [new TabStop(108.25, TabStopAlignment.Decimal, TabLeader.Underline)],
            42.5, CultureInfo.GetCultureInfo(cultureName));
        var selection = session.ProjectSelection(0)!;
        selection.PositionText.Should().Be(selectedPosition);
        session.State.DefaultTabStopText.Should().Be(defaultDistance);
        selection.AlignmentIndex.Should().Be(3);
        selection.LeaderIndex.Should().Be(3);

        var set = session.SetStop(new TabsDialogSetRequest(selectedPosition, selection.AlignmentIndex, selection.LeaderIndex));
        set.Applied.Should().BeTrue();
        var accepted = session.PlanAcceptance(defaultDistance);
        accepted.IsAccepted.Should().BeTrue();
        accepted.Result!.DefaultTabStopPt.Should().Be(42.5);
        accepted.Result.TabStops.Should().ContainSingle().Which
            .Should().Be(new TabStop(108.25, TabStopAlignment.Decimal, TabLeader.Underline));
    }
}
