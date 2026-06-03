using System.IO;
using System.Xml.Linq;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class WorkbookThemeDialogXamlTests
{
    [Fact]
    public void DialogOpenedFromKeyboard_FocusesThemeNameBox()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "WorkbookThemeDialog.xaml.cs"));

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("ThemeNameBox.Focus();");
        source.Should().Contain("ThemeNameBox.SelectAll();");
        source.Should().Contain("Keyboard.Focus(ThemeNameBox);");
    }

    [Fact]
    public void DialogInvalidThemeColor_FocusesInvalidColorBox()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "WorkbookThemeDialog.xaml.cs"));

        source.Should().Contain("WorkbookThemeDialogPlanner.TryCreateTheme");
        source.Should().Contain("private void ShowInvalidThemeColor(WorkbookThemeDialogValidationError error)");
        source.Should().Contain("ThemeColorFields().FirstOrDefault(field => field.Slot == error.Slot)");
        source.Should().Contain("FocusInvalidColorInput(field.TextBox);");
        source.Should().Contain("private static void FocusInvalidColorInput(TextBox colorBox)");
        source.Should().Contain("colorBox.Focus();");
        source.Should().Contain("colorBox.SelectAll();");
        source.Should().Contain("Keyboard.Focus(colorBox);");
    }

    [Fact]
    public void Dialog_ExposesKeyboardAccessKeysForThemeFieldsColorsAndButtons()
    {
        var document = XamlLocalizationTestHelper.LoadLocalizedXaml("WorkbookThemeDialog.xaml");
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        document.Descendants(presentation + "Button")
            .Select(element => element.Attribute("Content")?.Value)
            .Should()
            .Contain(["_Save", "_Cancel", "_Office", "FreeX _Colorful", "_Grayscale"]);

        AssertLabelTargets(document, presentation, "_Name:", "ThemeNameBox");
        AssertLabelTargets(document, presentation, "_Heading font:", "HeadingFontBox");
        AssertLabelTargets(document, presentation, "_Body font:", "BodyFontBox");
        AssertLabelTargets(document, presentation, "_Effects:", "EffectsBox");

        AssertLabelTargets(document, presentation, "_Dark 1", "Dark1ColorBox");
        AssertLabelTargets(document, presentation, "_Light 1", "Light1ColorBox");
        AssertLabelTargets(document, presentation, "D_ark 2", "Dark2ColorBox");
        AssertLabelTargets(document, presentation, "L_ight 2", "Light2ColorBox");
        AssertLabelTargets(document, presentation, "_Accent 1", "Accent1ColorBox");
        AssertLabelTargets(document, presentation, "A_ccent 2", "Accent2ColorBox");
        AssertLabelTargets(document, presentation, "Ac_cent 3", "Accent3ColorBox");
        AssertLabelTargets(document, presentation, "Acc_ent 4", "Accent4ColorBox");
        AssertLabelTargets(document, presentation, "Acce_nt 5", "Accent5ColorBox");
        AssertLabelTargets(document, presentation, "Accen_t 6", "Accent6ColorBox");
        AssertLabelTargets(document, presentation, "H_yperlink", "HyperlinkColorBox");
        AssertLabelTargets(document, presentation, "Followed Hype_rlink", "FollowedHyperlinkColorBox");

        static void AssertLabelTargets(XDocument document, XNamespace presentation, string content, string target)
        {
            var label = document
                .Descendants(presentation + "Label")
                .Single(element => element.Attribute("Content")?.Value == content);

            label.Attribute("Target")?.Value.Should().Be($"{{Binding ElementName={target}}}");
        }
    }
}
