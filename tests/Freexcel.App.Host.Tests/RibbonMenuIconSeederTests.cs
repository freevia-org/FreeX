using System.Reflection;
using System.Windows.Controls;
using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class RibbonMenuIconSeederTests
{
    [Theory]
    [InlineData("_More...")]
    [InlineData("_More\u2026")]
    public void SeedMenuItems_NormalizesAcceleratorsAndEllipsisBeforeCommandLookup(string header)
    {
        StaTestRunner.Run(() =>
        {
            var item = new MenuItem { Header = header };

            SeedMenuItems(item);

            item.Icon.Should().NotBeNull();
        });
    }

    [Fact]
    public void SeedMenuItems_PreservesExistingIconsAndSeedsNestedItems()
    {
        StaTestRunner.Run(() =>
        {
            var existingIcon = new TextBlock { Text = "Existing" };
            var parent = new MenuItem { Header = "Paste", Icon = existingIcon };
            var child = new MenuItem { Header = "Values" };
            parent.Items.Add(child);

            SeedMenuItems(parent);

            parent.Icon.Should().BeSameAs(existingIcon);
            child.Icon.Should().NotBeNull();
        });
    }

    [Fact]
    public void SeedMenuItems_SkipsDisabledGallerySectionHeaders()
    {
        StaTestRunner.Run(() =>
        {
            var sectionHeader = new MenuItem
            {
                Header = "Shapes",
                IsEnabled = false
            };

            SeedMenuItems(sectionHeader);

            sectionHeader.Icon.Should().BeNull();
        });
    }

    [Fact]
    public void SeedMenuItems_SkipsCleanedGallerySectionHeadersCaseInsensitively()
    {
        StaTestRunner.Run(() =>
        {
            var sectionHeader = new MenuItem
            {
                Header = "_shapes...",
                IsEnabled = false
            };

            SeedMenuItems(sectionHeader);

            sectionHeader.Icon.Should().BeNull();
        });
    }

    private static void SeedMenuItems(params MenuItem[] menuItems)
    {
        var method = typeof(RibbonMenuIconSeeder)
            .GetMethod("SeedMenuItems", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(nameof(RibbonMenuIconSeeder), "SeedMenuItems");

        method.Invoke(null, [menuItems]);
    }
}
