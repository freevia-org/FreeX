using System.Threading;

using Avalonia.Headless;

using FluentAssertions;

using Free.Shared.Ribbon;
using FreeX.App.Avalonia.Ribbon;
using FreeX.Core.Model;

using Xunit;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class AvaloniaPageLayoutScaleCommitTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Theory]
    [InlineData("pageLayout.width", "2 pages", 2, null, null)]
    [InlineData("pageLayout.height", "3 pages", null, 3, null)]
    [InlineData("pageLayout.scale", "85%", null, null, 85)]
    public async Task ScaleSelection_UpdatesWorksheetAndUndoRestoresDefault(
        string commandId,
        string selectedValue,
        int? expectedWidth,
        int? expectedHeight,
        int? expectedPercent)
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                var sheet = window.Session.ActiveSheet;
                sheet.ScaleToFit.Should().Be(WorksheetScaleToFit.Default);

                var canonicalId = new RibbonCommandId(AvaloniaCommandIdAdapter.ToCanonical(commandId));
                window.RibbonCommandRegistryForTest!.TryGet(canonicalId, out var command).Should().BeTrue();

                command!.Execute(RibbonCommandContext.ForSelectedValue(selectedValue));

                sheet.ScaleToFit.FitToPagesWide.Should().Be(expectedWidth);
                sheet.ScaleToFit.FitToPagesTall.Should().Be(expectedHeight);
                sheet.ScaleToFit.ScalePercent.Should().Be(expectedPercent);
                window.Session.CanUndo.Should().BeTrue();

                window.Session.UndoLastEdit().Success.Should().BeTrue();
                sheet.ScaleToFit.Should().Be(WorksheetScaleToFit.Default);
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }
}
