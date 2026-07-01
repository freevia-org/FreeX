using FluentAssertions;
using FreeX.App.Presentation.Dialogs;

namespace FreeX.App.Presentation.Tests.Dialogs;

public sealed class TextEntryDialogPlannerTests
{
    [Fact]
    public void CreateResult_TrimsNullToEmptyText()
    {
        TextEntryDialogPlanner.CreateResult(null).Should().Be(new TextEntryDialogResult(""));
        TextEntryDialogPlanner.CreateResult("  keep spacing inside  ").Should().Be(new TextEntryDialogResult("keep spacing inside"));
    }
}
