using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class WorkbookThemeDialogXamlTests
{
    [Fact]
    public void Dialog_ExposesExcelLikeThemePreviewPane()
    {
        var xaml = XamlLocalizationTestHelper.ReadLocalizedXaml("WorkbookThemeDialog.xaml");
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "WorkbookThemeDialog.xaml.cs"));

        xaml.Should().Contain("x:Name=\"ThemePreviewPane\"");
        xaml.Should().Contain("x:Name=\"PreviewHeadingText\"");
        xaml.Should().Contain("x:Name=\"PreviewBodyText\"");
        xaml.Should().Contain("x:Name=\"PreviewAccentStrip\"");
        xaml.Should().Contain("Sample");
        source.Should().Contain("UpdatePreview");
        source.Should().Contain("WirePreviewRefresh");
        source.Should().Contain("HeadingFontBox.SelectionChanged += (_, _) => UpdatePreview()");
        source.Should().Contain("HeadingFontBox.AddHandler(TextBox.TextChangedEvent");
        source.Should().Contain("colorBox.TextChanged += (_, _) =>");
        source.Should().Contain("UpdateColorPickerSwatches();");
        source.Should().Contain("ThemeColorTextBoxes");
        source.Should().Contain("PreviewHeadingText.FontFamily");
        source.Should().Contain("PreviewAccentStrip");
    }

    [Fact]
    public void DialogThemeFieldMap_CoversEveryThemeColorSlot()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "WorkbookThemeDialog.ThemeFields.cs"));

        source.Should().Contain("WorkbookThemeColorSlot.Dark1");
        source.Should().Contain("WorkbookThemeColorSlot.Light1");
        source.Should().Contain("WorkbookThemeColorSlot.Dark2");
        source.Should().Contain("WorkbookThemeColorSlot.Light2");
        source.Should().Contain("WorkbookThemeColorSlot.Accent1");
        source.Should().Contain("WorkbookThemeColorSlot.Accent2");
        source.Should().Contain("WorkbookThemeColorSlot.Accent3");
        source.Should().Contain("WorkbookThemeColorSlot.Accent4");
        source.Should().Contain("WorkbookThemeColorSlot.Accent5");
        source.Should().Contain("WorkbookThemeColorSlot.Accent6");
        source.Should().Contain("WorkbookThemeColorSlot.Hyperlink");
        source.Should().Contain("WorkbookThemeColorSlot.FollowedHyperlink");
        source.Should().Contain("IsAccent: true");
    }
}
