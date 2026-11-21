using System.Threading;
using System.Threading.Tasks;

using Avalonia.Headless;

using FluentAssertions;

using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>Excel's cell menu Filter ▸ "Filter by Selected Cell's Value", end to end in the window.</summary>
[Collection("AvaloniaHeadless")]
public sealed class FilterBySelectedCellValueTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task FiltersTheActiveColumnToTheActiveCellValue()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            window.Show();
            try
            {
                var sheet = window.Session.ActiveSheet;
                string[] fruit = ["Fruit", "Apple", "Pear", "Apple", "Plum"];
                for (uint row = 1; row <= fruit.Length; row++)
                    sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue(fruit[row - 1]));

                window.Session.SelectCell(new CellAddress(sheet.Id, 2, 1));
                window.FilterBySelectedCellValue();

                sheet.AutoFilter.Should().NotBeNull("the AutoFilter is turned on around the active cell");
                sheet.IsRowEffectivelyHidden(1).Should().BeFalse("the header row stays");
                sheet.IsRowEffectivelyHidden(2).Should().BeFalse();
                sheet.IsRowEffectivelyHidden(3).Should().BeTrue("Pear does not match the selected Apple");
                sheet.IsRowEffectivelyHidden(4).Should().BeFalse();
                sheet.IsRowEffectivelyHidden(5).Should().BeTrue();
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }

            return Task.FromResult(true);
        }, CancellationToken.None);
    }
}
