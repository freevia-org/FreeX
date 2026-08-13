using System.IO;

namespace FreeW.App.Host.Tests;

public sealed class DesignDialogParitySourceTests
{
    [Fact]
    public void WpfCustomThemeDialogs_UseSharedDesignPlanners()
    {
        var colors = File.ReadAllText(RepositoryFile("freew", "FreeW.App.Host", "CustomizeThemeColorsDialog.cs"));
        colors.Should().Contain("CustomizeThemeColorsDialogPlanner.BuildInitialState(currentTheme)");
        colors.Should().Contain("CustomizeThemeColorsDialogPlanner.TryBuildResult(");
        colors.Should().Contain("CustomizeThemeColorsDialogPlanner.Slots");

        var fonts = File.ReadAllText(RepositoryFile("freew", "FreeW.App.Host", "CustomizeThemeFontsDialog.cs"));
        fonts.Should().Contain("CustomizeThemeFontsDialogPlanner.CreateSession(current)");
        fonts.Should().Contain("_session.PlanAcceptance(");
        fonts.Should().Contain("CustomizeThemeFontsDialogPlanner.CommonFonts");
        fonts.Should().Contain("acceptance.FocusField == CustomizeThemeFontsDialogField.BodyFont");
        fonts.Should().Contain("DialogMessageHelper.ShowWarning");
        fonts.Should().Contain("CustomizeThemeFontsDialogPlanner.DialogWidth");
        fonts.Should().Contain("CustomizeThemeFontsDialogPlanner.DialogMargin");
        fonts.Should().Contain("CustomizeThemeFontsDialogPlanner.LabelColumnWidth");
        fonts.Should().Contain("CustomizeThemeFontsDialogPlanner.FieldMinWidth");
        fonts.Should().Contain("CustomizeThemeFontsDialogPlanner.ActionButtonWidth");
        fonts.Should().NotContain("CustomizeThemeFontsDialogPlanner.BuildInitialState(");
        fonts.Should().NotContain("CustomizeThemeFontsDialogPlanner.TryBuildResult(");

        var avalonia = File.ReadAllText(RepositoryFile("freew", "FreeW.App.Avalonia", "DesignDialogParity.cs"));
        avalonia.Should().Contain("CustomizeThemeFontsDialogPlanner.CreateSession(current)");
        avalonia.Should().Contain("_session.PlanAcceptance(");
        avalonia.Should().Contain("CustomizeThemeFontsDialogPlanner.DialogWidth");
        avalonia.Should().Contain("CustomizeThemeFontsDialogPlanner.DialogMargin");
        avalonia.Should().Contain("CustomizeThemeFontsDialogPlanner.LabelColumnWidth");
        avalonia.Should().Contain("CustomizeThemeFontsDialogPlanner.FieldMinWidth");
        avalonia.Should().Contain("CustomizeThemeFontsDialogPlanner.ActionButtonWidth");
        avalonia.Should().Contain("AvaloniaCompactDialogChrome.DialogSeparatorBrush");
        avalonia.Should().Contain("CreateActionButton(");
        avalonia.Should().Contain("ApplyValidationStatus");
        avalonia.Should().Contain("acceptance.FocusField == CustomizeThemeFontsDialogField.BodyFont");
    }

    [Fact]
    public void DesignShellWiringGaps_AreRecordedOutsideForbiddenRibbonFiles()
    {
        var avalonia = File.ReadAllText(RepositoryFile("freew", "FreeW.App.Avalonia", "DesignDialogParity.cs"));
        var sharedSpacing = File.ReadAllText(RepositoryFile("freew", "FreeW.App.Avalonia", "PageLayoutDialogs.cs"));
        avalonia.Should().Contain("public sealed class CustomizeThemeColorsDialog");
        avalonia.Should().Contain("public sealed class CustomizeThemeFontsDialog");
        sharedSpacing.Should().Contain("public sealed class CustomParagraphSpacingDialog");
        avalonia.Should().Contain("public sealed class PageColorDialog");
        avalonia.Should().Contain("public sealed class SetAsDefaultConfirmationDialog");
    }

    private static string RepositoryFile(params string[] parts) =>
        TestWorkspaceFileLocator.Find(parts);
}
