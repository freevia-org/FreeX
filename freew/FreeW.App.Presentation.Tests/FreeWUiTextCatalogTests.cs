using System.Globalization;
using FreeW.App.Localization;
using FreeW.App.Presentation.DocumentView;

namespace FreeW.App.Presentation.Tests;

public sealed class FreeWUiTextCatalogTests
{
    [Fact]
    public void NeutralCatalog_PreservesExistingEnglishText()
    {
        WithUiCulture("en-US", () =>
        {
            FreeWUiTextCatalog.Zoom.Should().Be("Zoom");
            FreeWUiTextCatalog.TableStylesCompact.Should().Be("Table\nStyles");
            FreeWUiTextCatalog.ThesaurusInsertToolTip("quick", "fast")
                .Should().Be("Insert \"quick\" in place of \"fast\"");
            FreeWUiTextCatalog.FootnoteLabel(4).Should().Be("Footnote 4");
            return true;
        });
    }

    [Fact]
    public void CatalogAndPortablePlanners_RespondToPseudoLocalization()
    {
        WithUiCulture(Loc.PseudoLocalizationCultureName, () =>
        {
            FreeWUiTextCatalog.Themes.Should().StartWith("[[").And.EndWith("]]");
            FreeWUiTextCatalog.NotesApplyToolTip.Should().Contain("CCoommmmiitt");
            FreeWUiTextCatalog.ThesaurusInsertToolTip("quick", "fast")
                .Should().Contain("quick").And.Contain("fast").And.StartWith("[[");
            return true;
        });
    }

    [Fact]
    public void WpfContentControlTooltip_UsesPortablePlanner()
    {
        var source = Read("freew", "FreeW.App.Host", "Editing", "DocumentView.cs");

        source.Should().Contain("ContentControlInteractionPlanner.Tooltip(control)");
        source.Should().NotContain("private static string ContentControlTooltip(");
    }

    [Fact]
    public void RendererTail_HasNoOwnedSemanticEnglishAssignments()
    {
        var files = new[]
        {
            Read("freew", "FreeW.App.Host", "Ribbon", "ThemeGallery.cs"),
            Read("freew", "FreeW.App.Host", "Ribbon", "TableStylesGallery.cs"),
            Read("freew", "FreeW.App.Host", "Ribbon", "StylesGallery.cs"),
            Read("freew", "FreeW.App.Host", "ThesaurusPane.cs"),
            Read("freew", "FreeW.App.Avalonia", "ThesaurusPane.cs"),
            Read("freew", "FreeW.App.Avalonia", "NotesPane.cs"),
        };

        var source = string.Join('\n', files);
        source.Should().NotContain("Text = \"Thesaurus\"");
        source.Should().NotContain("Text = \"Notes\"");
        source.Should().NotContain("ToolTip = \"Table Styles\"");
        source.Should().NotContain("ToolTip = \"More styles\"");
        source.Should().NotContain("Content = \"Copy\"");
        source.Should().NotContain("Content = \"Apply\"");
        source.Should().NotContain("Content = \"Delete\"");
    }

    [Fact]
    public void CatalogRequiredKeys_ExistInNeutralResources()
    {
        var neutralKeys = Loc.GetNeutralResourceKeys();

        FreeWUiTextCatalog.RequiredResourceKeys.Should().OnlyContain(key => neutralKeys.Contains(key));
    }

    private static string Read(params string[] parts) =>
        File.ReadAllText(TestWorkspaceFileLocator.Find(parts));

    private static T WithUiCulture<T>(string cultureName, Func<T> action)
    {
        var originalUi = CultureInfo.CurrentUICulture;
        var original = CultureInfo.CurrentCulture;
        try
        {
            var culture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentUICulture = culture;
            CultureInfo.CurrentCulture = culture;
            return action();
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalUi;
            CultureInfo.CurrentCulture = original;
        }
    }
}
