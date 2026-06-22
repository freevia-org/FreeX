using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using FreeW.App.Host.Editing;

namespace FreeW.App.Host.Tests;

public sealed class ThemeGalleryTests
{
    [StaFact]
    public void DesignGalleries_AreLabelledThemesAndColors_WithCatalogOrder()
    {
        var editor = new DocumentView();

        var themes = ThemeGallery.BuildThemes(editor);
        var styleSets = ThemeGallery.BuildStyleSets(editor);
        var colors = ThemeGallery.BuildColours(editor);
        var fonts = ThemeGallery.BuildFonts(editor);
        var paragraphSpacing = ThemeGallery.BuildParagraphSpacing(editor);

        AutomationProperties.GetName(themes).Should().Be("Themes");
        AutomationProperties.GetName(styleSets).Should().Be("Style Sets");
        AutomationProperties.GetName(colors).Should().Be("Colors");
        AutomationProperties.GetName(fonts).Should().Be("Fonts");
        AutomationProperties.GetName(paragraphSpacing).Should().Be("Paragraph Spacing");
        Captions(themes).Should().Equal("Themes", "Office", "Slate", "Berlin", "Ion");
        Captions(styleSets).Where(c => c != "Aa").Should().Equal("Style Sets", "Office", "Simple", "Elegant", "Formal");
        Captions(colors).Should().Equal("Colors", "Office", "Slate", "Berlin", "Ion");
        Captions(fonts).Where(c => c is not "Heading" and not "Body").Should().Equal("Fonts", "Office", "Cambria", "Georgia", "Trebuchet");
        Captions(paragraphSpacing).Should().Equal("Paragraph Spacing", "No Paragraph Space", "Compact", "Tight", "Open", "Relaxed", "Double");
    }

    private static IReadOnlyList<string> Captions(DependencyObject root)
    {
        var captions = new List<string>();
        Collect(root, captions);
        return captions;
    }

    private static void Collect(DependencyObject root, List<string> captions)
    {
        if (root is TextBlock { Text.Length: > 0 } text)
            captions.Add(text.Text);

        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            Collect(child, captions);
    }
}
