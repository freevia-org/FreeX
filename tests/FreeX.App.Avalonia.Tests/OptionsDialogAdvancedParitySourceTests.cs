using System.Threading;

using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Free.Shared.Shell.Avalonia;

using System.IO;

namespace FreeX.App.Avalonia.Tests;

public sealed class OptionsDialogAdvancedParitySourceTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

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
    public void ViewOptions_UsesWpfHeaderRhythmAndCaptureFixture()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.Options.cs"));
        var wpf = File.ReadAllText(RepoFile("tools", "FreeX.ParityCapture.Wpf", "Capture", "ParityCapture.cs"));
        var fixture = File.ReadAllText(RepoFile(
            "tools", "FreeX.ParityCapture.Support", "Services", "OptionsDialogParityFixture.cs"));

        source.Should().Contain("OptionsDialogParityFixture.Create()");
        source.Should().Contain("OptionsSectionHeader(OptionsText(\"Options_WorkbookViewOptions\"), topMargin: 0, bottomMargin: 12)");
        source.Should().Contain("viewPanel.Spacing = 0;");
        source.Should().Contain("RowDefinitions = new RowDefinitions(\"*,Auto\")");
        wpf.Should().Contain("OptionsDialogParityFixture.Create()");
        fixture.Should().Contain("ShowFormulaBar = true");
        fixture.Should().Contain("FormulaBarExpanded = false");
    }

    [Fact]
    public void AdvancedOptions_PreservesInteractiveStatesAndObjectsSelection()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.Options.cs"));

        source.Should().Contain("isChecked: current.EnableAutoCompleteForCellValues");
        source.Should().Contain("isEnabled: true,");
        source.Should().Contain("AutomationProperties.SetAutomationId(objectsDisplayBox, \"OptionsObjectsDisplayComboBox\")");
        source.Should().Contain("objectsDisplay: OptionsDialogPlanner.IndexToObjectDisplay(objectsDisplayBox.SelectedIndex)");
        source.Should().Contain("OptionsDialogPlanner.ObjectDisplayToIndex(current.ObjectsDisplay)");
    }

    [Fact]
    public void AdvancedOptions_FillHandleAndCellDragAndDropUsesSharedPersistedState()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.Options.cs"));

        source.Should().Contain("isChecked: current.EnableFillHandleAndCellDragAndDrop");
        source.Should().Contain("OptionsEnableFillHandleAndCellDragAndDropCheckBox");
        source.Should().Contain("enableFillHandleAndCellDragAndDrop: advancedFillHandleBox.IsChecked == true");
        source.Should().NotContain("isChecked: true,\n            isEnabled: false");
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
        source.Should().Contain("var customDictionaryEditor = optionsDialogSession.CustomDictionary;");
        source.Should().Contain("customDictionaryEditor.AddPendingWord();");
        source.Should().Contain("customDictionaryEditor.RemoveSelectedWord();");
        source.Should().Contain("customDictionaryEditor.Clear();");
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

    [Fact]
    public void CustomizeRibbon_UsesEnabledOwnedLocalizedDeferredRoute()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.Options.cs"));
        var wpf = File.ReadAllText(RepoFile("src", "FreeX.App.Host", "OptionsDialog.xaml"));

        source.Should().Contain("var customizeRibbonImportExportButton = OptionsButton(OptionsText(\"Options_ImportExport\"), width: 130);");
        source.Should().Contain("AutomationProperties.SetAutomationId(customizeRibbonImportExportButton, \"RibbonImportExportButton\");");
        source.Should().Contain("customizeRibbonImportExportButton.Click += async (_, _) =>");
        source.Should().Contain("AvaloniaUserMessageDialog.ShowWarningAsync(");
        source.Should().Contain("UiText.Get(\"DeferredCommand_RibbonCustomization_Body\")");
        source.Should().Contain("UiText.Get(\"DeferredCommand_RibbonCustomization_Title\")");
        source.Should().Contain("customizeRibbonImportExportButton);");
        source.Should().NotContain("OptionsButton(OptionsText(\"Options_ImportExport\"), width: 130, isEnabled: false)");

        wpf.Should().Contain("x:Name=\"RibbonImportExportButton\"");
        wpf.Should().Contain("Width=\"130\" Height=\"26\"");
        wpf.Should().Contain("Click=\"RibbonImportExportButton_Click\"");

        source.Should().Contain("Key.Enter or Key.Space");
        source.Should().Contain("dialog.Opened += (_, _) => categoryRows[0].Focus();");
        source.Should().Contain("IsDefault = true");
        source.Should().Contain("IsCancel = true");
        source.Should().Contain("cancelButton.Click += (_, _) => dialog.Close();");
        source.Should().Contain("await dialog.ShowDialog(this);");
    }

    [Fact]
    public void AddIns_UsesWpfGeometryAndEnabledOwnedDeferredRoute()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.Options.cs"));
        var planner = File.ReadAllText(RepoFile("src", "FreeX.App.Services", "OptionsDialogPlanner.cs"));
        var wpf = File.ReadAllText(RepoFile("src", "FreeX.App.Host", "OptionsDialog.xaml"));

        planner.Should().Contain("AddInsSectionHeaderTopMargin");
        planner.Should().Contain("AddInsSectionRuleBottomMargin");
        source.Should().Contain("var addInsGoButton = OptionsButton(");
        source.Should().Contain("OptionsDialogPlanner.AddInsGoButtonWidth");
        source.Should().Contain("AutomationProperties.SetAutomationId(addInsGoButton, \"AddInsGoButton\")");
        source.Should().Contain("addInsGoButton.Click += async (_, _) =>");
        source.Should().Contain("UiText.Get(\"DeferredCommand_OfficeAddIns_Body\")");
        source.Should().Contain("UiText.Get(\"DeferredCommand_OfficeAddIns_Title\")");
        source.Should().Contain("addInsPanel.Spacing = 0;");
        source.Should().NotContain("OptionsButton(OptionsText(\"Options_Go\"), width: 72, isEnabled: false)");

        wpf.Should().Contain("x:Name=\"PanelAddIns\"");
        wpf.Should().Contain("x:Name=\"AddInsGoButton\"");
        wpf.Should().Contain("Width=\"70\" Height=\"26\"");
        wpf.Should().Contain("Click=\"AddInsGoButton_Click\"");
    }

    [Fact]
    public async Task CustomizeRibbonImportExport_UsesOwnedModalAndClosesThroughClickAndKeyboard()
    {
        await Session.Dispatch(async () =>
        {
            var owner = new MainWindow([]);
            Task? optionsTask = null;
            try
            {
                owner.Show();
                optionsTask = owner.ShowOptionsDialogForTestAsync();
                await DrainInputAsync();

                var options = FindOwnedWindow(owner, "OptionsDialog");
                var categoryList = FindByAutomationId<StackPanel>(options, "OptionsCategoryList");
                categoryList.Should().NotBeNull();
                categoryList!.Tag.Should().BeOfType<Action<int>>().Subject(8);
                options.UpdateLayout();

                var importExport = FindByAutomationId<Button>(options, "RibbonImportExportButton");
                importExport.Should().NotBeNull();
                importExport!.IsEnabled.Should().BeTrue();

                importExport.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await DrainInputAsync();
                var message = FindOwnedMessage(options);
                message.Owner.Should().BeSameAs(options);
                message.GetVisualDescendants().OfType<Button>().Single(button => button.IsDefault)
                    .IsCancel.Should().BeTrue();

                var messageOk = message.GetVisualDescendants().OfType<Button>().Single(button => button.IsDefault);
                messageOk.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await DrainInputAsync();
                message.IsVisible.Should().BeFalse();
                options.IsVisible.Should().BeTrue();

                importExport.Focus().Should().BeTrue();
                MainWindow.SendDialogKeyForTest(options, Key.Enter, RawInputModifiers.None, out var inputError)
                    .Should().BeTrue(inputError);
                await DrainInputAsync();
                message = FindOwnedMessage(options);
                message.Owner.Should().BeSameAs(options);

                MainWindow.SendDialogKeyForTest(message, Key.Escape, RawInputModifiers.None, out inputError)
                    .Should().BeTrue(inputError);
                await DrainInputAsync();
                message.IsVisible.Should().BeFalse();

                FindByAutomationId<Button>(options, "OptionsCancelButton")!
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await optionsTask;
                options.IsVisible.Should().BeFalse();
            }
            finally
            {
                foreach (var dialog in owner.OwnedWindows.ToArray())
                {
                    if (dialog.IsVisible)
                        dialog.Close();
                }

                owner.AllowCloseWithoutDirtyPromptForParityCapture();

                if (owner.IsVisible)
                    owner.Close();

                if (optionsTask is { IsCompleted: false })
                    await optionsTask;
            }
        }, CancellationToken.None);
    }

    private static Window FindOwnedWindow(MainWindow owner, string automationId) =>
        owner.OwnedWindows.Single(window =>
            string.Equals(AutomationProperties.GetAutomationId(window), automationId, StringComparison.Ordinal));

    private static T? FindByAutomationId<T>(Control root, string automationId)
        where T : Control =>
        root.GetVisualDescendants().OfType<T>().FirstOrDefault(control =>
            string.Equals(AutomationProperties.GetAutomationId(control), automationId, StringComparison.Ordinal));

    private static AvaloniaUserMessageDialog FindOwnedMessage(Window owner) =>
        owner.OwnedWindows.OfType<AvaloniaUserMessageDialog>().Single(window => window.IsVisible);

    private static async Task DrainInputAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Input);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
    }

    private static string RepoFile(params string[] parts) =>
        Path.Combine([TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx"), .. parts]);
}
