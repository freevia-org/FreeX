using System.IO;

namespace FreeX.App.Avalonia.Tests;

public sealed class OptionsDialogAdvancedParitySourceTests
{
    [Fact]
    public void AdvancedOptions_UsesSharedMetricsAndWpfRowGeometry()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.Options.cs"));

        source.Should().Contain("OptionsDialogPlanner.CategoryColumnWidth");
        source.Should().Contain("OptionsDialogPlanner.ContentPaddingHorizontal");
        source.Should().Contain("OptionsDialogPlanner.FooterHeight");
        source.Should().Contain("OptionsSectionHeader(OptionsText(\"Options_EditingOptions\"), topMargin: 0)");
        source.Should().Contain("advancedPanel.Spacing = 0;");
        source.Should().Contain("spacing: 0");
        source.Should().Contain("OptionsDialogPlanner.AdvancedDirectionLeftMargin");
        source.Should().Contain("OptionsDialogPlanner.AdvancedObjectsControlWidth");
    }

    [Fact]
    public void AdvancedOptions_PreservesInteractiveStatesAndObjectsSelection()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.Options.cs"));

        source.Should().Contain("isChecked: current.EnableAutoCompleteForCellValues");
        source.Should().Contain("isEnabled: true,");
        source.Should().Contain("AutomationProperties.SetAutomationId(objectsDisplayBox, \"OptionsObjectsDisplayComboBox\")");
        source.Should().Contain("objectsDisplay: objectsDisplayBox.SelectedIndex switch");
        source.Should().Contain("AppOptionsObjectDisplay.Placeholders");
        source.Should().Contain("AppOptionsObjectDisplay.Nothing");
    }

    [Fact]
    public void ProofingOptions_UsesWpfGeometryAndKeyboardCategoryNavigation()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.Options.cs"));
        var wpf = File.ReadAllText(RepoFile("src", "FreeX.App.Host", "OptionsDialog.xaml"));

        source.Should().Contain("Width = OptionsDialogPlanner.ProofingContentWidth");
        source.Should().Contain("Height = OptionsDialogPlanner.ProofingWordsListHeight");
        source.Should().Contain("proofingPanel.Spacing = 0;");
        source.Should().Contain("HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden");
        source.Should().Contain("VerticalContentAlignment = AvaloniaVerticalAlignment.Top");
        source.Should().Contain("Width = OptionsDialogPlanner.FooterButtonWidth");
        source.Should().Contain("dialog.Opened += (_, _) => categoryRows[0].Focus();");
        source.Should().Contain("Key.Up or Key.Left");
        source.Should().Contain("Key.Down or Key.Right");
        source.Should().Contain("Key.Home");
        source.Should().Contain("Key.End");
        source.Should().Contain("Key.Enter or Key.Space");
        source.Should().Contain("args.Handled = true;");
        source.Should().Contain("SpellCheckWorkflowPlanner.AddCustomDictionaryWord");
        source.Should().Contain("SpellCheckWorkflowPlanner.RemoveCustomDictionaryWordAndSelectNext");
        source.Should().Contain("SpellCheckWorkflowPlanner.ClearCustomDictionaryWords");
        source.Should().Contain("proofingAddButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));");

        wpf.Should().Contain("Height=\"108\"");
        wpf.Should().Contain("Width=\"78\" Height=\"26\"");
        wpf.Should().Contain("Width=\"92\" Height=\"26\"");
        wpf.Should().Contain("Width=\"82\" Height=\"26\"");
        wpf.Should().Contain("Width=\"80\" Height=\"26\"");
        wpf.Should().Contain("Padding=\"16,10\"");
    }

    [Fact]
    public void TrustCenter_UsesWpfControlStatesGeometryAndDeferredSettingsRoute()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.Options.cs"));

        source.Should().Contain("IsChecked = current.CrashAnalyticsEnabled");
        source.Should().Contain("OptionsCrashAnalyticsCheckBox");
        source.Should().Contain("trustCenterPanel.Width = OptionsDialogPlanner.GeneralContentWidth;");
        source.Should().Contain("OptionsButton(OptionsText(\"Options_TrustCenterSettings\"), width: 170)");
        source.Should().Contain("AvaloniaUserMessageDialog.ShowWarningAsync");
        source.Should().Contain("UiText.Get(\"DeferredCommand_TrustCenter_Body\")");
        source.Should().Contain("crashAnalyticsEnabled: crashAnalyticsBox.IsChecked == true");
        source.Should().Contain("Key.Enter or Key.Space");
        source.Should().Contain("IsDefault = true");
        source.Should().Contain("IsCancel = true");
        source.Should().Contain("cancelButton.Click += (_, _) => dialog.Close();");
        source.Should().Contain("await dialog.ShowDialog(this);");
    }

    private static string RepoFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FreeX.slnx")))
            directory = directory.Parent;

        if (directory is null)
            throw new DirectoryNotFoundException("Could not find repository root containing FreeX.slnx.");

        return Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
    }
}
