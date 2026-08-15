using Avalonia.Headless;
using Free.Shared.Ribbon;
using FreeX.App.Avalonia.Ribbon;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class HomeBorderRibbonCommandTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Theory]
    [InlineData("Thin", BorderStyle.Thin)]
    [InlineData("Medium", BorderStyle.Medium)]
    [InlineData("Thick", BorderStyle.Thick)]
    [InlineData("Dashed", BorderStyle.Dashed)]
    [InlineData("Dotted", BorderStyle.Dotted)]
    [InlineData("Double", BorderStyle.Double)]
    public async Task LineStyleCommand_ChangesTheStyleUsedByBorderPresets(
        string commandId,
        BorderStyle expectedStyle)
    {
        await Session.Dispatch(() =>
        {
            var window = CreateWindowWithCleanSelection(out var address);

            Execute(window, commandId);
            window.ApplySelectedRangeBorderPresetForTest(CellBorderPreset.All);

            GetStyle(window, address).BorderTop.Should().Be(new CellBorder(expectedStyle, CellColor.Black));
            window.AllowCloseWithoutDirtyPromptForParityCapture();
            window.Close();
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData("Black", null)]
    [InlineData("Gray", null)]
    [InlineData("Accent 1", WorkbookThemeColorSlot.Accent1)]
    [InlineData("Accent 2", WorkbookThemeColorSlot.Accent2)]
    public async Task LineColorCommand_ChangesTheColorUsedByBorderPresets(
        string commandId,
        WorkbookThemeColorSlot? themeSlot)
    {
        await Session.Dispatch(() =>
        {
            var window = CreateWindowWithCleanSelection(out var address);
            var expectedColor = commandId switch
            {
                "Black" => CellColor.Black,
                "Gray" => new CellColor(128, 128, 128),
                _ => window.Session.Workbook.Theme.GetColor(themeSlot!.Value),
            };

            Execute(window, commandId);
            window.ApplySelectedRangeBorderPresetForTest(CellBorderPreset.All);

            GetStyle(window, address).BorderTop.Should().Be(new CellBorder(BorderStyle.Thin, expectedColor));
            window.AllowCloseWithoutDirtyPromptForParityCapture();
            window.Close();
        }, CancellationToken.None);
    }

    private static MainWindow CreateWindowWithCleanSelection(out CellAddress address)
    {
        var window = new MainWindow([]);
        var sheet = window.Session.Workbook.AddSheet("BorderCommands");
        window.Session.SelectSheet(sheet.Id);
        address = new CellAddress(sheet.Id, 1, 1);
        window.Session.SelectCell(address);
        return window;
    }

    private static void Execute(MainWindow window, string commandId)
    {
        var registry = AvaloniaRibbonComposition.BuildRegistry(
            () => window.Session,
            _ => { },
            new AvaloniaRibbonHostCallbacks
            {
                ExtraCommands = window.BuildHomeBorderRibbonActionsForTest(),
            });
        Assert.True(registry.TryGet(new RibbonCommandId(commandId), out var command));
        Assert.IsType<ActionRibbonCommand>(command);
        command.Execute(RibbonCommandContext.Empty);
    }

    private static CellStyle GetStyle(MainWindow window, CellAddress address)
    {
        var sheet = window.Session.ActiveSheet;
        var styleId = sheet.GetCell(address)?.StyleId ??
            sheet.GetStyleOnly(address.Row, address.Col) ??
            StyleId.Default;
        return window.Session.Workbook.GetStyle(styleId);
    }
}
