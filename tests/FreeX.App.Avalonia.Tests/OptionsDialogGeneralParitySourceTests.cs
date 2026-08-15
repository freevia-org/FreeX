using System.IO;

namespace FreeX.App.Avalonia.Tests;

public sealed class OptionsDialogGeneralParitySourceTests
{
    [Fact]
    public void GeneralOptions_UsesWpfMetricsAndExactContentStates()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.Options.cs"));
        var wpf = File.ReadAllText(RepoFile("src", "FreeX.App.Host", "OptionsDialog.xaml"));

        source.Should().Contain("generalPanel.Spacing = 0;");
        source.Should().Contain("OptionsDialogPlanner.GeneralContentWidth");
        source.Should().Contain("OptionsDialogPlanner.GeneralLabelWidth");
        source.Should().Contain("OptionsDialogPlanner.GeneralSmallFieldWidth");
        source.Should().Contain("OptionsDialogPlanner.GeneralFieldSpacing");
        source.Should().Contain("IsEditable = true");
        source.Should().Contain("OptionsText(\"Options_ShowFeatureDescriptionsInScreenTips\")");
        source.Should().Contain("IsChecked = current.CollapseRibbonAutomatically");
        source.Should().Contain("collapseRibbonAutomatically: collapseRibbonBox.IsChecked == true");
        source.Should().Contain("ColumnDefinitions = new ColumnDefinitions($\"{OptionsDialogPlanner.CategoryColumnWidth},*\")");
        source.Should().Contain("ColumnDefinitions = new ColumnDefinitions($\"{labelWidth},*\")");

        wpf.Should().Contain("Options_ShowFeatureDescriptionsInScreenTips");
        wpf.Should().Contain("ColumnDefinition Width=\"230\"");
        wpf.Should().Contain("Height=\"24\"");
    }

    [Fact]
    public void SaveOptions_UsesWpfSectionGeometryAndStretching()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.Options.cs"));
        var wpf = File.ReadAllText(RepoFile("src", "FreeX.App.Host", "OptionsDialog.xaml"));

        source.Should().Contain("savePanel.Spacing = 0;");
        source.Should().Contain("topMargin: 0,");
        source.Should().Contain("fieldWidth: OptionsDialogPlanner.GeneralFontFieldWidth,");
        source.Should().Contain("spacing: OptionsDialogPlanner.GeneralFieldSpacing,");
        source.Should().Contain("topMargin: OptionsDialogPlanner.GeneralSectionTopMargin,");
        source.Should().Contain("stretchField: true,");
        source.Should().Contain("minWidth: 0");

        wpf.Should().Contain("<ColumnDefinition Width=\"230\"/>");
        wpf.Should().Contain("<ColumnDefinition Width=\"200\"/>");
        wpf.Should().Contain("<ColumnDefinition Width=\"*\"/>");
        wpf.Should().Contain("x:Name=\"OptRecentFilesPath\"");
    }

    [Fact]
    public void LanguageOptions_UsesSharedCatalogAndWpfFieldGeometry()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.Options.cs"));
        var wpf = File.ReadAllText(RepoFile("src", "FreeX.App.Host", "OptionsDialog.xaml"));

        source.Should().Contain("AppLanguageCatalog.GetAvailableLanguages()");
        source.Should().Contain("AppLanguageCatalog.NormalizeCultureName(current.AppLanguage)");
        source.Should().Contain("OptionsAppLanguageComboBox");
        source.Should().Contain("spacing: OptionsDialogPlanner.GeneralFieldSpacing");
        source.Should().Contain("SizeToContent = SizeToContent.Manual");
        source.Should().Contain("languagePanel.Spacing = 0;");
        source.Should().Contain("appLanguage:");
        source.Should().NotContain("isEnabled: false,\n                minWidth: 240");

        wpf.Should().Contain("x:Name=\"PanelLanguage\"");
        wpf.Should().Contain("<ColumnDefinition Width=\"230\"/>");
        wpf.Should().Contain("<ColumnDefinition Width=\"240\"/>");
        wpf.Should().Contain("Height=\"24\" VerticalAlignment=\"Center\"");
    }

    [Fact]
    public void EaseOfAccessOptions_UsesWpfHeaderAndCheckboxRhythm()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.Options.cs"));
        var planner = File.ReadAllText(RepoFile("src", "FreeX.App.Services", "OptionsDialogPlanner.cs"));
        var wpf = File.ReadAllText(RepoFile("src", "FreeX.App.Host", "OptionsDialog.xaml"));
        var styles = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "DialogControlStyles.cs"));
        var sharedTokens = File.ReadAllText(RepoFile("shared", "Free.Shared.Shell", "CompactDialogVisualTokens.cs"));

        source.Should().Contain("OptionsDialogPlanner.EaseSectionRuleBottomMargin");
        source.Should().Contain("OptionsDialogPlanner.EaseCheckBoxBottomMargin");
        source.Should().Contain("OptionsDialogPlanner.EaseCheckBoxHeight");
        source.Should().Contain("free-options-ease-checkbox");
        source.Should().Contain("easePanel.Spacing = 0;");
        source.Should().Contain("ruleTopMargin: OptionsDialogPlanner.EaseSectionRuleTopMargin");
        planner.Should().Contain("public const double EaseCheckBoxBottomMargin = 6;");
        wpf.Should().Contain("<StackPanel x:Name=\"PanelEaseOfAccess\" Visibility=\"Collapsed\">");
        wpf.Should().Contain("Margin=\"0,0,0,6\" FontSize=\"12\"");
        styles.Should().Contain("AvaloniaCompactDialogChrome.CreateCompactCheckBoxTemplate(");
        sharedTokens.Should().Contain("ToggleDisabledBackgroundHex");
        sharedTokens.Should().Contain("ToggleDisabledMarkHex");
    }

    private static string RepoFile(params string[] parts) =>
        Path.Combine([TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx"), .. parts]);
}
