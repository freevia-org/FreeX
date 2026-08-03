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
        fonts.Should().Contain("CustomizeThemeFontsDialogPlanner.BuildInitialState(current)");
        fonts.Should().Contain("CustomizeThemeFontsDialogPlanner.TryBuildResult(");
        fonts.Should().Contain("CustomizeThemeFontsDialogPlanner.CommonFonts");
        fonts.Should().Contain("DialogMessageHelper.ShowWarning");
        fonts.Should().Contain("CustomizeThemeFontsDialogPlanner.DialogWidth");
        fonts.Should().Contain("CustomizeThemeFontsDialogPlanner.DialogMargin");
        fonts.Should().Contain("CustomizeThemeFontsDialogPlanner.LabelColumnWidth");
        fonts.Should().Contain("CustomizeThemeFontsDialogPlanner.FieldMinWidth");
        fonts.Should().Contain("CustomizeThemeFontsDialogPlanner.ActionButtonWidth");

        var avalonia = File.ReadAllText(RepositoryFile("freew", "FreeW.App.Avalonia", "DesignDialogParity.cs"));
        avalonia.Should().Contain("CustomizeThemeFontsDialogPlanner.DialogWidth");
        avalonia.Should().Contain("CustomizeThemeFontsDialogPlanner.DialogMargin");
        avalonia.Should().Contain("CustomizeThemeFontsDialogPlanner.LabelColumnWidth");
        avalonia.Should().Contain("CustomizeThemeFontsDialogPlanner.FieldMinWidth");
        avalonia.Should().Contain("CustomizeThemeFontsDialogPlanner.ActionButtonWidth");
        avalonia.Should().Contain("AvaloniaCompactDialogChrome.DialogSeparatorBrush");
        avalonia.Should().Contain("CreateActionButton(");
        avalonia.Should().Contain("ApplyValidationStatus");
        avalonia.Should().Contain("(validation?.Field == CustomizeThemeFontsDialogField.BodyFont ? _body : _heading).Focus();");
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

    private static string RepositoryFile(params string[] parts)
    {
        var directory = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(directory))
        {
            var candidate = Path.Combine(new[] { directory }.Concat(parts).ToArray());
            if (File.Exists(candidate))
                return candidate;
            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new FileNotFoundException("Could not locate repository file.", Path.Combine(parts));
    }
}
