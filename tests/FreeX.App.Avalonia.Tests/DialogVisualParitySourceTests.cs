using System.IO;

namespace FreeX.App.Avalonia.Tests;

public sealed class DialogVisualParitySourceTests
{
    [Fact]
    public void FindReplaceDialog_UsesWpfColumnAndResultSurfaceMetrics()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("AvaloniaCompactDialogChrome.ApplyWindow(dialog);");
        source.Should().Contain("var findBox = new TextBox { Text = _session.LastFindText, MinWidth = 260 };");
        source.Should().Contain("findFormatButton.Margin = new Thickness(8, 0, 0, 0);");
        source.Should().Contain("findChooseFormatButton.Margin = new Thickness(6, 0, 0, 0);");
        source.Should().Contain("Height = 108,");
        source.Should().Contain("Width = 88,");
        source.Should().Contain("new Thickness(6, 1)");
        source.Should().Contain("BorderBrush = Brush(68, 114, 196)");
        source.Should().Contain("Background = Brush(242, 242, 242)");
        source.Should().Contain("optionsHeader");
        source.Should().Contain("resultsHeader.Height = 24;");
        source.Should().Contain("button.CornerRadius = new CornerRadius(0);");
        source.Should().Contain("textBox.CornerRadius = new CornerRadius(0);");
        source.Should().Contain("optionsHeader.Width = 88;");
        source.Should().Contain("optionsHeader.Background = Brushes.White;");
        source.Should().Contain("? Fr(\"FindReplace_OptionsExpanded\", \"Options <<\")");
        source.Should().Contain(": Fr(\"FindReplace_Options\", \"Options >>\")");
        source.Should().Contain("AutomationProperties.SetName(optionsHeader, optionsHeaderText.Text);");
        source.Should().Contain("dialog.Opened += (_, _) => resultsList.Background = Brush(242, 242, 242);");
    }

    [Fact]
    public void GoToSpecialDialog_UsesCompactRowsAndBottomDockedActions()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("numbersBox.Opacity = enabled ? 1 : 0.7;");
        source.Should().Contain("AvaloniaCompactDialogChrome.ApplyCompactRadioButton(button, AvaloniaCompactDialogChrome.WindowsStyle);");
        source.Should().Contain("AvaloniaCompactDialogChrome.ApplyCompactCheckBox(numbersBox, AvaloniaCompactDialogChrome.WindowsStyle);");
        source.Should().Contain("AvaloniaCompactDialogChrome.ApplyGroupBox(availableGroup, borderBrush: Brush(213, 223, 229));");
        source.Should().Contain("AvaloniaCompactDialogChrome.ApplyGroupBox(valueTypeGroup, borderBrush: Brush(213, 223, 229));");
        source.Should().Contain("GoToSpecialDialogPlanner.AvaloniaChoiceGroupTopMargin");
        source.Should().Contain("GoToSpecialDialogPlanner.AvaloniaChoiceGroupBottomMargin");
        source.Should().Contain("GoToSpecialDialogPlanner.AvaloniaChoiceGroupHorizontalPadding");
        source.Should().Contain("GoToSpecialDialogPlanner.AvaloniaChoiceGroupBottomPadding");
        source.Should().Contain("GoToSpecialDialogPlanner.AvaloniaValueTypeGroupBottomPadding");
        source.Should().Contain("GoToSpecialDialogPlanner.AvaloniaValueTypeSpacing");
        source.Should().Contain("GoToSpecialDialogPlanner.AvaloniaChoiceButtonRightMargin");
        source.Should().Contain("GoToSpecialDialogPlanner.AvaloniaChoiceButtonBottomMargin");
        source.Should().Contain("GoToSpecialDialogPlanner.AvaloniaContentLeftMargin");
        source.Should().Contain("GoToSpecialDialogPlanner.AvaloniaContentTopMargin");
        source.Should().Contain("GoToSpecialDialogPlanner.AvaloniaContentRightMargin");
        source.Should().Contain("Margin = new Thickness(0, 0, 0, 7),");
        source.Should().Contain("GoToSpecialDialogPlanner.ActionRowTopMargin");
        source.Should().Contain("GoToSpecialDialogPlanner.ActionRowRightMargin");
        source.Should().Contain("GoToSpecialDialogPlanner.ActionRowBottomMargin");
        source.Should().Contain("ApplyGoToSpecialButtonSize(okButton);");
        source.Should().Contain("ApplyGoToSpecialButtonSize(cancelButton);");
        source.Should().Contain("var root = new DockPanel { Margin = new Thickness(0) };");
        source.Should().Contain("DockPanel.SetDock(buttonRow, Dock.Bottom);");

        var wpfSource = File.ReadAllText(RepoFile("src", "FreeX.App.Host", "GoToSpecialDialog.cs"));
        wpfSource.Should().Contain("GoToSpecialDialogPlanner.ContentMargin");

        var chrome = File.ReadAllText(RepoFile("shared", "Free.Shared.Shell.Avalonia", "AvaloniaCompactDialogChrome.cs"));
        chrome.Should().Contain("IBrush? borderBrush = null");
        chrome.Should().Contain("Color.FromRgb(198, 215, 232)");
        chrome.Should().Contain("groupBox.BorderBrush = borderBrush ?? GroupBoxBorderBrush;");
    }

    [Fact]
    public void SortOptionsDialog_UsesSharedLocalizationAndReferenceCaptureState()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var captureSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.ParityCapture.cs"));

        source.Should().Contain("Title = UiText.Get(\"SortOptions_SortOptions\")");
        source.Should().Contain("Content = UiText.Get(\"SortOptions_CaseSensitive\")");
        source.Should().Contain("UiText.Get(\"SortOptions_FirstKeySortOrderLabel\")");
        source.Should().Contain("UiText.Get(\"SortOptions_FirstKeyJanToDecShort\")");
        source.Should().Contain("Content = UiText.Get(\"SortOptions_SortTopToBottom\")");
        source.Should().Contain("Content = UiText.Get(\"SortOptions_SortLeftToRight\")");
        source.Should().Contain("Header = StripDisplayMnemonic(UiText.Get(\"SortOptions_Orientation\"))");
        source.Should().Contain("FirstKeySortOrder: firstKeyBox.SelectedItem is SortDialogComboItem<string> choice");
        source.Should().Contain("AutomationProperties.SetAutomationId(dialog, \"SortOptionsDialog\")");
        source.Should().Contain("AutomationProperties.SetAutomationId(firstKeyBox, \"SortOptionsFirstKeySortOrderBox\")");
        source.Should().Contain("AutomationProperties.SetAutomationId(leftToRightButton, \"SortOptionsLeftToRightRadio\")");

        captureSource.Should().Contain("CaseSensitive: true");
        captureSource.Should().Contain("LeftToRight: true");
        captureSource.Should().Contain("FirstKeySortOrder: \"Jan, Feb, Mar, Apr, May, Jun, Jul, Aug, Sep, Oct, Nov, Dec\"");
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
