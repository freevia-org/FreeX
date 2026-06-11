using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class WorkbookThemeDialogXamlTests
{
    [Fact]
    public void Dialog_ExposesThemePresetButtonsBackedByWorkflow()
    {
        var xaml = XamlLocalizationTestHelper.ReadLocalizedXaml("WorkbookThemeDialog.xaml");
        var source = DialogSourceTestSupport.ReadHostSources("WorkbookThemeDialog.xaml.cs");

        xaml.Should().Contain("x:Name=\"OfficePresetButton\"");
        xaml.Should().Contain("x:Name=\"ColorfulPresetButton\"");
        xaml.Should().Contain("x:Name=\"GrayscalePresetButton\"");
        xaml.Should().Contain("Click=\"OfficePresetButton_Click\"");
        xaml.Should().Contain("Click=\"ColorfulPresetButton_Click\"");
        xaml.Should().Contain("Click=\"GrayscalePresetButton_Click\"");

        source.Should().Contain("OfficePresetButton_Click");
        source.Should().Contain("WorkbookThemeDialogMode.Colors => WorkbookThemeWorkflow.ApplyOfficeColors");
        source.Should().Contain("WorkbookThemeDialogMode.Effects => ReadCurrentDialogThemeOrInitial().WithEffects(WorkbookTheme.Office.EffectsName)");
        source.Should().Contain("ColorfulPresetButton_Click");
        source.Should().Contain("WorkbookThemeDialogMode.Colors => WorkbookThemeWorkflow.ApplyColorfulColors");
        source.Should().Contain("WorkbookThemeDialogMode.Effects => ReadCurrentDialogThemeOrInitial().WithEffects(\"Subtle\")");
        source.Should().Contain("_ => WorkbookThemeWorkflow.CreateColorfulTheme()");
        source.Should().Contain("GrayscalePresetButton_Click");
        source.Should().Contain("WorkbookThemeDialogMode.Colors => WorkbookThemeWorkflow.ApplyGrayscaleColors");
        source.Should().Contain("WorkbookThemeDialogMode.Effects => ReadCurrentDialogThemeOrInitial().WithEffects(\"Refined\")");
        source.Should().Contain("_ => WorkbookThemeWorkflow.CreateGrayscaleTheme()");
    }

    [Fact]
    public void ColorsMode_PresetsApplyPaletteOnlyAndHideThemeMetadata()
    {
        StaTestRunner.Run(() =>
        {
            var initialTheme = WorkbookTheme.Office
                .WithName("Keep Theme")
                .WithFonts("Georgia", "Verdana")
                .WithEffects("Refined");

            var dialog = new WorkbookThemeDialog(initialTheme, WorkbookThemeDialogMode.Colors);
            try
            {
                ((Grid)dialog.FindName("ThemeMetadataPanel")).Visibility.Should().Be(Visibility.Collapsed);
                dialog.Title.Should().Be("Theme Colors");

                DialogSourceTestSupport.ClickButton((Button)dialog.FindName("GrayscalePresetButton"));
                DialogSourceTestSupport.ClickButtonAllowingNonModalDialogResult((Button)dialog.FindName("SaveButton"));

                dialog.ResultTheme.Name.Should().Be("Keep Theme");
                dialog.ResultTheme.MajorFontName.Should().Be("Georgia");
                dialog.ResultTheme.MinorFontName.Should().Be("Verdana");
                dialog.ResultTheme.EffectsName.Should().Be("Refined");
                dialog.ResultTheme.GetColor(WorkbookThemeColorSlot.Accent1).Should().Be(new CellColor(89, 89, 89));
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void EffectsMode_PresetsApplyEffectsOnlyAndHideColorsEditor()
    {
        StaTestRunner.Run(() =>
        {
            var initialTheme = WorkbookTheme.Office
                .WithName("Keep Theme")
                .WithFonts("Georgia", "Verdana")
                .WithColor(WorkbookThemeColorSlot.Accent1, new CellColor(10, 20, 30));

            var dialog = new WorkbookThemeDialog(initialTheme, WorkbookThemeDialogMode.Effects);
            try
            {
                ((UniformGrid)dialog.FindName("ThemeColorsPanel")).Visibility.Should().Be(Visibility.Collapsed);
                ((TextBlock)dialog.FindName("ThemeDialogTitle")).Text.Should().Be("Effects");
                ((Button)dialog.FindName("ColorfulPresetButton")).Content.Should().Be("Subtle");

                DialogSourceTestSupport.ClickButton((Button)dialog.FindName("ColorfulPresetButton"));
                DialogSourceTestSupport.ClickButtonAllowingNonModalDialogResult((Button)dialog.FindName("SaveButton"));

                dialog.ResultTheme.Name.Should().Be("Keep Theme");
                dialog.ResultTheme.MajorFontName.Should().Be("Georgia");
                dialog.ResultTheme.MinorFontName.Should().Be("Verdana");
                dialog.ResultTheme.EffectsName.Should().Be("Subtle");
                dialog.ResultTheme.GetColor(WorkbookThemeColorSlot.Accent1).Should().Be(new CellColor(10, 20, 30));
            }
            finally
            {
                dialog.Close();
            }
        });
    }
}
