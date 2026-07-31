using Avalonia.Headless;
using FluentAssertions;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

public sealed class CaptionDialogTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Fact]
    public Task Dialog_preserves_table_default_and_trims_accepted_text() =>
        Session.Dispatch(() =>
        {
            var dialog = new CaptionDialog(CaptionLabel.Table);

            dialog.SelectedLabelForTest.Should().Be(CaptionLabel.Table);
            dialog.BuildResultForTest(2, "  Energy  ")
                .Should().Be(new CaptionDialogResult(CaptionLabel.Equation, "Energy"));
        }, CancellationToken.None);
}
