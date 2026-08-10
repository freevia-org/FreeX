using FluentAssertions;
using FreeX.App.Services.Ribbon;

namespace FreeX.App.Services.Tests;

public sealed class QuickAccessToolbarCatalogTests
{
    [Theory]
    [InlineData(1, "1")]
    [InlineData(9, "9")]
    [InlineData(10, "01")]
    [InlineData(18, "09")]
    [InlineData(19, "0A")]
    [InlineData(44, "0Z")]
    [InlineData(45, "45")]
    public void FormatKeyTip_UsesCanonicalExcelSequence(int visibleIndex, string expected)
    {
        QuickAccessToolbarCatalog.FormatKeyTip(visibleIndex).Should().Be(expected);
    }

    [Fact]
    public void RendererAdapters_DelegateKeyTipNumberingToCatalog()
    {
        var wpf = Read("src", "FreeX.App.Host", "MainWindow.QuickAccessToolbar.cs");
        var avalonia = Read("src", "FreeX.App.Avalonia", "MainWindow.CatalogContextMenus.cs");

        wpf.Should().Contain("QuickAccessToolbarCatalog.FormatKeyTip(visibleIndex)");
        wpf.Should().NotContain("private static string FormatQuickAccessToolbarKeyTip(");
        avalonia.Should().Contain("QuickAccessToolbarCatalog.FormatKeyTip(index + 1)");
        avalonia.Should().NotContain("private static string FormatAvaloniaQuickAccessKeyTip(");
    }

    private static string Read(params string[] parts) =>
        File.ReadAllText(RepositoryFileLocator.Find(parts));
}
